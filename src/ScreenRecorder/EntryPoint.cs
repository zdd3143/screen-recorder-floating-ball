using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using ScreenRecorder.Capture.Encoding;
using ScreenRecorder.Shell;
using ScreenRecorder.UI;

namespace ScreenRecorder;

/// <summary>
/// 程序入口。不使用 App.xaml 生成的入口点（那会导致 WPF 启动时重复创建 Application 实例），
/// 主题资源由 Application.Resources 以 pack URI 直接加载。
/// </summary>
internal static class EntryPoint
{
    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int processId);

    [DllImport("kernel32.dll")]
    private static extern bool AllocConsole();

    private static Mutex? _singleInstanceMutex;

    [STAThread]
    private static int Main(string[] args)
    {
        // ---- 命令行自检模式 ----
        if (args.Length > 0 && (args[0].Equals("--selftest", StringComparison.OrdinalIgnoreCase)
                                || args[0].Equals("--capturetest", StringComparison.OrdinalIgnoreCase)
                                || args[0].Equals("--uitest", StringComparison.OrdinalIgnoreCase)
                                || args[0].Equals("--stoptest", StringComparison.OrdinalIgnoreCase)
                                || args[0].Equals("--interacttest", StringComparison.OrdinalIgnoreCase)
                                || args[0].Equals("--savetest", StringComparison.OrdinalIgnoreCase)
                                || args[0].Equals("--shot", StringComparison.OrdinalIgnoreCase)))
        {
            if (!AttachConsole(-1)) AllocConsole();
            try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch { }
            try
            {
                string[] effective;
                if (args[0].Equals("--capturetest", StringComparison.OrdinalIgnoreCase))
                    effective = new[] { "--selftest", "--capturetest" }.Concat(args[1..]).ToArray();
                else if (args[0].Equals("--stoptest", StringComparison.OrdinalIgnoreCase))
                    effective = new[] { "--selftest", "--stoptest" }.Concat(args[1..]).ToArray();
                else if (args[0].Equals("--shot", StringComparison.OrdinalIgnoreCase))
                    effective = new[] { "--selftest", "--shot" }.Concat(args[1..]).ToArray();
                else if (args[0].Equals("--savetest", StringComparison.OrdinalIgnoreCase))
                    effective = new[] { "--selftest", "--savetest" };
                else if (args[0].Equals("--uitest", StringComparison.OrdinalIgnoreCase))
                    effective = new[] { "--selftest", "--uitest" };
                else if (args[0].Equals("--interacttest", StringComparison.OrdinalIgnoreCase))
                    effective = new[] { "--selftest", "--interacttest" };
                else
                    effective = args;
                return SelfTest.SelfTestRunner.Run(effective);
            }
            catch (Exception ex)
            {
                try { Console.WriteLine("自检崩溃: " + ex); } catch { }
                Log.Error("自检崩溃", ex);
                return 5;
            }
        }

        // ---- 单实例 ----
        _singleInstanceMutex = new Mutex(true, @"Local\ScreenRecorderFloatingBall_SingleInstance", out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show("屏录球已经在运行了，请看桌面上的悬浮球。", "屏录球",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return 0;
        }

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };

        app.DispatcherUnhandledException += (_, e) =>
        {
            try { Log.Write("DISPATCHER: " + e.Exception); } catch { }
            MessageBox.Show("发生未处理的错误：" + e.Exception.Message, "屏录球",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            e.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            try { Log.Write("UNHANDLED: " + e.ExceptionObject); } catch { }
        };

        LoadOptionalTheme(app);

        Log.Info("==== 屏录球启动 ====");        Log.Info("日志目录: " + Log.LogDirectory);
        Log.Info("ffmpeg: " + (FFmpegRecorder.FindFFmpeg() ?? "未找到"));

        try { ShortcutHelper.EnsureDesktopShortcut(); }
        catch (Exception ex) { Log.Warn("创建快捷方式失败: " + ex.Message); }

        try
        {
            var ball = new FloatBallWindow();
            app.MainWindow = ball;
            ball.Show();
            HotkeyManager.Register(ball);
            try
            {
                var helper = new System.Windows.Interop.WindowInteropHelper(ball);
                Log.Info($"悬浮球已显示：Left={ball.Left:F0} Top={ball.Top:F0} W={ball.Width:F0} H={ball.Height:F0} " +
                         $"Visible={ball.IsVisible} State={ball.WindowState} HWND=0x{helper.Handle.ToInt64():X} " +
                         $"Topmost={ball.Topmost} 工作区={SystemParameters.WorkArea}");
            }
            catch (Exception ex) { Log.Warn("读取窗口状态失败: " + ex.Message); }
            Log.Info("悬浮球已显示，进入消息循环");
        }
        catch (Exception ex)
        {
            Log.Error("创建悬浮球窗口失败", ex);
            MessageBox.Show("屏录球启动失败：\n" + ex, "屏录球", MessageBoxButton.OK, MessageBoxImage.Error);
            return 1;
        }

        int code = app.Run();
        try { HotkeyManager.Unregister(); } catch { }
        try { _singleInstanceMutex.ReleaseMutex(); } catch { }
        try { _singleInstanceMutex.Dispose(); } catch { }
        return code;
    }

    /// <summary>可选主题资源：存在就合并，不存在也不影响运行。</summary>
    private static void LoadOptionalTheme(Application app)
    {
        foreach (var uri in new[]
        {
            "/屏录球;component/Styles/Theme.xaml",
            "pack://application:,,,/屏录球;component/Styles/Theme.xaml",
        })
        {
            try
            {
                var dict = new ResourceDictionary { Source = new Uri(uri, UriKind.RelativeOrAbsolute) };
                app.Resources.MergedDictionaries.Add(dict);
                Log.Info("已加载主题资源: " + uri);
                return;
            }
            catch
            {
                // 忽略：主题是可选的
            }
        }
    }
}
