using AutoHdrSwitcher.Matching;

namespace AutoHdrSwitcher.UI;

public sealed class ProcessWatchRuleRow
{
    public const string DefaultTargetDisplayValue = "__DEFAULT__";
    public const string SwitchAllDisplaysTargetValue = ProcessWatchRule.SwitchAllDisplaysTargetValue;

    public string Pattern { get; set; } = string.Empty;

    public bool ExactMatch { get; set; }

    public bool CaseSensitive { get; set; }

    public bool RegexMode { get; set; }

    public bool Enabled { get; set; } = true;

    // UI token: "__DEFAULT__" means follow runtime default resolution.
    private string _targetDisplay = DefaultTargetDisplayValue;

    public string TargetDisplay
    {
        get => _targetDisplay;
        set => _targetDisplay = NormalizeTargetDisplayValue(value);
    }

    public static bool IsDefaultTargetDisplayValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ||
            string.Equals(value, DefaultTargetDisplayValue, StringComparison.OrdinalIgnoreCase);
    }

    public static string NormalizeTargetDisplayValue(string? value)
    {
        if (IsDefaultTargetDisplayValue(value))
        {
            return DefaultTargetDisplayValue;
        }

        var normalized = value!.Trim();
        if (string.Equals(
                normalized,
                SwitchAllDisplaysTargetValue,
                StringComparison.OrdinalIgnoreCase))
        {
            return SwitchAllDisplaysTargetValue;
        }

        return normalized;
    }

    public ProcessWatchRule ToRule()
    {
        return new ProcessWatchRule
        {
            Pattern = Pattern ?? string.Empty,
            ExactMatch = ExactMatch,
            CaseSensitive = CaseSensitive,
            RegexMode = RegexMode,
            Enabled = Enabled,
            TargetDisplay = IsDefaultTargetDisplayValue(TargetDisplay)
                ? null
                : TargetDisplay.Trim()
        };
    }

    public static ProcessWatchRuleRow FromRule(ProcessWatchRule rule)
    {
        return new ProcessWatchRuleRow
        {
            Pattern = rule.Pattern,
            ExactMatch = rule.ExactMatch,
            CaseSensitive = rule.CaseSensitive,
            RegexMode = rule.RegexMode,
            Enabled = rule.Enabled,
            TargetDisplay = IsDefaultTargetDisplayValue(rule.TargetDisplay)
                ? DefaultTargetDisplayValue
                : rule.TargetDisplay!.Trim()
        };
    }
}
