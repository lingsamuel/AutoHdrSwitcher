namespace AutoHdrSwitcher.Display;

public sealed class ProcessDisplayResolver
{
    public bool TryGetPrimaryDisplay(out string gdiDeviceName)
    {
        var monitor = NativeWindowing.MonitorFromPoint(
            new NativeWindowing.Point { X = 0, Y = 0 },
            NativeWindowing.MonitorDefaultToPrimary);
        if (monitor == IntPtr.Zero)
        {
            gdiDeviceName = string.Empty;
            return false;
        }

        var info = new NativeWindowing.MonitorInfoEx
        {
            CbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeWindowing.MonitorInfoEx>(),
            SzDevice = string.Empty
        };
        if (!NativeWindowing.GetMonitorInfo(monitor, ref info) || string.IsNullOrWhiteSpace(info.SzDevice))
        {
            gdiDeviceName = string.Empty;
            return false;
        }

        // Use the monitor reported as primary to avoid relying on task focus.
        if ((info.DwFlags & NativeWindowing.MonitorInfoPrimary) == 0)
        {
            gdiDeviceName = string.Empty;
            return false;
        }

        gdiDeviceName = info.SzDevice;
        return true;
    }

    public bool TryGetDisplayForProcess(int processId, out string gdiDeviceName, out bool isFullscreenLike)
    {
        var windows = CaptureProcessWindows();
        if (!windows.TryGetValue(processId, out var resolved))
        {
            gdiDeviceName = string.Empty;
            isFullscreenLike = false;
            return false;
        }

        gdiDeviceName = resolved.GdiDeviceName;
        isFullscreenLike = resolved.IsFullscreenLike;
        return true;
    }

    public IReadOnlyDictionary<int, ResolvedProcessWindow> CaptureProcessWindows()
    {
        var bestByPid = new Dictionary<int, ResolvedProcessWindow>();
        var foreground = NativeWindowing.GetForegroundWindow();

        _ = NativeWindowing.EnumWindows((hWnd, __) =>
        {
            if (!NativeWindowing.IsWindowVisible(hWnd) || NativeWindowing.IsIconic(hWnd))
            {
                return true;
            }

            NativeWindowing.GetWindowThreadProcessId(hWnd, out var ownerPid);
            if (ownerPid == 0)
            {
                return true;
            }

            if (!TryGetWindowDisplayInfo(hWnd, out var displayInfo))
            {
                return true;
            }

            var pid = unchecked((int)ownerPid);
            var candidate = new ResolvedProcessWindow
            {
                ProcessId = pid,
                GdiDeviceName = displayInfo.GdiDeviceName,
                IsFullscreenLike = displayInfo.IsFullscreenLike,
                Area = displayInfo.Area,
                IsForeground = hWnd == foreground
            };

            if (bestByPid.TryGetValue(pid, out var existing))
            {
                if (ShouldReplace(existing, candidate))
                {
                    bestByPid[pid] = candidate;
                }
            }
            else
            {
                bestByPid[pid] = candidate;
            }

            return true;
        }, IntPtr.Zero);

        return bestByPid;
    }

    private static bool TryGetWindowDisplayInfo(IntPtr hWnd, out WindowDisplayInfo displayInfo)
    {
        var monitor = NativeWindowing.MonitorFromWindow(hWnd, NativeWindowing.MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero)
        {
            displayInfo = new WindowDisplayInfo();
            return false;
        }

        if (!NativeWindowing.GetWindowRect(hWnd, out var windowRect))
        {
            displayInfo = new WindowDisplayInfo();
            return false;
        }

        var info = new NativeWindowing.MonitorInfoEx
        {
            CbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeWindowing.MonitorInfoEx>(),
            SzDevice = string.Empty
        };
        if (!NativeWindowing.GetMonitorInfo(monitor, ref info) || string.IsNullOrWhiteSpace(info.SzDevice))
        {
            displayInfo = new WindowDisplayInfo();
            return false;
        }

        var style = NativeWindowing.GetWindowLongPtr(hWnd, NativeWindowing.GwlStyle).ToInt64();
        var isBorderless = (style & (NativeWindowing.WsCaption | NativeWindowing.WsThickFrame)) == 0;
        var fillsMonitor =
            Math.Abs(windowRect.Left - info.RcMonitor.Left) <= 1 &&
            Math.Abs(windowRect.Top - info.RcMonitor.Top) <= 1 &&
            Math.Abs(windowRect.Right - info.RcMonitor.Right) <= 1 &&
            Math.Abs(windowRect.Bottom - info.RcMonitor.Bottom) <= 1;
        var area = Math.Max(0, windowRect.Right - windowRect.Left) * Math.Max(0, windowRect.Bottom - windowRect.Top);
        if (area <= 0)
        {
            displayInfo = new WindowDisplayInfo();
            return false;
        }

        displayInfo = new WindowDisplayInfo
        {
            GdiDeviceName = info.SzDevice,
            IsFullscreenLike = fillsMonitor && isBorderless,
            Area = area
        };
        return true;
    }

    private static bool ShouldReplace(ResolvedProcessWindow existing, ResolvedProcessWindow candidate)
    {
        if (candidate.IsForeground && !existing.IsForeground)
        {
            return true;
        }

        if (candidate.IsFullscreenLike && !existing.IsFullscreenLike)
        {
            return true;
        }

        return candidate.Area > existing.Area;
    }

    private sealed class WindowDisplayInfo
    {
        public string GdiDeviceName { get; init; } = string.Empty;

        public bool IsFullscreenLike { get; init; }

        public int Area { get; init; }
    }
}
