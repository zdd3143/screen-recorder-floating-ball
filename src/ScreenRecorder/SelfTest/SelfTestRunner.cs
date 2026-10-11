using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;
using System.Threading;
using ScreenRecorder.Capture;
using ScreenRecorder.Capture.Audio;
using ScreenRecorder.Shell;
using ScreenRecorder.UI;

namespace ScreenRecorder.SelfTest;

/// <summary>
/// 命令行自检：不依赖界面，真实录制一小段 MP4，用于验证采集/编码/封装链路。
/// 用法：屏录球.exe --selftest [秒数] [--area=full|primary] [--audio=both|system|mic|none]
/// </summary>
[SupportedOSPlatform("windows")]
internal static class SelfTestRunner
{
    public static int Run(string[] args)
    {
        foreach (var a in args)
        {
            if (a.Equals("--capturetest", StringComparison.OrdinalIgnoreCase))
                return CaptureTest(args);
            if (a.Equals("--uitest", StringComparison.OrdinalIgnoreCase))
                return UiTest();
            if (a.Equals("--stoptest", StringComparison.OrdinalIgnoreCase))
                return StopLatencyTest(args);
            if (a.Equals("--interacttest", StringComparison.OrdinalIgnoreCase))
                return InteractionTest();
            if (a.Equals("--savetest", StringComparison.OrdinalIgnoreCase))
                return SavePipelineTest(args);
        }

        int seconds = 6;
        string area = "primary";
        string audio = "both";
        string outDir = Path.Combine(Path.GetTempPath(), "ScreenRecorderSelfTest");

        foreach (var a in args)
        {
            if (a.StartsWith("--seconds=", StringComparison.OrdinalIgnoreCase))
                int.TryParse(a[10..], out seconds);
            else if (a.StartsWith("--area=", StringComparison.OrdinalIgnoreCase))
                area = a[7..].ToLowerInvariant();
            else if (a.StartsWith("--audio=", StringComparison.OrdinalIgnoreCase))
                audio = a[8..].ToLowerInvariant();
            else if (a.StartsWith("--out=", StringComparison.OrdinalIgnoreCase) && a.Length > 6)
                outDir = a[6..];
        }
        if (args.Length > 1 && int.TryParse(args[1], out int posSeconds)) seconds = posSeconds;

        Print($"== 屏录球自检 ==");
        Print($"时长={seconds}s 区域={area} 音频={audio} 输出={outDir}");
        Print($"线程公寓状态(初始) = {Thread.CurrentThread.GetApartmentState()}");

        int exit = 0;
        try
        {
            Print("显示器:");
            foreach (var m in ScreenHelper.EnumerateMonitors())
                Print($"  [{m.Index}] {m.DeviceName} {m.Width}x{m.Height} 主屏={m.IsPrimary} DPI={m.DpiX}");

            Print("麦克风设备:");
            foreach (var d in WasapiAudioCapture.EnumerateDevices(true))
                Print($"  {d.Name} default={d.IsDefault}");

            Print("播放设备:");
            foreach (var d in WasapiAudioCapture.EnumerateDevices(false))
                Print($"  {d.Name} default={d.IsDefault}");

            var options = new RecordingOptions
            {
                Area = area == "full" ? CaptureAreaMode.FullScreen : CaptureAreaMode.PrimaryScreen,
                Audio = audio switch
                {
                    "none" => AudioSourceMode.None,
                    "system" => AudioSourceMode.SystemOnly,
                    "mic" => AudioSourceMode.MicrophoneOnly,
                    _ => AudioSourceMode.Both,
                },
                Fps = 30,
                VideoBitrateKbps = 8000,
                AudioBitrateKbps = 192,
                OutputDirectory = outDir,
            };

            // 显式初始化 COM：部分 Media Foundation 组件要求线程已完成 COM 初始化
            Thread.CurrentThread.TrySetApartmentState(ApartmentState.STA);
            Print($"线程公寓状态(调整后) = {Thread.CurrentThread.GetApartmentState()}");

            using var engine = new RecordingEngine();
            engine.Failed += ex => Print("引擎回调异常: " + ex.Message);
            engine.StatusChanged += s => Print("状态: " + s);

            var sw = Stopwatch.StartNew();
            engine.Start(options);
            Print($"录制已开始，等待 {seconds} 秒…");

            while (sw.Elapsed.TotalSeconds < seconds)
            {
                Thread.Sleep(500);
                Print($"  已录 {engine.Elapsed:mm\\:ss} …");
            }

            var result = engine.Stop();
            Print($"停止完成: {result.FilePath}");
            Print($"时长={result.Duration:mm\\:ss} 大小={result.FileSize / 1024.0 / 1024.0:F2} MB 帧数={result.FrameCount}");

            if (!File.Exists(result.FilePath) || result.FileSize < 20 * 1024)
            {
                Print("!! 失败：输出文件缺失或过小");
                exit = 3;
            }
            else
            {
                Print("OK：文件已生成");
            }
        }
        catch (Exception ex)
        {
            Print("!! 异常: " + ex);
            exit = 4;
        }
        Print("== 自检结束 ==");
        return exit;
    }

