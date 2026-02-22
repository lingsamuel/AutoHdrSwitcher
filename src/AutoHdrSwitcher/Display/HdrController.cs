using AutoHdrSwitcher.Logging;

namespace AutoHdrSwitcher.Display;

internal sealed class HdrController
{
    public IReadOnlyList<HdrDisplayInfo> GetDisplays()
    {
        var result = new List<HdrDisplayInfo>();
        var getBufferCode = NativeDisplay.GetDisplayConfigBufferSizes(
            NativeDisplay.QdcOnlyActivePaths,
            out var pathCount,
            out var modeCount);
        if (getBufferCode != NativeDisplay.ErrorSuccess || pathCount == 0)
        {
            return result;
        }

        var paths = new NativeDisplay.DisplayConfigPathInfo[pathCount];
        var modes = new NativeDisplay.DisplayConfigModeInfo[modeCount];
        var queryCode = NativeDisplay.QueryDisplayConfig(
            NativeDisplay.QdcOnlyActivePaths,
            ref pathCount,
            paths,
            ref modeCount,
            modes,
            IntPtr.Zero);
        if (queryCode != NativeDisplay.ErrorSuccess)
        {
            return result;
        }

        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < pathCount; i++)
        {
            var path = paths[i];
            if ((path.Flags & NativeDisplay.DisplayConfigPathActive) == 0)
            {
                continue;
            }

            if (!TryGetSourceName(path.SourceInfo.AdapterId, path.SourceInfo.Id, out var sourceName))
            {
                continue;
            }

            var key = BuildDisplayKey(path.TargetInfo.AdapterId, path.TargetInfo.Id);
            if (!seenKeys.Add(key))
            {
                continue;
            }

            _ = TryGetTargetName(path.TargetInfo.AdapterId, path.TargetInfo.Id, out var targetName, out _);
            _ = TryGetAdvancedColorInfo(path.TargetInfo.AdapterId, path.TargetInfo.Id, out var supported, out var enabled);

            result.Add(new HdrDisplayInfo
            {
                DisplayKey = key,
                GdiDeviceName = sourceName,
                FriendlyName = string.IsNullOrWhiteSpace(targetName) ? sourceName : targetName,
                IsHdrSupported = supported,
                IsHdrEnabled = enabled,
                AdapterId = path.TargetInfo.AdapterId,
                TargetId = path.TargetInfo.Id
            });
        }

        result.Sort(static (a, b) => string.Compare(a.GdiDeviceName, b.GdiDeviceName, StringComparison.OrdinalIgnoreCase));
        return result;
    }

    public bool TrySetHdr(HdrDisplayInfo display, bool enable, out string message)
    {
        AppLogger.Info(
            $"DisplayConfigSetAdvancedColorState request. display={display.GdiDeviceName}; target={display.AdapterId.HighPart}:{display.AdapterId.LowPart}:{display.TargetId}; desired={enable}");
        var packet = new NativeDisplay.DisplayConfigSetAdvancedColorState
        {
            Header = new NativeDisplay.DisplayConfigDeviceInfoHeader
            {
                Type = NativeDisplay.DeviceInfoSetAdvancedColorState,
                Size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeDisplay.DisplayConfigSetAdvancedColorState>(),
                AdapterId = display.AdapterId,
                Id = display.TargetId
            },
            Value = enable ? 1u : 0u
        };

        var setCode = NativeDisplay.DisplayConfigSetDeviceInfo(ref packet);
        if (setCode != NativeDisplay.ErrorSuccess)
        {
            message = $"Set HDR failed (code {setCode})";
            AppLogger.Warn(
                $"DisplayConfigSetAdvancedColorState failed. display={display.GdiDeviceName}; desired={enable}; code={setCode}");
            return false;
        }

        message = enable ? "HDR enabled" : "HDR disabled";
        AppLogger.Info(
            $"DisplayConfigSetAdvancedColorState succeeded. display={display.GdiDeviceName}; desired={enable}; result={message}");
        return true;
    }

    private static string BuildDisplayKey(NativeDisplay.Luid adapterId, uint targetId)
    {
        return $"{adapterId.HighPart}:{adapterId.LowPart}:{targetId}";
    }

    private static bool TryGetSourceName(NativeDisplay.Luid adapterId, uint sourceId, out string sourceName)
    {
        var packet = new NativeDisplay.DisplayConfigSourceDeviceName
        {
            Header = new NativeDisplay.DisplayConfigDeviceInfoHeader
            {
                Type = NativeDisplay.DeviceInfoGetSourceName,
                Size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeDisplay.DisplayConfigSourceDeviceName>(),
                AdapterId = adapterId,
                Id = sourceId
            },
            ViewGdiDeviceName = string.Empty
        };
        var code = NativeDisplay.DisplayConfigGetDeviceInfo(ref packet);
        sourceName = packet.ViewGdiDeviceName;
        return code == NativeDisplay.ErrorSuccess && !string.IsNullOrWhiteSpace(sourceName);
    }

    private static bool TryGetTargetName(
        NativeDisplay.Luid adapterId,
        uint targetId,
        out string friendlyName,
        out string monitorDevicePath)
    {
        var packet = new NativeDisplay.DisplayConfigTargetDeviceName
        {
            Header = new NativeDisplay.DisplayConfigDeviceInfoHeader
            {
                Type = NativeDisplay.DeviceInfoGetTargetName,
                Size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeDisplay.DisplayConfigTargetDeviceName>(),
                AdapterId = adapterId,
                Id = targetId
            },
            MonitorFriendlyDeviceName = string.Empty,
            MonitorDevicePath = string.Empty
        };
        var code = NativeDisplay.DisplayConfigGetDeviceInfo(ref packet);
        friendlyName = packet.MonitorFriendlyDeviceName;
        monitorDevicePath = packet.MonitorDevicePath;
        return code == NativeDisplay.ErrorSuccess;
    }

    private static bool TryGetAdvancedColorInfo(
        NativeDisplay.Luid adapterId,
        uint targetId,
        out bool isSupported,
        out bool isEnabled)
    {
        var packet = new NativeDisplay.DisplayConfigGetAdvancedColorInfo
        {
            Header = new NativeDisplay.DisplayConfigDeviceInfoHeader
            {
                Type = NativeDisplay.DeviceInfoGetAdvancedColorInfo,
                Size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeDisplay.DisplayConfigGetAdvancedColorInfo>(),
                AdapterId = adapterId,
                Id = targetId
            }
        };
        var code = NativeDisplay.DisplayConfigGetDeviceInfo(ref packet);
        if (code != NativeDisplay.ErrorSuccess)
        {
            isSupported = false;
            isEnabled = false;
            return false;
        }

        isSupported = (packet.Value & 0x1u) != 0;
        isEnabled = (packet.Value & 0x2u) != 0;
        return true;
    }
}
