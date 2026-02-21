namespace AutoHdrSwitcher.UI;

public sealed class FullscreenProcessRow
{
    public int ProcessId { get; init; }

    public string ProcessName { get; init; } = string.Empty;

    public string Display { get; init; } = string.Empty;

    public bool MatchedByRule { get; init; }
}