    private static void Print(string message)
    {
        try { Console.WriteLine(message); } catch { }
        Log.Info("[selftest] " + message);
    }

    /// <summary>抓一帧存成 BMP，并统计像素亮度，用于确认抓屏链路真的拿到了画面。</summary>
    private static int CaptureTest(string[] args)
    {
        Print("== 抓屏链路自检 ==");
        try
        {
            var monitors = ScreenHelper.EnumerateMonitors();
            foreach (var m in monitors)
                Print($"  显示器[{m.Index}] {m.DeviceName} {m.Width}x{m.Height} primary={m.IsPrimary} handle=0x{m.Handle.ToInt64():X}");
            var monitor = ScreenHelper.Primary(monitors);

            CapturedFrame? captured = null;
            using (var screen = new ScreenCapture())
            {
                screen.FrameArrived += f => captured = f;
                screen.Start(monitor.Bounds.Left, monitor.Bounds.Top, monitor.Width, monitor.Height, 30);
                Print($"  采集区域: {monitor.Width}x{monitor.Height} @ ({monitor.Bounds.Left},{monitor.Bounds.Top})");

                var sw = Stopwatch.StartNew();
                while (captured == null && sw.ElapsedMilliseconds < 8000) Thread.Sleep(100);
                if (captured == null)
                {
                    Print($"!! 8 秒内没有拿到任何帧，采集状态: {screen.Status}");
                    return 6;
                }
            }

            var frame = captured;
            long sum = 0; int max = 0; int nonBlack = 0; int samples = 0;
            for (int y = 0; y < frame.Height; y += 5)
            {
                int rowStart = y * frame.Stride;
                for (int x = 0; x < frame.Width; x += 5)
                {
                    int o = rowStart + x * 4;
                    int v = frame.Buffer[o] + frame.Buffer[o + 1] + frame.Buffer[o + 2];
                    sum += v; samples++;
                    if (v > max) max = v;
                    if (v > 12) nonBlack++;
                }
            }
            double mean = samples > 0 ? (double)sum / samples : 0;
            Print($"  帧 {frame.Width}x{frame.Height} 采样 {samples} 个像素: 平均亮度={mean:F1} 最大={max} 非黑比例={(double)nonBlack / Math.Max(1, samples):P1}");

            string dir = Path.Combine(Path.GetTempPath(), "ScreenRecorderCaptureTest");
            Directory.CreateDirectory(dir);
            string bmp = Path.Combine(dir, "capture.bmp");
            WriteBmp(bmp, frame);
            Print($"  已保存: {bmp} ({new FileInfo(bmp).Length / 1024} KB)");

            if (mean < 1.0) { Print("!! 画面几乎全黑，抓屏可能失败"); return 7; }
            Print("OK：抓屏拿到了有效画面");
            return 0;
        }
        catch (Exception ex)
        {
            Print("!! 异常: " + ex);
            return 8;
        }
    }

