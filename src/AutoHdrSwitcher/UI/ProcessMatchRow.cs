namespace AutoHdrSwitcher.UI;

public sealed class ProcessMatchRow
{
    public int RuleIndex { get; set; } = -1;

    public string ProcessTargetKey { get; set; } = string.Empty;

    public bool HasProcessTargetOverride { get; set; }

    public int ProcessId { get; set; }

    public string ProcessName { get; set; } = string.Empty;

    public string RulePattern { get; set; } = string.Empty;

    public string Mode { get; set; } = string.Empty;

    public string MatchInput { get; set; } = string.Empty;

    public string Display { get; set; } = string.Empty;

    public bool FullscreenLike { get; set; }

    private string _targetDisplay = ProcessWatchRuleRow.DefaultTargetDisplayValue;

    public string TargetDisplay
    {
        get => _targetDisplay;
        set => _targetDisplay = ProcessWatchRuleRow.NormalizeTargetDisplayValue(value);
    }
}
