using System.Management;

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

    private ManagementEventWatcher? _startWatcher;
    private ManagementEventWatcher? _stopWatcher;
    private bool _running;
    private ProcessEventStreamMode _currentMode = ProcessEventStreamMode.Unavailable;

    public event EventHandler<ProcessEventNotification>? ProcessEventReceived;
    public event EventHandler? StreamModeChanged;

    public ProcessEventStreamMode CurrentMode => _currentMode;

    public bool Start(out string error)
    {
        if (_running)
        {
            error = string.Empty;
            return true;
        }

        if (TryCreateAndStartWatchers(StartTraceQuery, StopTraceQuery, out var traceStartWatcher, out var traceStopWatcher, out var traceError))
        {
            _startWatcher = traceStartWatcher;
            _stopWatcher = traceStopWatcher;
            _running = true;
            SetCurrentMode(ProcessEventStreamMode.Trace);
            error = string.Empty;
            return true;
        }

        if (TryCreateAndStartWatchers(StartInstanceQuery, StopInstanceQuery, out var instanceStartWatcher, out var instanceStopWatcher, out var instanceError))
        {
            _startWatcher = instanceStartWatcher;
            _stopWatcher = instanceStopWatcher;
            _running = true;
            SetCurrentMode(ProcessEventStreamMode.Instance);
            error = string.Empty;
            return true;
        }

        error = $"trace query failed: {traceError}; instance query failed: {instanceError}";
        SetCurrentMode(ProcessEventStreamMode.Unavailable);
        return false;
    }

    public bool TrySwitchToTrace(out string error)
    {
        if (!_running)
        {
            error = "event stream is not running";
            return false;
        }

        if (_currentMode == ProcessEventStreamMode.Trace)
        {
            error = string.Empty;
            return true;
        }

        if (!TryCreateAndStartWatchers(
                StartTraceQuery,
                StopTraceQuery,
                out var traceStartWatcher,
                out var traceStopWatcher,
                out error))
        {
            return false;
        }

        var oldStartWatcher = _startWatcher;
        var oldStopWatcher = _stopWatcher;
        _startWatcher = traceStartWatcher;
        _stopWatcher = traceStopWatcher;
        SetCurrentMode(ProcessEventStreamMode.Trace);
        DisposeWatcher(oldStartWatcher);
        DisposeWatcher(oldStopWatcher);
        error = string.Empty;
        return true;
    }

    public void Stop()
    {
        DisposeWatchers();
    }

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }

    private bool TryCreateAndStartWatchers(
        string startQuery,
        string stopQuery,
        out ManagementEventWatcher? startWatcher,
        out ManagementEventWatcher? stopWatcher,
        out string error)
    {
        startWatcher = null;
        stopWatcher = null;
        ManagementEventWatcher? localStartWatcher = null;
        ManagementEventWatcher? localStopWatcher = null;
        try
        {
            localStartWatcher = new ManagementEventWatcher(ScopePath, startQuery);
            localStopWatcher = new ManagementEventWatcher(ScopePath, stopQuery);

            localStartWatcher.EventArrived += (_, args) => HandleEvent("start", args);
            localStopWatcher.EventArrived += (_, args) => HandleEvent("stop", args);

            localStartWatcher.Start();
            localStopWatcher.Start();

            startWatcher = localStartWatcher;
            stopWatcher = localStopWatcher;
            error = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            DisposeWatcher(localStartWatcher);
            DisposeWatcher(localStopWatcher);
            error = ex.Message;
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

    private void DisposeWatchers()
    {
        DisposeWatcher(_startWatcher);
        _startWatcher = null;
        DisposeWatcher(_stopWatcher);
        _stopWatcher = null;

        _running = false;
        SetCurrentMode(ProcessEventStreamMode.Unavailable);
    }

    private void SetCurrentMode(ProcessEventStreamMode mode)
    {
        if (_currentMode == mode)
        {
            return;
        }

        _currentMode = mode;
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
}