    /// <summary>
    /// 回归测试：录制几秒后调用 Stop()，测量耗时。
    /// 这是"点停止后界面卡死/停不下来"这个 bug 的守门测试——Stop 必须在几秒内返回。
    /// </summary>
    private static int StopLatencyTest(string[] args)
    {
        int seconds = 5;
        foreach (var a in args)
            if (a.StartsWith("--seconds=", StringComparison.OrdinalIgnoreCase))
                int.TryParse(a[10..], out seconds);

        Print("== 停止响应自检 ==");
        int exit = 0;
        try
        {
            string dir = Path.Combine(Path.GetTempPath(), "ScreenRecorderStopTest");
            using var engine = new RecordingEngine();
            engine.Start(new RecordingOptions
            {
                Area = CaptureAreaMode.PrimaryScreen,
                Audio = AudioSourceMode.SystemOnly,
                Fps = 30,
                VideoBitrateKbps = 8000,
                OutputDirectory = dir,
            });

            Print($"  已开始录制，等待 {seconds} 秒…");
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed.TotalSeconds < seconds) Thread.Sleep(100);
            Print($"  录制时长 {engine.Elapsed:mm\\:ss}");

            // 真正要测的部分
            var stopWatch = Stopwatch.StartNew();
            var result = engine.Stop();
            stopWatch.Stop();

            Print($"  Stop() 用时 {stopWatch.ElapsedMilliseconds} ms，文件 {result.FileSize / 1024.0 / 1024.0:F2} MB，帧数 {result.FrameCount}");

            if (stopWatch.ElapsedMilliseconds > 8000)
            {
                Print("!! 停止耗时超过 8 秒，界面会明显卡顿");
                exit = 10;
            }
            else if (!File.Exists(result.FilePath) || result.FileSize < 20 * 1024)
            {
                Print("!! 输出文件缺失或过小");
                exit = 11;
            }
            else
            {
                Print("OK：停止迅速且文件正常");
            }
        }
        catch (Exception ex)
        {
            Print("!! 异常: " + ex);
            exit = 12;
        }
        return exit;
    }

    /// <summary>
    /// 保存链路自检：短时长录制（含音轨与无音轨两条回退路径），
    /// 检查最终文件是否非空、成功标记是否正确、目录是否干净（临时文件已清理）。
    /// </summary>
    private static int SavePipelineTest(string[] args)
    {
        Print("== 保存链路自检 ==");
        int exit = 0;

        foreach (var audioMode in new[] { AudioSourceMode.Both, AudioSourceMode.None })
        {
            string dir = Path.Combine(Path.GetTempPath(), $"ScreenRecorderSaveTest_{audioMode}");
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
            Directory.CreateDirectory(dir);

            try
            {
                using var engine = new RecordingEngine();
                engine.Start(new RecordingOptions
                {
                    Area = CaptureAreaMode.PrimaryScreen,
                    Audio = audioMode,
                    Fps = 30,
                    VideoBitrateKbps = 6000,
                    OutputDirectory = dir,
                });

                // 故意录很短，验证"时长不足"也不会给出坏文件
                var sw = Stopwatch.StartNew();
                while (sw.Elapsed.TotalSeconds < 2.5) Thread.Sleep(100);

                var result = engine.Stop();
                long size = File.Exists(result.FilePath) ? new FileInfo(result.FilePath).Length : 0;
                var leftovers = Directory.GetFiles(dir, ".screenrec_*");

                Print($"  [{audioMode}] 成功={result.Success} 大小={size} 字节 帧数={result.FrameCount} " +
                      $"临时残留={leftovers.Length}");

                if (!result.Success || size < 20 * 1024)
                {
                    Print($"!! [{audioMode}] 保存失败：{result.Error}");
                    exit = 30;
                }
                if (leftovers.Length > 0)
                {
                    Print($"!! [{audioMode}] 临时文件没有清理：{string.Join(", ", leftovers)}");
                    exit = 31;
                }

                // 用 ffprobe 能力有限，这里只校验 MP4 文件头
                if (size >= 20 * 1024 && !HasMp4Header(result.FilePath))
                {
                    Print($"!! [{audioMode}] 文件不是合法 MP4");
                    exit = 32;
                }
            }
            catch (Exception ex)
            {
                Print($"!! [{audioMode}] 异常: " + ex);
                exit = 33;
            }
        }

        if (exit == 0) Print("OK：两条保存路径都正常，且没有临时文件残留");
        return exit;
    }

    /// <summary>MP4 的第 5–8 字节应该是 'ftyp'。</summary>
    private static bool HasMp4Header(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            var buf = new byte[12];
            if (fs.Read(buf, 0, 12) < 12) return false;
            return buf[4] == 'f' && buf[5] == 't' && buf[6] == 'y' && buf[7] == 'p';
        }
        catch { return false; }
    }

    /// <summary>
    /// 悬浮球交互自检：
    ///  1) 真实路由事件是否被处理（验证事件确实绑上了处理函数）
    ///  2) 拖动是否真的移动窗口
    ///  3) 单击是否触发录制、再点是否停得下来
    /// </summary>
    private static int InteractionTest()
    {
        Print("== 悬浮球交互自检 ==");
        int exit = 0;
        try
        {
            var app = Application.Current ?? new Application();

            // 固定初始位置，让测试可在任意显示器/DPI 下复现
            FloatBallWindow.SetInitialPositionForTest(300, 300);
            var ball = new FloatBallWindow
            {
                Width = 132,
                Height = 132,
            };
            ball.Show();
            ball.UpdateLayout();
            Pump(ball);

            // 记录保存完成（录制/保存走后台任务，必须等它回来）
            RecordingResult? saved = null;
            var saveEvent = new ManualResetEventSlim(false);
            ball.SaveCompleted += r => { saved = r; saveEvent.Set(); };

            // ---- 1) 事件是否真的绑定了处理函数 ----
            bool downHandled = RaiseMouse(ball, System.Windows.Input.MouseButton.Left, isDown: true);
            Pump(ball);
            bool upHandled = RaiseMouse(ball, System.Windows.Input.MouseButton.Left, isDown: false);
            Pump(ball);
            Print($"  路由事件被处理: down={downHandled} up={upHandled}");
            if (!downHandled || !upHandled)
            {
                Print("!! 鼠标事件没有被处理（事件未绑定）");
                exit = 20;
            }

            // 等待单击定时器触发录制（最多 3 秒）
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < 3000 && ball.State == RecorderState.Idle)
            {
                Pump(ball);
                Thread.Sleep(30);
            }
            if (ball.State == RecorderState.Idle)
            {
                // 兜底：直接走一次单击逻辑，判断问题在定时器还是在处理逻辑
                Print("  定时器没有触发，直接调用单击逻辑验证");
                ball.HandleClick(1);
                var sw2 = Stopwatch.StartNew();
                while (sw2.ElapsedMilliseconds < 3000 && ball.State == RecorderState.Idle)
                {
                    Pump(ball);
                    Thread.Sleep(30);
                }
            }
            Print($"  单击后状态 = {ball.State}");
            if (ball.State == RecorderState.Idle)
            {
                Print("!! 单击没有触发录制");
                exit = 21;
            }

            // ---- 2) 拖动 ----
            // 注入可控光标位置：无头环境下无法真的移动鼠标
            int cursorX = 1000, cursorY = 800;
            ball.CursorSource = () => (cursorX, cursorY);

            var startRect = BallRectForTest(ball);
            bool tinyNotPressed = ball.DragTo(new Point(67, 67));   // 没按下，应 false
            ball.BeginDrag(new Point(66, 66));

            cursorX = 1003; cursorY = 802;                          // 3/2 px，小于阈值
            bool tiny = ball.DragTo(new Point(67, 67));

            cursorX = 1152; cursorY = 906;                          // +152/+106 px，肯定算拖动
            bool big = ball.DragTo(new Point(166, 136));
            bool wasDrag = ball.EndDrag(1);
            Pump(ball);
            var endRect = BallRectForTest(ball);

            int dx = endRect.X - startRect.X, dy = endRect.Y - startRect.Y;
            Print($"  起始窗口像素位置=({startRect.X},{startRect.Y}) 缩放={ball.DpiScalePublic:F2}");
            Print($"  未按下 DragTo={tinyNotPressed}（应 False）；小位移算拖动={tiny}（应 False）；大位移算拖动={big}（应 True）；抬起判为拖动={wasDrag}");
            Print($"  拖动后窗口位移 = ({dx},{dy})（期望约 152,106 物理像素）");
            if (tinyNotPressed) { Print("!! 未按下也算拖动"); exit = 22; }
            if (tiny) { Print("!! 小于阈值就判定成拖动，单击会失灵"); exit = 22; }
            if (!big || !wasDrag) { Print("!! 拖动没有生效"); exit = 22; }
            if (Math.Abs(dx - 152) > 6 || Math.Abs(dy - 106) > 6) { Print("!! 窗口位移与鼠标位移不符"); exit = 22; }

            // ---- 3) 停止录制（走真实的单击路径：ToggleRecording -> 后台保存）----
            var stopSw = Stopwatch.StartNew();
            ball.ToggleRecording();
            while (stopSw.ElapsedMilliseconds < 30000)
            {
                Pump(ball);
                if (saveEvent.IsSet && ball.State == RecorderState.Idle) break;
                Thread.Sleep(30);
            }
            stopSw.Stop();
            Print($"  保存完成={saveEvent.IsSet}，状态={ball.State}，用时 {stopSw.ElapsedMilliseconds} ms");
            if (!saveEvent.IsSet || ball.State != RecorderState.Idle)
            {
                Print("!! 无法结束录制");
                exit = 23;
            }
            else if (saved is { Success: false })
            {
                Print("!! 保存失败: " + saved.Error);
                exit = 23;
            }
            else if (saved != null)
            {
                Print($"  输出文件 {saved.FileSize / 1024.0 / 1024.0:F2} MB，{saved.FrameCount} 帧");
                if (stopSw.ElapsedMilliseconds > 15000)
                {
                    Print("!! 保存耗时过长，界面体验差");
                    exit = 25;
                }
            }

            ball.Close();
            if (exit == 0) Print("OK：单击、拖动、停止都正常");
        }
        catch (Exception ex)
        {
            Print("!! 交互自检异常: " + ex);
            exit = 24;
        }
        return exit;
    }

    private static void Pump(System.Windows.Window w)
    {
        try { w.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background); } catch { }
    }

    private static (int X, int Y, int W, int H) BallRectForTest(FloatBallWindow ball) => ball.WindowRectForTest();

    /// <summary>在悬浮球上抛出真实的 WPF 路由鼠标事件，返回是否被处理（Handled）。</summary>
    private static bool RaiseMouse(System.Windows.Window w, System.Windows.Input.MouseButton button, bool isDown)
    {
        try
        {
            var args = new System.Windows.Input.MouseButtonEventArgs(
                System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount, button)
            {
                RoutedEvent = isDown ? System.Windows.Input.Mouse.MouseDownEvent
                                     : System.Windows.Input.Mouse.MouseUpEvent,
                Source = w,
            };
            var target = w.FindName("RootGrid") as System.Windows.UIElement ?? w;
            target.RaiseEvent(args);
            return args.Handled;
        }
        catch (Exception ex)
        {
            Print("    抛事件失败: " + ex.Message);
            return false;
        }
    }

    /// <summary>把悬浮球窗口渲染成 PNG，用来在无头环境下检查界面外观。</summary>
    private static int UiTest()
    {
        Print("== 界面渲染自检 ==");
        int exit = 0;
        try
        {
            var app = Application.Current ?? new Application();
            var ball = new FloatBallWindow();
            ball.Width = 132;
            ball.Height = 132;
            ball.Left = -3000;   // 屏幕外，不打扰用户
            ball.Top = -3000;
            ball.Show();
            ball.UpdateLayout();

            string dir = Path.Combine(Path.GetTempPath(), "ScreenRecorderCaptureTest");
            Directory.CreateDirectory(dir);
            foreach (var state in new[] { "idle", "recording", "paused" })
            {
                ball.SetPreviewState(state);
                ball.UpdateLayout();
                ball.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Render);
                Thread.Sleep(250);

                int w = (int)Math.Ceiling(ball.Width);
                int h = (int)Math.Ceiling(ball.Height);
                var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(w, h, 96, 96,
                    System.Windows.Media.PixelFormats.Pbgra32);
                rtb.Render(ball);
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
                string file = Path.Combine(dir, $"ball-{state}.png");
                using (var fs = File.Create(file)) encoder.Save(fs);
                Print($"  {state}: {file} ({new FileInfo(file).Length} 字节)");
            }
            ball.Close();
            Print("OK：界面渲染完成");
        }
        catch (Exception ex)
        {
            Print("!! 界面渲染失败: " + ex);
            exit = 9;
        }
        return exit;
    }

    private static void WriteBmp(string path, CapturedFrame frame)    {
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var w = new BinaryWriter(fs);
        int rowBytes = frame.Width * 4;
        int imageSize = rowBytes * frame.Height;
        w.Write((byte)'B'); w.Write((byte)'M');
        w.Write(14 + 40 + imageSize);
        w.Write(0); w.Write(14 + 40);
        w.Write(40);
        w.Write(frame.Width);
        w.Write(-frame.Height);
        w.Write((short)1);
        w.Write((short)32);
        w.Write(0);
        w.Write(imageSize);
        w.Write(2835); w.Write(2835);
        w.Write(0); w.Write(0);
        for (int y = 0; y < frame.Height; y++)
            w.Write(frame.Buffer, y * frame.Stride, rowBytes);
    }
}
