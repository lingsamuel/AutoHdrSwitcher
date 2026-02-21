using System.Management;

namespace AutoHdrSwitcher.Monitoring;

public sealed class ProcessEventMonitor : IDisposable
{
    private ManagementEventWatcher? _startWatcher;
    private ManagementEventWatcher? _stopWatcher;
    private bool _running;

    public event EventHandler<ProcessEventNotification>? ProcessEventReceived;

    public bool Start(out string error)
    {
        if (_running)
        {
            error = string.Empty;
            return true;
        }

        try
        {
            _startWatcher = new ManagementEventWatcher(
                new WqlEventQuery("SELECT ProcessID, ProcessName FROM Win32_ProcessStartTrace"));
            _stopWatcher = new ManagementEventWatcher(
                new WqlEventQuery("SELECT ProcessID, ProcessName FROM Win32_ProcessStopTrace"));

            _startWatcher.EventArrived += (_, args) => HandleEvent("start", args);
            _stopWatcher.EventArrived += (_, args) => HandleEvent("stop", args);

            _startWatcher.Start();
            _stopWatcher.Start();
            _running = true;
            error = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            DisposeWatchers();
            error = ex.Message;
            return false;
        }
    }

    public void Stop()
    {
        DisposeWatchers();
        _running = false;
    }

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }

    private void HandleEvent(string eventType, EventArrivedEventArgs args)
    {
        try
        {
            var processId = Convert.ToInt32(args.NewEvent.Properties["ProcessID"].Value ?? 0);
            var processName = Convert.ToString(args.NewEvent.Properties["ProcessName"].Value) ?? string.Empty;
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

    private void DisposeWatchers()
    {
        if (_startWatcher is not null)
        {
            try
            {
                _startWatcher.Stop();
            }
            catch
            {
                // Ignore watcher stop exceptions.
            }
            finally
            {
                _startWatcher.Dispose();
                _startWatcher = null;
            }
        }

        if (_stopWatcher is not null)
        {
            try
            {
                _stopWatcher.Stop();
            }
            catch
            {
                // Ignore watcher stop exceptions.
            }
            finally
            {
                _stopWatcher.Dispose();
                _stopWatcher = null;
            }
        }
    }
}
