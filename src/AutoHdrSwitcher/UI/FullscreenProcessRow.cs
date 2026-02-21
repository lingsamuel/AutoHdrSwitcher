namespace AutoHdrSwitcher.UI;

public sealed class FullscreenProcessRow
{
    public int ProcessId { get; init; }

    public string ProcessName { get; init; } = string.Empty;

    public string ExecutablePath { get; init; } = string.Empty;

    public string Display { get; init; } = string.Empty;

    public bool MatchedByRule { get; init; }

    public bool Ignore { get; set; }

    public string IgnoreKey { get; init; } = string.Empty;

    public bool IsDefaultIgnoreApplied { get; init; }
}
