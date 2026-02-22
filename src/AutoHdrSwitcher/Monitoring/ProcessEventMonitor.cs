using System.Management;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Runtime.ExceptionServices;
using AutoHdrSwitcher.Logging;

namespace AutoHdrSwitcher.Monitoring;

public sealed class ProcessEventMonitor : IDisposable
{
    private const string ScopePath = @"\\.\root\CIMV2";
    private const string StartTraceQuery = "SELECT * FROM Win32_ProcessStartTrace";
    private const string StopTraceQuery = "SELECT * FROM Win32_ProcessStopTrace";
    private const string StartInstanceQuery =
        "SELECT * FROM __InstanceCreationEvent WITHIN 1 WHERE TargetInstance ISA 'Win32_Process'";
    private const string StopInstanceQuery =
        "SELECT * FROM __InstanceDeletionEvent WITHIN 1 WHERE TargetInstance ISA 'Win32_Process'";

    private readonly BlockingCollection<Action> _mtaWorkQueue = new();
    private readonly Thread _mtaThread;
    private readonly ManualResetEventSlim _mtaReady = new(false);
    private ManagementEventWatcher? _startWatcher;
    private ManagementEventWatcher? _stopWatcher;
    private ManagementScope? _scope;
    private bool _running;
    private bool _disposed;
    private int _mtaThreadId;
    private bool _traceAccessDenied;
    private ProcessEventStreamMode _currentMode = ProcessEventStreamMode.Unavailable;

    public event EventHandler<ProcessEventNotification>? ProcessEventReceived;
    public event EventHandler? StreamModeChanged;

    public ProcessEventStreamMode CurrentMode => _currentMode;
    public bool IsTraceRetrySuppressed => _traceAccessDenied;

    public ProcessEventMonitor()
    {
        _mtaThread = new Thread(MtaThreadMain)
        {
            Name = "AutoHdrSwitcher.ProcessEventMonitor.MTA",
            IsBackground = true
        };
        _mtaThread.SetApartmentState(ApartmentState.MTA);
        _mtaThread.Start();

        if (!_mtaReady.Wait(TimeSpan.FromSeconds(5)))
        {
            throw new InvalidOperationException("Failed to initialize WMI MTA worker thread.");
        }
    }

    public bool Start(out string error)
    {
        var result = InvokeOnMta(StartCore);
        error = result.Error;
        return result.Success;
    }

    public bool TrySwitchToTrace(out string error)
    {
        var result = InvokeOnMta(TrySwitchToTraceCore);
        error = result.Error;
        return result.Success;
    }

    public void Stop()
    {
        if (_disposed)
        {
            return;
        }

        InvokeOnMta(StopCore);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            Stop();
        }
        catch (Exception ex)
        {
            AppLogger.Error("Process event monitor stop failed during dispose.", ex);
        }

        _disposed = true;
        _mtaWorkQueue.CompleteAdding();
        if (Environment.CurrentManagedThreadId != _mtaThreadId)
        {
            _mtaThread.Join(TimeSpan.FromSeconds(3));
        }

