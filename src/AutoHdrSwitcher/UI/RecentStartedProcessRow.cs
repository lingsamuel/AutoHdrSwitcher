namespace AutoHdrSwitcher.UI;

public sealed class RecentStartedProcessRow
{
    public long SequenceId { get; init; }

    public int ProcessId { get; init; }

    public string ProcessName { get; init; } = string.Empty;

    public string StartedAt { get; init; } = string.Empty;

    public string RulePattern { get; init; } = string.Empty;

    public bool Matched { get; set; }
}
