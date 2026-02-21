using System.Configuration;

namespace AutoHdrSwitcher.UI;

internal sealed class WindowPlacementSettings : ApplicationSettingsBase
{
    private static readonly WindowPlacementSettings DefaultInstance =
        (WindowPlacementSettings)Synchronized(new WindowPlacementSettings());

    public static WindowPlacementSettings Default => DefaultInstance;

    [UserScopedSetting]
    [DefaultSettingValue("False")]
    public bool HasBounds
    {
        get => (bool)this[nameof(HasBounds)];
        set => this[nameof(HasBounds)] = value;
    }

    [UserScopedSetting]
    [DefaultSettingValue("0")]
    public int X
    {
        get => (int)this[nameof(X)];
        set => this[nameof(X)] = value;
    }

    [UserScopedSetting]
    [DefaultSettingValue("0")]
    public int Y
    {
        get => (int)this[nameof(Y)];
        set => this[nameof(Y)] = value;
    }

    [UserScopedSetting]
    [DefaultSettingValue("0")]
    public int Width
    {
        get => (int)this[nameof(Width)];
        set => this[nameof(Width)] = value;
    }

    [UserScopedSetting]
    [DefaultSettingValue("0")]
    public int Height
    {
        get => (int)this[nameof(Height)];
        set => this[nameof(Height)] = value;
    }

    [UserScopedSetting]
    [DefaultSettingValue("False")]
    public bool Maximized
    {
        get => (bool)this[nameof(Maximized)];
        set => this[nameof(Maximized)] = value;
    }
}
