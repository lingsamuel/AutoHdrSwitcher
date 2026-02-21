namespace AutoHdrSwitcher.UI;

public sealed class ProcessMatchRow
{
    public int ProcessId { get; init; }

    public string ProcessName { get; init; } = string.Empty;

    public string RulePattern { get; init; } = string.Empty;

    public string Mode { get; init; } = string.Empty;

    public string MatchInput { get; init; } = string.Empty;

    public string Display { get; init; } = string.Empty;

    public bool FullscreenLike { get; init; }
}
