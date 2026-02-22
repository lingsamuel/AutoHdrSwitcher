namespace AutoHdrSwitcher.UI;

public sealed class DisplayHdrRow
{
    public string Display { get; set; } = string.Empty;

    public string FriendlyName { get; set; } = string.Empty;

    public bool Supported { get; set; }

    public bool AutoMode { get; set; } = true;

    public bool HdrEnabled { get; set; }

    public bool DesiredHdr { get; set; }

    public string Action { get; set; } = string.Empty;
}
