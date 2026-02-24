using System.Configuration;

namespace AutoHdrSwitcher.UI;

internal sealed class WindowPlacementSettings : ApplicationSettingsBase
{
    private static readonly WindowPlacementSettings DefaultInstance =
        (WindowPlacementSettings)Synchronized(new WindowPlacementSettings());
    private static readonly object UpgradeLock = new();
    private static bool _upgradeChecked;

    public static WindowPlacementSettings Default => DefaultInstance;

    public void EnsureUpgraded()
    {
        if (_upgradeChecked)
        {
            return;
        }

        lock (UpgradeLock)
        {
            if (_upgradeChecked)
            {
                return;
            }

            if (!SettingsUpgraded)
            {
                try
                {
                    Upgrade();
                }
                catch (ConfigurationErrorsException)
                {
                    // Keep startup resilient even when prior user settings are unreadable.
                }
                SettingsUpgraded = true;
                try
                {
                    Save();
                }
                catch (ConfigurationErrorsException)
                {
                    // Ignore write failures and continue with in-memory settings for this run.
                }
            }

            _upgradeChecked = true;
        }
    }

    [UserScopedSetting]
    [DefaultSettingValue("False")]
    public bool SettingsUpgraded
    {
        get => (bool)this[nameof(SettingsUpgraded)];
        set => this[nameof(SettingsUpgraded)] = value;
    }

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
