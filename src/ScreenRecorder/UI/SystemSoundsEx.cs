using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace ScreenRecorder.UI;

/// <summary>开始/停止录制时的提示音（系统别名音，无需附带音频文件）。</summary>
[SupportedOSPlatform("windows")]
internal static class SystemSoundsEx
{
    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    private static extern bool PlaySound(string? sound, IntPtr module, uint flags);

    private const uint SND_ALIAS = 0x00010000;
    private const uint SND_ASYNC = 0x0001;
    private const uint SND_NODEFAULT = 0x0002;

    public static void PlayStart()
    {
        try { PlaySound("SystemAsterisk", IntPtr.Zero, SND_ALIAS | SND_ASYNC | SND_NODEFAULT); } catch { }
    }

    public static void PlayStop()
    {
        try { PlaySound("SystemExclamation", IntPtr.Zero, SND_ALIAS | SND_ASYNC | SND_NODEFAULT); } catch { }
    }
}
