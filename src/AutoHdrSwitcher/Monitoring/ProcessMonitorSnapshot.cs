namespace AutoHdrSwitcher.Monitoring;

public sealed class ProcessMonitorSnapshot
{
    public required DateTimeOffset CollectedAt { get; init; }

    public required int ProcessCount { get; init; }

    public required IReadOnlyList<ProcessMatchResult> Matches { get; init; }

    public required IReadOnlyList<FullscreenProcessInfo> FullscreenProcesses { get; init; }

    public required IReadOnlyList<HdrDisplayStatus> Displays { get; init; }
}
