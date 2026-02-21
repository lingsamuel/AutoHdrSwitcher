namespace AutoHdrSwitcher.Monitoring;

public sealed class FullscreenProcessInfo
{
    public required int ProcessId { get; init; }

    public required string ProcessName { get; init; }

    public string ExecutablePath { get; init; } = string.Empty;

    public required string IgnoreKey { get; init; }

    public required bool IsIgnored { get; init; }

    public required bool IsDefaultIgnoreApplied { get; init; }

    public required string Display { get; init; }

    public required bool MatchedByRule { get; init; }
}
