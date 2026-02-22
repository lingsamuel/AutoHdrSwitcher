namespace AutoHdrSwitcher.Monitoring;

public sealed class ProcessEventNotification
{
    public required string EventType { get; init; }

    public required int ProcessId { get; init; }

    public required string ProcessName { get; init; }

    public long SequenceId { get; init; }

    public ProcessEventStreamMode StreamMode { get; init; } = ProcessEventStreamMode.Unavailable;

    public DateTimeOffset ReceivedAtUtc { get; init; }

    public DateTimeOffset? EventCreatedAtUtc { get; init; }

    public double? DeliveryLatencyMs { get; init; }

    public string EventClassName { get; init; } = string.Empty;
}
