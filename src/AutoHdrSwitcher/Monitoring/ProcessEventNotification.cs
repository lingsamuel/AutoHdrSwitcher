namespace AutoHdrSwitcher.Monitoring;

public sealed class ProcessEventNotification
{
    public required string EventType { get; init; }

    public required int ProcessId { get; init; }

    public required string ProcessName { get; init; }
}
