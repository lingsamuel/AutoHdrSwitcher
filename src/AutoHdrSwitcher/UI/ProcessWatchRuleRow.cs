using AutoHdrSwitcher.Matching;

namespace AutoHdrSwitcher.UI;

public sealed class ProcessWatchRuleRow
{
    public string Pattern { get; set; } = string.Empty;

    public bool ExactMatch { get; set; }

    public bool CaseSensitive { get; set; }

    public bool RegexMode { get; set; }

    public bool Enabled { get; set; } = true;

    public ProcessWatchRule ToRule()
    {
        return new ProcessWatchRule
        {
            Pattern = Pattern ?? string.Empty,
            ExactMatch = ExactMatch,
            CaseSensitive = CaseSensitive,
            RegexMode = RegexMode,
            Enabled = Enabled
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
            Enabled = rule.Enabled
        };
    }
}
