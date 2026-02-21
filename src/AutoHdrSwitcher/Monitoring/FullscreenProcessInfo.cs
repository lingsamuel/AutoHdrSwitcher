namespace AutoHdrSwitcher.Monitoring;

public sealed class FullscreenProcessInfo
{
    public required int ProcessId { get; init; }

    public required string ProcessName { get; init; }

    public required string Display { get; init; }

    public required bool MatchedByRule { get; init; }
}
