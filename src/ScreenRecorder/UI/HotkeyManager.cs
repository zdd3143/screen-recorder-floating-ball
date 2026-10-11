using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Interop;
using ScreenRecorder.Shell;

namespace ScreenRecorder.UI;

/// <summary>全局快捷键：Ctrl+Alt+R 开始/停止，Ctrl+Alt+P 暂停/继续。</summary>
[SupportedOSPlatform("windows")]
internal static class HotkeyManager
{
    private const int WM_HOTKEY = 0x0312;
    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_NOREPEAT = 0x4000;

    private const int IdToggle = 0xB001;
    private const int IdPause = 0xB002;

    private static HwndSource? _source;
    private static FloatBallWindow? _ball;
    private static bool _registered;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hwnd, int id);

    /// <summary>用悬浮球窗口自身的 HWND 注册全局快捷键。</summary>
    public static void Register(FloatBallWindow ball)
    {
        _ball = ball;
        try
        {
            var helper = new WindowInteropHelper(ball);
            IntPtr hwnd = helper.Handle;
            if (hwnd == IntPtr.Zero)
            {
                // 窗口还没创建句柄，等它 Loaded 之后再挂
                ball.SourceInitialized += (_, _) => RegisterOn(ball);
                return;
            }
            RegisterOn(ball);
        }
        catch (Exception ex)
        {
            Log.Warn("注册全局快捷键失败: " + ex.Message);
        }
    }

    private static void RegisterOn(FloatBallWindow ball)
    {
        try
        {
            var helper = new WindowInteropHelper(ball);
            _source = HwndSource.FromHwnd(helper.Handle);
            if (_source == null) { Log.Warn("无法获取窗口消息源，跳过快捷键注册"); return; }
            _source.AddHook(WndProc);

            bool a = RegisterHotKey(helper.Handle, IdToggle, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, 0x52); // R
            bool b = RegisterHotKey(helper.Handle, IdPause, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, 0x50);  // P
            _registered = a || b;
            Log.Info($"注册全局快捷键 Ctrl+Alt+R={a}, Ctrl+Alt+P={b}");
        }
        catch (Exception ex)
        {
            Log.Warn("挂载快捷键钩子失败: " + ex.Message);
        }
    }

    private static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && _ball != null)
        {
            int id = wParam.ToInt32();
            if (id == IdToggle) { _ball.ToggleRecording(); handled = true; }
            else if (id == IdPause) { _ball.TogglePause(); handled = true; }
        }
        return IntPtr.Zero;
    }

    public static void Unregister()
    {
        if (_source == null) return;
        try
        {
            UnregisterHotKey(_source.Handle, IdToggle);
            UnregisterHotKey(_source.Handle, IdPause);
            _source.RemoveHook(WndProc);
        }
        catch { }
        _source = null;
        _registered = false;
    }
}
