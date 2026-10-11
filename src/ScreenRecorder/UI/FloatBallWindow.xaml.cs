using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using ScreenRecorder.Capture;
using ScreenRecorder.Shell;

namespace ScreenRecorder.UI;

[SupportedOSPlatform("windows")]
public partial class FloatBallWindow : Window
{
    private readonly AppSettings _settings = AppSettings.Current;
    private readonly RecordingEngine _engine = new();
    private readonly DispatcherTimer _timer;
    private readonly DispatcherTimer _singleClickTimer;

    private bool _leftDown;
    private bool _dragging;

    private IntPtr _hwnd;
    private Storyboard? _pulse;
    private TrayIcon? _tray;

    /// <summary>一次录制保存完成（录制/保存是异步的，外部可以据此等待）。</summary>
    internal event Action<RecordingResult>? SaveCompleted;

    public FloatBallWindow()
    {
        InitializeComponent();

        _engine.StatusChanged += OnStatusChanged;
        _engine.Failed += OnEngineFailed;

        _timer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(200) };
        _timer.Tick += (_, _) => UpdateTimeBadge();

        _singleClickTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(230) };
        _singleClickTimer.Tick += (_, _) =>
        {
            _singleClickTimer.Stop();
            ToggleRecording();
        };

        // ---- 鼠标交互必须显式挂事件（XAML 里没有写事件绑定）----
        RootGrid.MouseLeftButtonDown += OnMouseLeftButtonDown;
        RootGrid.MouseMove += OnMouseMove;
        RootGrid.MouseLeftButtonUp += OnMouseLeftButtonUp;
        RootGrid.MouseRightButtonUp += OnMouseRightButtonUp;

        Loaded += OnLoaded;
        Closed += (_, _) => { try { _tray?.Dispose(); } catch { } };
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var area = SystemParameters.WorkArea;
        double size = Math.Clamp(_settings.BallSize, 48, 132);
        Width = Math.Max(size + 60, 132);
        Height = Math.Max(size + 60, 132);
        BallHost.Width = size;
        BallHost.Height = size;
        Ball.Width = size * 0.75;
        Ball.Height = size * 0.75;
        Halo.Width = size;
        Halo.Height = size;
        IconIdle.Width = IconIdle.Height = Ball.Width;
        IconPaused.Width = IconPaused.Height = Ball.Width;

        if (!double.IsNaN(_settings.BallLeft) && !double.IsNaN(_settings.BallTop))
        {
            Left = Clamp(_settings.BallLeft, area.Left, Math.Max(area.Left, area.Right - Width));
            Top = Clamp(_settings.BallTop, area.Top, Math.Max(area.Top, area.Bottom - Height));
        }
        else
        {
            Left = Math.Max(area.Left, area.Right - Width - 16);
            Top = Math.Max(area.Top, area.Bottom - Height - 16);
        }

        BuildContextMenu();
        if (_settings.ShowTrayIcon) _tray = new TrayIcon(this);
        UpdateVisualState();

        try { _hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle; } catch { }
        Log.Info($"悬浮球已就绪（HWND=0x{_hwnd.ToInt64():X}）");
    }

    private static double Clamp(double v, double min, double max) => v < min ? min : (v > max ? max : v);

    // ------------------------------------------------------------------ 菜单

    private void BuildContextMenu()
    {
        var menu = new ContextMenu
        {
            Background = new SolidColorBrush(Color.FromRgb(0x1B, 0x1B, 0x1F)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(4),
            HasDropShadow = true,
        };

        menu.Items.Add(MenuItem(_engine.State is RecorderState.Recording or RecorderState.Paused
            ? "停止录制" : "开始录制", () => ToggleRecording(), "Ctrl+Alt+R"));
        menu.Items.Add(MenuItem("暂停 / 继续", () => TogglePause(), "Ctrl+Alt+P"));
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuItem("设置…", OpenSettings));
        menu.Items.Add(MenuItem("打开保存文件夹", OpenOutputFolder));
        menu.Items.Add(MenuItem("创建桌面快捷方式", CreateDesktopShortcut));
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuItem("退出", () => ExitApp()));
        ContextMenu = menu;
    }

    private void CreateDesktopShortcut()
    {
        try
        {
            bool created = ShortcutHelper.CreateShortcut(ShortcutHelper.ExecutablePath, ShortcutHelper.ShortcutPath);
            MessageBox.Show(created
                    ? "已在桌面创建快捷方式：" + ShortcutHelper.ShortcutPath
                    : "创建快捷方式失败（桌面可能不可写），详见程序目录 logs\\app.log。",
                "屏录球", MessageBoxButton.OK,
                created ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            Log.Error("创建快捷方式失败", ex);
        }
    }

    private MenuItem MenuItem(string header, Action action, string? gesture = null)
    {
        var item = new MenuItem
        {
            Header = gesture == null ? header : header + "    " + gesture,
            Foreground = new SolidColorBrush(Color.FromRgb(0xF2, 0xF3, 0xF5)),
            Background = System.Windows.Media.Brushes.Transparent,
            Padding = new Thickness(10, 6, 18, 6),
        };
        item.Click += (_, _) => action();
        return item;
    }

    // ------------------------------------------------------------------ 鼠标交互

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        BeginDrag(e.GetPosition(this));
        e.Handled = true;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_leftDown) return;
        if (DragTo(e.GetPosition(this))) e.Handled = true;
    }

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_leftDown) return;
        EndDrag(e.ClickCount);
        e.Handled = true;
    }

    // ---- 交互逻辑本体（与具体输入事件解耦，便于自动化测试） ----

    /// <summary>窗口所在显示器的 DPI 缩放（设备像素 / DIP）。</summary>
    private double DpiScale
    {
        get
        {
            try
            {
                var src = PresentationSource.FromVisual(this);
                if (src?.CompositionTarget != null)
                {
                    var m = src.CompositionTarget.TransformToDevice;
                    return m.M11 <= 0 ? 1.0 : m.M11;
                }
            }
            catch { }
            return 1.0;
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT p);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private POINT _pressCursor;          // 按下时的光标位置（物理像素）
    private int _pressWinX, _pressWinY;  // 按下时的窗口左上角（物理像素）

    /// <summary>光标位置来源，默认读真实光标；自检时可注入固定值。</summary>
    internal Func<(int X, int Y)> CursorSource { get; set; } = () =>
        GetCursorPos(out POINT p) ? (p.X, p.Y) : (0, 0);

    /// <summary>取窗口当前的物理像素位置与尺寸。</summary>
    private (int X, int Y, int W, int H) GetWindowRectPx()
    {
        try
        {
            if (_hwnd == IntPtr.Zero) _hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            if (_hwnd != IntPtr.Zero && GetWindowRect(_hwnd, out RECT r))
                return (r.Left, r.Top, Math.Max(1, r.Right - r.Left), Math.Max(1, r.Bottom - r.Top));
        }
        catch { }
        double scale = DpiScale;
        return ((int)Math.Round(Left * scale), (int)Math.Round(Top * scale),
                (int)Math.Round(Width * scale), (int)Math.Round(Height * scale));
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    internal void BeginDrag(Point windowPoint)
    {
        _leftDown = true;
        _dragging = false;

        var c = CursorSource();
        _pressCursor = new POINT { X = c.X, Y = c.Y };

        var r = GetWindowRectPx();
        _pressWinX = r.X;
        _pressWinY = r.Y;

        try { RootGrid.CaptureMouse(); } catch { }
    }

    /// <summary>移动鼠标；返回 true 表示本次确实是拖动（窗口已跟随移动）。</summary>
    internal bool DragTo(Point windowPoint)
    {
        if (!_leftDown) return false;

        var c = CursorSource();
        double scale = DpiScale;
        double dxPx = c.X - _pressCursor.X;
        double dyPx = c.Y - _pressCursor.Y;

        // 阈值按物理像素算，再乘缩放，保证高 DPI 下手感一致
        double threshold = 5.0 * scale;
        if (!_dragging && (Math.Abs(dxPx) > threshold || Math.Abs(dyPx) > threshold))
            _dragging = true;
        if (!_dragging) return false;

        if (_hwnd == IntPtr.Zero) _hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (_hwnd != IntPtr.Zero)
        {
            // 用物理像素直接移动窗口，避开 DIP 换算带来的漂移
            SetWindowPos(_hwnd, IntPtr.Zero,
                _pressWinX + (int)Math.Round(dxPx), _pressWinY + (int)Math.Round(dyPx),
                0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
        }
        else
        {
            // 还没拿到句柄时退回 DIP 定位
            Left += dxPx / scale;
            Top += dyPx / scale;
        }
        return true;
    }

    /// <summary>抬起左键；返回 true 表示这是一次拖动（而非单击）。</summary>
    internal bool EndDrag(int clickCount)
    {
        _leftDown = false;
        try { RootGrid.ReleaseMouseCapture(); } catch { }

        if (_dragging)
        {
            _dragging = false;
            // 用窗口的真实像素位置回写设置，下次启动位置一致
            var r = GetWindowRectPx();
            double scale = DpiScale;
            _settings.BallLeft = r.X / scale;
            _settings.BallTop = r.Y / scale;
            _settings.Save();
            Log.Info($"悬浮球位置已保存: ({_settings.BallLeft:F0},{_settings.BallTop:F0})");
            return true;
        }

        HandleClick(clickCount);
        return false;
    }

    /// <summary>点击处理：双击打开设置，单击延迟触发开始/停止。</summary>
    internal void HandleClick(int clickCount)
    {
        _singleClickTimer.Stop();
        if (clickCount >= 2)
        {
            OpenSettings();
        }
        else
        {
            _singleClickTimer.Start();
        }
    }

    // 自检用：暴露内部度量
    internal Point PointToScreenPublic(Point p) => PointToScreen(p);
    internal double DpiScalePublic => DpiScale;

    /// <summary>自检用：在创建窗口前固定初始位置，让测试可复现。</summary>
    internal static void SetInitialPositionForTest(double left, double top)
    {
        AppSettings.Current.BallLeft = left;
        AppSettings.Current.BallTop = top;
    }

    /// <summary>自检用：读窗口当前的物理像素矩形。</summary>
    internal (int X, int Y, int W, int H) WindowRectForTest() => GetWindowRectPx();

    private void OnMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        BuildContextMenu();      // 每次重建，保证菜单项文字与当前状态一致
        if (ContextMenu != null)
        {
            ContextMenu.PlacementTarget = this;
            ContextMenu.IsOpen = true;
        }
        e.Handled = true;
    }

    // ------------------------------------------------------------------ 录制控制

    public void ToggleRecording()
    {
        try
        {
            if (_engine.State == RecorderState.Paused)
            {
                _engine.Resume();
                return;
            }
            if (_engine.State == RecorderState.Recording)
            {
                StopRecording();
                return;
            }
            if (_engine.State != RecorderState.Idle) return;
            StartRecording();
        }
        catch (Exception ex)
        {
            Log.Error("切换录制状态失败", ex);
        }
    }

    public void TogglePause()
    {
        if (_engine.State == RecorderState.Recording) _engine.Pause();
        else if (_engine.State == RecorderState.Paused) _engine.Resume();
    }

    private void StartRecording()
    {
        try
        {
            _engine.Start(BuildOptions());
            if (_settings.PlaySoundOnStartStop) SystemSoundsEx.PlayStart();
            BuildContextMenu();
        }
        catch (Exception ex)
        {
            Log.Error("启动录制失败", ex);
            MessageBox.Show("启动录制失败：\n" + ex.Message, "屏录球",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>停止录制。整个保存过程放在后台线程，UI 不会被卡住。</summary>
    private void StopRecording()
    {
        if (_engine.State is RecorderState.Stopping) return;

        _timer.Stop();
        SetBusy(true);
        StatusText("正在保存…");

        var sw = Stopwatch.StartNew();
        System.Threading.Tasks.Task.Run(() =>
        {
            RecordingResult result;
            try { result = _engine.Stop(); }
            catch (Exception ex)
            {
                Log.Error("停止录制失败", ex);
                result = new RecordingResult { Success = false, Error = ex.Message };
            }
            sw.Stop();

            Dispatcher.BeginInvoke(() =>
            {
                SetBusy(false);
                BuildContextMenu();
                UpdateVisualState();
                if (_settings.PlaySoundOnStartStop) SystemSoundsEx.PlayStop();
                try { SaveCompleted?.Invoke(result); } catch { }

                if (result.Success)
                {
                    Log.Info($"保存完成，用时 {sw.ElapsedMilliseconds} ms");
                    if (_settings.OpenFolderWhenSaved) OpenInExplorer(result.FilePath);
                    _tray?.ShowBalloon("录制完成",
                        $"{Path.GetFileName(result.FilePath)}\n{result.FileSize / 1024.0 / 1024.0:F1} MB · {result.Duration:hh\\:mm\\:ss}");
                }
                else
                {
                    MessageBox.Show("录制保存失败：\n" + (result.Error ?? "未知错误") +
                                    "\n\n请查看程序目录 logs\\app.log。", "屏录球",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            });
        });
    }

    private void StatusText(string text)
    {
        TimeBadge.Visibility = Visibility.Visible;
        TimeText.Text = text;
    }

    private void SetBusy(bool busy)
    {
        Ball.Opacity = busy ? 0.55 : 1.0;
        IsEnabled = !busy;
        if (busy) StopPulse();
    }

    private RecordingOptions BuildOptions() => new()
    {
        Area = _settings.Area,
        MonitorIndex = _settings.MonitorIndex,
        RegionX = _settings.RegionX,
        RegionY = _settings.RegionY,
        RegionWidth = _settings.RegionWidth,
        RegionHeight = _settings.RegionHeight,
        Audio = _settings.Audio,
        SystemVolume = _settings.SystemVolume,
        MicVolume = _settings.MicVolume,
        MicrophoneDeviceId = _settings.MicrophoneDeviceId,
        Fps = _settings.Fps,
        VideoBitrateKbps = _settings.VideoBitrateKbps,
        AudioBitrateKbps = _settings.AudioBitrateKbps,
        OutputDirectory = _settings.EffectiveOutputDirectory,
    };

    public void OpenSettings()
    {
        try
        {
            var win = new SettingsWindow(_settings) { Owner = this };
            win.ShowDialog();
            UpdateVisualState();
        }
        catch (Exception ex)
        {
            Log.Error("打开设置失败", ex);
        }
    }

    public void OpenOutputFolder() => OpenInExplorer(null);

    /// <summary>
    /// 在资源管理器中打开保存目录（可顺带选中刚录好的文件）。
    /// 用 shell 的 SHOpenFolderAndSelectItems，避免直接启动 explorer.exe 在某些会话里弹错误框。
    /// </summary>
    public void OpenInExplorer(string? fileToSelect)
    {
        string dir = _settings.EffectiveOutputDirectory;
        try
        {
            Directory.CreateDirectory(dir);

            if (!string.IsNullOrEmpty(fileToSelect) && File.Exists(fileToSelect) &&
                SHOpenFolderAndSelectItems(fileToSelect, true))
                return;

            // 只打开目录（不选中文件）
            if (SHOpenFolderAndSelectItems(dir, false))
                return;

            // 最后兜底：交给系统按目录打开
// 不要用 Process.Start(路径)：那会把目录当程序去执行，导致「拒绝访问」
            try
            {
                var target = string.IsNullOrWhiteSpace(fileToSelect)
                    ? _settings.OutputDirectory
                    : fileToSelect;
                if (string.IsNullOrWhiteSpace(target)) return;
                var full = Path.GetFullPath(target);
                var psi = File.Exists(full)
                    ? new ProcessStartInfo("explorer.exe", "/select,\"" + full + "\"")
                    : new ProcessStartInfo("explorer.exe", "\"" + (Directory.Exists(full) ? full : Path.GetDirectoryName(full)) + "\"");
                psi.UseShellExecute = true;
                Process.Start(psi);
            }
            catch (Exception ex) { Log.Warn("打开文件夹失败：" + ex.Message); }
        }
        catch (Exception ex)
        {
            Log.Error("打开文件夹失败: " + dir, ex);
        }
    }

    // ---------------- 资源管理器定位 ----------------

    [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int SHParseDisplayName(string pszName, IntPtr pbc,
        out IntPtr ppidl, uint sfgaoIn, out uint psfgaoOut);

    [System.Runtime.InteropServices.DllImport("shell32.dll")]
    private static extern int SHOpenFolderAndSelectItems(IntPtr pidlFolder, uint cidl, IntPtr apidl, uint dwFlags);

    [System.Runtime.InteropServices.DllImport("shell32.dll")]
    private static extern void ILFree(IntPtr pidl);

    [System.Runtime.InteropServices.DllImport("ole32.dll")]
    private static extern void CoUninitialize();

    [System.Runtime.InteropServices.DllImport("ole32.dll")]
    private static extern int CoInitializeEx(IntPtr pvReserved, uint dwCoInit);

    /// <summary>打开某个存在的路径（文件则选中它，目录则打开它）。成功返回 true。</summary>
    private static bool SHOpenFolderAndSelectItems(string path, bool selectFile)
    {
        IntPtr pidl = IntPtr.Zero;
        try
        {
            if (SHParseDisplayName(path, IntPtr.Zero, out pidl, 0, out _) != 0 || pidl == IntPtr.Zero)
                return false;

            if (selectFile)
            {
                IntPtr apidl = System.Runtime.InteropServices.Marshal.AllocCoTaskMem(IntPtr.Size);
                try
                {
                    System.Runtime.InteropServices.Marshal.WriteIntPtr(apidl, pidl);
                    return SHOpenFolderAndSelectItems(pidl, 1, apidl, 0) == 0;
                }
                finally { System.Runtime.InteropServices.Marshal.FreeCoTaskMem(apidl); }
            }

            return SHOpenFolderAndSelectItems(pidl, 0, IntPtr.Zero, 0) == 0;
        }
        catch (Exception ex)
        {
            Log.Warn("SHOpenFolderAndSelectItems 失败: " + ex.Message);
            return false;
        }
        finally
        {
            if (pidl != IntPtr.Zero) ILFree(pidl);
        }
    }

    public void ExitApp()
    {
        if (_engine.State != RecorderState.Idle)
        {
            var r = MessageBox.Show("正在录制中，确定要停止并退出吗？", "屏录球",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r != MessageBoxResult.Yes) return;
            Cursor = System.Windows.Input.Cursors.Wait;
            try { _engine.Stop(); } catch { }
        }
        _settings.Save();
        try { _tray?.Dispose(); } catch { }
        _timer.Stop();
        Application.Current.Shutdown();
    }

    // ------------------------------------------------------------------ 状态与外观

    private void OnStatusChanged(string status)
    {
        Dispatcher.BeginInvoke(() =>
        {
            UpdateVisualState();
            if (_engine.State == RecorderState.Recording && !_timer.IsEnabled) _timer.Start();
            if (_engine.State == RecorderState.Idle) _timer.Stop();
            _tray?.UpdateState(_engine.State);
        });
    }

    private void OnEngineFailed(Exception ex)
    {
        Dispatcher.BeginInvoke(() => _tray?.ShowBalloon("录制出错", ex.Message));
    }

    private void UpdateTimeBadge()
    {
        if (_engine.State is RecorderState.Recording or RecorderState.Paused)
        {
            TimeBadge.Visibility = Visibility.Visible;
            var t = _engine.Elapsed;
            TimeText.Text = $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}";
        }
        else if (_engine.State == RecorderState.Idle)
        {
            TimeBadge.Visibility = Visibility.Collapsed;
        }
    }

    private void UpdateVisualState()
    {
        bool recording = _engine.State == RecorderState.Recording;
        bool paused = _engine.State == RecorderState.Paused;

        Color c1, c2;
        if (paused) { c1 = Color.FromRgb(0xF5, 0xA6, 0x23); c2 = Color.FromRgb(0xB5, 0x74, 0x0A); }
        else if (recording) { c1 = Color.FromRgb(0xEC, 0x44, 0x44); c2 = Color.FromRgb(0xA0, 0x16, 0x22); }
        else { c1 = Color.FromRgb(0x2E, 0x7B, 0xF6); c2 = Color.FromRgb(0x16, 0x50, 0xB8); }

        BallStop1.Color = c1;
        BallStop2.Color = c2;
        IconIdle.Visibility = recording || paused ? Visibility.Collapsed : Visibility.Visible;
        IconPaused.Visibility = paused ? Visibility.Visible : Visibility.Collapsed;
        BallShadow.Color = recording ? Color.FromRgb(0xE2, 0x3B, 0x3B) : Color.FromRgb(0x00, 0x00, 0x00);
        Ball.Opacity = 1.0;
        IsEnabled = true;

        if (recording) StartPulse(); else StopPulse();
        UpdateTimeBadge();
        ToolTip = _engine.State switch
        {
            RecorderState.Recording => "正在录制…（单击停止，双击设置，右键菜单）",
            RecorderState.Paused => "已暂停（单击继续）",
            RecorderState.Starting => "正在启动…",
            RecorderState.Stopping => "正在保存…",
            _ => "单击开始录制，双击设置，右键更多",
        };
    }

    private void StartPulse()
    {
        if (_pulse != null) return;
        var anim = new DoubleAnimation(0.15, 0.75, TimeSpan.FromMilliseconds(900))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };
        _pulse = new Storyboard();
        Storyboard.SetTarget(anim, Halo);
        Storyboard.SetTargetProperty(anim, new PropertyPath(OpacityProperty));
        _pulse.Children.Add(anim);
        _pulse.Begin();
    }

    private void StopPulse()
    {
        if (_pulse == null) return;
        try { _pulse.Stop(); } catch { }
        _pulse = null;
        Halo.Opacity = 0;
    }

    public RecorderState State => _engine.State;

    /// <summary>仅供界面自检使用：把悬浮球切到指定外观。</summary>
    internal void SetPreviewState(string state)
    {
        switch (state)
        {
            case "recording":
                BallStop1.Color = Color.FromRgb(0xEC, 0x44, 0x44);
                BallStop2.Color = Color.FromRgb(0xA0, 0x16, 0x22);
                IconIdle.Visibility = Visibility.Collapsed;
                IconPaused.Visibility = Visibility.Collapsed;
                TimeBadge.Visibility = Visibility.Visible;
                TimeText.Text = "00:12";
                StartPulse();
                break;
            case "paused":
                BallStop1.Color = Color.FromRgb(0xF5, 0xA6, 0x23);
                BallStop2.Color = Color.FromRgb(0xB5, 0x74, 0x0A);
                IconIdle.Visibility = Visibility.Collapsed;
                IconPaused.Visibility = Visibility.Visible;
                TimeBadge.Visibility = Visibility.Visible;
                TimeText.Text = "00:12";
                StopPulse();
                break;
            default:
                BallStop1.Color = Color.FromRgb(0x2E, 0x7B, 0xF6);
                BallStop2.Color = Color.FromRgb(0x16, 0x50, 0xB8);
                IconIdle.Visibility = Visibility.Visible;
                IconPaused.Visibility = Visibility.Collapsed;
                TimeBadge.Visibility = Visibility.Collapsed;
                StopPulse();
                break;
        }
    }
}