        _mtaReady.Dispose();
        _mtaWorkQueue.Dispose();
        GC.SuppressFinalize(this);
    }

    private void MtaThreadMain()
    {
        _mtaThreadId = Environment.CurrentManagedThreadId;
        _mtaReady.Set();

        try
        {
            foreach (var action in _mtaWorkQueue.GetConsumingEnumerable())
            {
                action();
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error("Unexpected fatal exception in WMI MTA worker thread.", ex);
        }
    }

    private (bool Success, string Error) StartCore()
    {
        if (_running)
        {
            return (true, string.Empty);
        }

        _traceAccessDenied = false;
        AppLogger.Info("Starting process event stream.");
        if (TryCreateAndStartWatchers(
                StartTraceQuery,
                StopTraceQuery,
                out var traceStartWatcher,
                out var traceStopWatcher,
                out var traceScope,
                out var traceError,
                out var traceAccessDenied))
        {
            _startWatcher = traceStartWatcher;
            _stopWatcher = traceStopWatcher;
            _scope = traceScope;
            _running = true;
            SetCurrentMode(ProcessEventStreamMode.Trace);
            AppLogger.Info("Trace event stream started.");
            return (true, string.Empty);
        }

        _traceAccessDenied = traceAccessDenied;
        if (_traceAccessDenied)
        {
            AppLogger.Warn("Trace event stream permission denied. Auto-retry to trace will be suppressed for this session.");
        }

        AppLogger.Warn($"Trace event stream start failed. {traceError}");
        if (TryCreateAndStartWatchers(
                StartInstanceQuery,
                StopInstanceQuery,
                out var instanceStartWatcher,
                out var instanceStopWatcher,
                out var instanceScope,
                out var instanceError,
                out _))
        {
            _startWatcher = instanceStartWatcher;
            _stopWatcher = instanceStopWatcher;
            _scope = instanceScope;
            _running = true;
            SetCurrentMode(ProcessEventStreamMode.Instance);
            AppLogger.Warn("Using instance-event fallback stream.");
            return (true, string.Empty);
        }

        var error = $"trace query failed: {traceError}; instance query failed: {instanceError}";
        SetCurrentMode(ProcessEventStreamMode.Unavailable);
        AppLogger.Error($"Event stream unavailable. {error}");
        return (false, error);
    }

    private (bool Success, string Error) TrySwitchToTraceCore()
    {
        if (!_running)
        {
            return (false, "event stream is not running");
        }

        if (_currentMode == ProcessEventStreamMode.Trace)
        {
            return (true, string.Empty);
        }

        if (_traceAccessDenied)
        {
            return (false, "trace access denied; retry suppressed for current session");
        }

        if (!TryCreateAndStartWatchers(
                StartTraceQuery,
                StopTraceQuery,
                out var traceStartWatcher,
                out var traceStopWatcher,
                out var traceScope,
                out var error,
                out var accessDenied))
        {
            if (accessDenied)
            {
                _traceAccessDenied = true;
                AppLogger.Warn("Trace recovery permission denied. Further auto-retries will be suppressed for this session.");
            }

            AppLogger.Warn($"Trace recovery attempt failed. {error}");
            return (false, error);
        }

        var oldStartWatcher = _startWatcher;
        var oldStopWatcher = _stopWatcher;
        _startWatcher = traceStartWatcher;
        _stopWatcher = traceStopWatcher;
        _scope = traceScope;
        _traceAccessDenied = false;
        SetCurrentMode(ProcessEventStreamMode.Trace);
        DisposeWatcher(oldStartWatcher);
        DisposeWatcher(oldStopWatcher);
        AppLogger.Info("Recovered from instance stream to trace stream.");
        return (true, string.Empty);
    }

    private void StopCore()
    {
        if (!_running && _startWatcher is null && _stopWatcher is null)
        {
            SetCurrentMode(ProcessEventStreamMode.Unavailable);
            return;
        }

        AppLogger.Info("Stopping process event stream.");
        DisposeWatchersCore();
    }

    private bool TryCreateAndStartWatchers(
        string startQuery,
        string stopQuery,
        out ManagementEventWatcher? startWatcher,
        out ManagementEventWatcher? stopWatcher,
        out ManagementScope? scope,
        out string error,
        out bool accessDenied)
    {
        startWatcher = null;
        stopWatcher = null;
        scope = null;
        accessDenied = false;
        ManagementEventWatcher? localStartWatcher = null;
        ManagementEventWatcher? localStopWatcher = null;
        ManagementScope? localScope = null;
        try
        {
            var options = new ConnectionOptions
            {
                Impersonation = ImpersonationLevel.Impersonate,
                EnablePrivileges = true
            };
            localScope = new ManagementScope(ScopePath, options);
            localScope.Connect();

            localStartWatcher = new ManagementEventWatcher(localScope, new WqlEventQuery(startQuery));
            localStopWatcher = new ManagementEventWatcher(localScope, new WqlEventQuery(stopQuery));

            localStartWatcher.EventArrived += (_, args) => HandleEvent("start", args);
            localStopWatcher.EventArrived += (_, args) => HandleEvent("stop", args);

            localStartWatcher.Start();
            localStopWatcher.Start();

            startWatcher = localStartWatcher;
            stopWatcher = localStopWatcher;
            scope = localScope;
            error = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            DisposeWatcher(localStartWatcher);
            DisposeWatcher(localStopWatcher);
            accessDenied = IsAccessDenied(ex);
            error = DescribeException(ex);
            AppLogger.Error($"Failed to start watcher pair. startQuery={startQuery}; stopQuery={stopQuery}", ex);
            return false;
        }
    }

    private void HandleEvent(string eventType, EventArrivedEventArgs args)
    {
        try
        {
            if (!TryReadProcessInfo(args.NewEvent, out var processId, out var processName))
            {
                return;
            }

            ProcessEventReceived?.Invoke(this, new ProcessEventNotification
            {
                EventType = eventType,
                ProcessId = processId,
                ProcessName = processName
            });
        }
        catch
        {
            // Ignore malformed event payloads.
        }
    }

    private static bool TryReadProcessInfo(
        ManagementBaseObject? eventData,
        out int processId,
        out string processName)
    {
        processId = 0;
        processName = string.Empty;
        if (eventData is null)
        {
            return false;
        }

        if (TryReadDirectEventInfo(eventData, out processId, out processName))
        {
            return true;
        }

        return TryReadTargetInstanceInfo(eventData, out processId, out processName);
    }

    private static bool TryReadDirectEventInfo(
        ManagementBaseObject eventData,
        out int processId,
        out string processName)
    {
        processId = 0;
        processName = string.Empty;

        var processIdValue = GetPropertyValue(eventData, "ProcessID", "ProcessId");
        var processNameValue = GetPropertyValue(eventData, "ProcessName", "Name");
        if (processIdValue is null && processNameValue is null)
        {
            return false;
        }

        processId = ParseProcessId(processIdValue);
        processName = Convert.ToString(processNameValue) ?? string.Empty;
        return true;
    }

    private static bool TryReadTargetInstanceInfo(
        ManagementBaseObject eventData,
        out int processId,
        out string processName)
    {
        processId = 0;
        processName = string.Empty;

        var targetInstance = GetPropertyValue(eventData, "TargetInstance") as ManagementBaseObject;
        if (targetInstance is null)
        {
            return false;
        }

        var processIdValue = GetPropertyValue(targetInstance, "ProcessID", "ProcessId", "Handle");
        var processNameValue = GetPropertyValue(targetInstance, "ProcessName", "Name", "Caption");
        processId = ParseProcessId(processIdValue);
        processName = Convert.ToString(processNameValue) ?? string.Empty;
        return processId != 0 || !string.IsNullOrWhiteSpace(processName);
    }

    private static object? GetPropertyValue(ManagementBaseObject source, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            try
            {
                var property = source.Properties[propertyName];
                if (property?.Value is not null)
                {
                    return property.Value;
                }
            }
            catch
            {
                // Ignore missing/invalid property access for mixed event payloads.
            }
        }

        return null;
    }

    private static int ParseProcessId(object? value)
    {
        if (value is null)
        {
            return 0;
        }

        try
        {
            return Convert.ToInt32(value);
        }
        catch
        {
            return 0;
        }
    }

    private void DisposeWatchersCore()
    {
        DisposeWatcher(_startWatcher);
        _startWatcher = null;
        DisposeWatcher(_stopWatcher);
        _stopWatcher = null;
        _scope = null;

        _running = false;
        SetCurrentMode(ProcessEventStreamMode.Unavailable);
    }

    private void SetCurrentMode(ProcessEventStreamMode mode)
    {
        if (_currentMode == mode)
        {
            return;
        }

        var previous = _currentMode;
        _currentMode = mode;
        AppLogger.Info($"Event stream mode changed: {previous} -> {mode}");
        StreamModeChanged?.Invoke(this, EventArgs.Empty);
    }

    private static void DisposeWatcher(ManagementEventWatcher? watcher)
    {
        if (watcher is null)
        {
            return;
        }

        try
        {
            watcher.Stop();
        }
        catch
        {
            // Ignore watcher stop exceptions.
        }
        finally
        {
            watcher.Dispose();
        }
    }

    private T InvokeOnMta<T>(Func<T> operation)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(ProcessEventMonitor));
        }

        if (Environment.CurrentManagedThreadId == _mtaThreadId)
        {
            return operation();
        }

        T? result = default;
        Exception? failure = null;
        using var completed = new ManualResetEventSlim(false);

        try
        {
            _mtaWorkQueue.Add(() =>
            {
                try
                {
                    result = operation();
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
                finally
                {
                    completed.Set();
                }
            });
        }
        catch (InvalidOperationException ex)
        {
            throw new ObjectDisposedException(nameof(ProcessEventMonitor), ex);
        }

        completed.Wait();
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }

        return result!;
    }

    private void InvokeOnMta(Action operation)
    {
        InvokeOnMta(() =>
        {
            operation();
            return true;
        });
    }

    private static string DescribeException(Exception ex)
    {
        return $"{ex.Message} (HRESULT=0x{(ex.HResult & 0xFFFFFFFF):X8})";
    }

    private static bool IsAccessDenied(Exception ex)
    {
        Exception? current = ex;
        while (current is not null)
        {
            if (current is ManagementException managementException &&
                managementException.ErrorCode == ManagementStatus.AccessDenied)
            {
                return true;
            }

            if (current is COMException comException &&
                (uint)comException.HResult == 0x80041003)
            {
                return true;
            }

            current = current.InnerException;
        }

        return false;
    }
}
