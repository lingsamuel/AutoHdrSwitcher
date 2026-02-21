namespace AutoHdrSwitcher.UI;

public sealed class DisplayHdrRow
{
    public string Display { get; init; } = string.Empty;

    public string FriendlyName { get; init; } = string.Empty;

    public bool Supported { get; init; }

    public bool HdrEnabled { get; init; }

    public bool DesiredHdr { get; init; }

    public string Action { get; init; } = string.Empty;
}
