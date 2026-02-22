using System.Text.Json.Serialization;

namespace AutoHdrSwitcher.Matching;

public sealed class ProcessWatchRule
{
    public const string SwitchAllDisplaysTargetValue = "__ALL_DISPLAYS__";

    // Process name/path pattern. Example: "game", "*.exe", "^eldenring.*$"
    public string Pattern { get; init; } = string.Empty;

    // Off by default: if enabled and RegexMode == false, require whole-string match.
    public bool ExactMatch { get; init; }

    // Off by default: if enabled and RegexMode == false, matching is case-sensitive.
    public bool CaseSensitive { get; init; }

    // If enabled, Pattern is treated as regular expression and other 2 flags are ignored.
    public bool RegexMode { get; init; }

    // Optional on/off switch per row.
    public bool Enabled { get; init; } = true;

    // Optional preferred target display. Null/empty means "Default".
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TargetDisplay { get; init; }
}
