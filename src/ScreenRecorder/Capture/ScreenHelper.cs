using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using static ScreenRecorder.Capture.NativeDisplay;

namespace ScreenRecorder.Capture;

public sealed class MonitorInfo
{
    public int Index { get; init; }
    public IntPtr Handle { get; init; }
    public RECT Bounds { get; init; }
    public RECT WorkArea { get; init; }
    public string DeviceName { get; init; } = "";
    public bool IsPrimary { get; init; }
    public uint DpiX { get; init; } = 96;
    public uint DpiY { get; init; } = 96;

    public int Width => Bounds.Width;
    public int Height => Bounds.Height;

    public string Display => IsPrimary
        ? $"{DeviceName}（主屏 {Width}×{Height}）"
        : $"{DeviceName}（{Width}×{Height}）";

    public override string ToString() => Display;
}

[SupportedOSPlatform("windows")]
public static class ScreenHelper
{
    public static List<MonitorInfo> EnumerateMonitors()
    {
        var list = new List<MonitorInfo>();
        int index = 0;
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMon, IntPtr hdc, ref RECT rect, IntPtr data) =>
        {
            var mi = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
            bool ok = GetMonitorInfo(hMon, ref mi);
            uint dpiX = 96, dpiY = 96;
            try { GetDpiForMonitor(hMon, 0 /*MDT_EFFECTIVE_DPI*/, out dpiX, out dpiY); } catch { }
            list.Add(new MonitorInfo
            {
                Index = index++,
                Handle = hMon,
                Bounds = ok ? mi.rcMonitor : rect,
                WorkArea = ok ? mi.rcWork : rect,
                DeviceName = ok ? (mi.szDevice ?? "").TrimEnd('\0') : "",
                IsPrimary = ok && (mi.dwFlags & 1) != 0,
                DpiX = dpiX == 0 ? 96 : dpiX,
                DpiY = dpiY == 0 ? 96 : dpiY,
            });
            return true;
        }, IntPtr.Zero);

        if (list.Count == 0)
        {
            list.Add(new MonitorInfo
            {
                Index = 0,
                Handle = IntPtr.Zero,
                Bounds = new RECT { Left = 0, Top = 0, Right = 1920, Bottom = 1080 },
                WorkArea = new RECT { Left = 0, Top = 0, Right = 1920, Bottom = 1040 },
                DeviceName = "PRIMARY",
                IsPrimary = true,
            });
        }
        return list;
    }

    public static RECT VirtualDesktopBounds(List<MonitorInfo>? monitors = null)
    {
        monitors ??= EnumerateMonitors();
        int left = int.MaxValue, top = int.MaxValue, right = int.MinValue, bottom = int.MinValue;
        foreach (var m in monitors)
        {
            left = Math.Min(left, m.Bounds.Left);
            top = Math.Min(top, m.Bounds.Top);
            right = Math.Max(right, m.Bounds.Right);
            bottom = Math.Max(bottom, m.Bounds.Bottom);
        }
        return new RECT { Left = left, Top = top, Right = right, Bottom = bottom };
    }

    public static MonitorInfo Primary(List<MonitorInfo> monitors) =>
        monitors.Find(m => m.IsPrimary) ?? monitors[0];
}
