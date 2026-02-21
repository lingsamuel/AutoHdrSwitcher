using System.Runtime.InteropServices;

namespace AutoHdrSwitcher.Display;

internal static class NativeDisplay
{
    internal const int ErrorSuccess = 0;
    internal const uint QdcOnlyActivePaths = 0x00000002;
    internal const uint DisplayConfigPathActive = 0x00000001;

    internal const uint DeviceInfoGetSourceName = 1;
    internal const uint DeviceInfoGetTargetName = 2;
    internal const uint DeviceInfoGetAdvancedColorInfo = 9;
    internal const uint DeviceInfoSetAdvancedColorState = 10;

    internal const int CchDeviceName = 32;
    internal const int DisplayConfigTargetDeviceNameLength = 64;
    internal const int DisplayConfigTargetDeviceNamePath = 128;

    [StructLayout(LayoutKind.Sequential)]
    internal struct Luid
    {
        internal uint LowPart;
        internal int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DisplayConfigRational
    {
        internal uint Numerator;
        internal uint Denominator;
    }

    internal enum DisplayConfigVideoOutputTechnology : uint
    {
        Other = 0xFFFFFFFF
    }

    internal enum DisplayConfigRotation : uint
    {
        Identity = 1
    }

    internal enum DisplayConfigScaling : uint
    {
        Identity = 1
    }

    internal enum DisplayConfigScanLineOrdering : uint
    {
        Unspecified = 0
    }

    internal enum DisplayConfigModeInfoType : uint
    {
        Source = 1,
        Target = 2,
        DesktopImage = 3
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DisplayConfigPathSourceInfo
    {
        internal Luid AdapterId;
        internal uint Id;
        internal uint ModeInfoIdx;
        internal uint StatusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DisplayConfigPathTargetInfo
    {
        internal Luid AdapterId;
        internal uint Id;
        internal uint ModeInfoIdx;
        internal DisplayConfigVideoOutputTechnology OutputTechnology;
        internal DisplayConfigRotation Rotation;
        internal DisplayConfigScaling Scaling;
        internal DisplayConfigRational RefreshRate;
        internal DisplayConfigScanLineOrdering ScanLineOrdering;
        [MarshalAs(UnmanagedType.Bool)]
        internal bool TargetAvailable;
        internal uint StatusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DisplayConfigPathInfo
    {
        internal DisplayConfigPathSourceInfo SourceInfo;
        internal DisplayConfigPathTargetInfo TargetInfo;
        internal uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DisplayConfig2DRegion
    {
        internal uint Cx;
        internal uint Cy;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DisplayConfigVideoSignalInfo
    {
        internal ulong PixelRate;
        internal DisplayConfigRational HSyncFreq;
        internal DisplayConfigRational VSyncFreq;
        internal DisplayConfig2DRegion ActiveSize;
        internal DisplayConfig2DRegion TotalSize;
        internal uint VideoStandard;
        internal DisplayConfigScanLineOrdering ScanLineOrdering;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DisplayConfigTargetMode
    {
        internal DisplayConfigVideoSignalInfo TargetVideoSignalInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DisplayConfigSourceMode
    {
        internal uint Width;
        internal uint Height;
        internal uint PixelFormat;
        internal PointL Position;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PointL
    {
        internal int X;
        internal int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DisplayConfigDesktopImageInfo
    {
        internal PointL PathSourceSize;
        internal RectL DesktopImageRegion;
        internal RectL DesktopImageClip;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RectL
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;
    }

    [StructLayout(LayoutKind.Explicit)]
    internal struct DisplayConfigModeInfoUnion
    {
        [FieldOffset(0)]
        internal DisplayConfigTargetMode TargetMode;

        [FieldOffset(0)]
        internal DisplayConfigSourceMode SourceMode;

        [FieldOffset(0)]
        internal DisplayConfigDesktopImageInfo DesktopImageInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DisplayConfigModeInfo
    {
        internal DisplayConfigModeInfoType InfoType;
        internal uint Id;
        internal Luid AdapterId;
        internal DisplayConfigModeInfoUnion ModeInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DisplayConfigDeviceInfoHeader
    {
        internal uint Type;
        internal uint Size;
        internal Luid AdapterId;
        internal uint Id;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct DisplayConfigSourceDeviceName
    {
        internal DisplayConfigDeviceInfoHeader Header;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CchDeviceName)]
        internal string ViewGdiDeviceName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct DisplayConfigTargetDeviceName
    {
        internal DisplayConfigDeviceInfoHeader Header;
        internal uint Flags;
        internal DisplayConfigVideoOutputTechnology OutputTechnology;
        internal ushort EdidManufactureId;
        internal ushort EdidProductCodeId;
        internal uint ConnectorInstance;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = DisplayConfigTargetDeviceNameLength)]
        internal string MonitorFriendlyDeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = DisplayConfigTargetDeviceNamePath)]
        internal string MonitorDevicePath;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DisplayConfigGetAdvancedColorInfo
    {
        internal DisplayConfigDeviceInfoHeader Header;
        internal uint Value;
        internal uint ColorEncoding;
        internal uint BitsPerColorChannel;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DisplayConfigSetAdvancedColorState
    {
        internal DisplayConfigDeviceInfoHeader Header;
        internal uint Value;
    }

    [DllImport("user32.dll")]
    internal static extern int GetDisplayConfigBufferSizes(
        uint flags,
        out uint numPathArrayElements,
        out uint numModeInfoArrayElements);

    [DllImport("user32.dll")]
    internal static extern int QueryDisplayConfig(
        uint flags,
        ref uint numPathArrayElements,
        [Out] DisplayConfigPathInfo[] pathInfoArray,
        ref uint numModeInfoArrayElements,
        [Out] DisplayConfigModeInfo[] modeInfoArray,
        IntPtr currentTopologyId);

    [DllImport("user32.dll")]
    internal static extern int DisplayConfigGetDeviceInfo(ref DisplayConfigSourceDeviceName requestPacket);

    [DllImport("user32.dll")]
    internal static extern int DisplayConfigGetDeviceInfo(ref DisplayConfigTargetDeviceName requestPacket);

    [DllImport("user32.dll")]
    internal static extern int DisplayConfigGetDeviceInfo(ref DisplayConfigGetAdvancedColorInfo requestPacket);

    [DllImport("user32.dll")]
    internal static extern int DisplayConfigSetDeviceInfo(ref DisplayConfigSetAdvancedColorState setPacket);
}
