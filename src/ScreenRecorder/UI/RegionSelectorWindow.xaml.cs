using System;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ScreenRecorder.Capture;

namespace ScreenRecorder.UI;

/// <summary>全屏半透明遮罩，让用户拖拽框选录制区域。</summary>
[SupportedOSPlatform("windows")]
public partial class RegionSelectorWindow : Window
{
    private Point _start;
    private bool _dragging;

    /// <summary>选中的区域（屏幕坐标，可能为虚拟桌面坐标）。</summary>
    public int RegionX { get; private set; }
    public int RegionY { get; private set; }
    public int RegionWidth { get; private set; }
    public int RegionHeight { get; private set; }
    public bool Confirmed { get; private set; }

    /// <summary>该区域所在的显示器索引。</summary>
    public int MonitorIndex { get; private set; }

    public RegionSelectorWindow()
    {
        InitializeComponent();

        var bounds = ScreenHelper.VirtualDesktopBounds();
        // 覆盖整个虚拟桌面
        Left = bounds.Left;
        Top = bounds.Top;
        Width = bounds.Width;
        Height = bounds.Height;

        MouseLeftButtonDown += OnDown;
        MouseMove += OnMove;
        MouseLeftButtonUp += OnUp;
        KeyDown += OnKeyDown;
        Loaded += (_, _) => Activate();
    }

    private void OnDown(object sender, MouseButtonEventArgs e)
    {
        _start = e.GetPosition(this);
        _dragging = true;
        Selection.Visibility = Visibility.Visible;
        Canvas.SetLeft(Selection, _start.X);
        Canvas.SetTop(Selection, _start.Y);
        Selection.Width = 0;
        Selection.Height = 0;
        SizeLabel.Text = "";
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        if (!_dragging) return;
        var p = e.GetPosition(this);
        double x = Math.Min(p.X, _start.X);
        double y = Math.Min(p.Y, _start.Y);
        double w = Math.Abs(p.X - _start.X);
        double h = Math.Abs(p.Y - _start.Y);
        Canvas.SetLeft(Selection, x);
        Canvas.SetTop(Selection, y);
        Selection.Width = w;
        Selection.Height = h;
        SizeLabel.Text = $"{(int)w} × {(int)h}";
    }

    private void OnUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;

        var bounds = ScreenHelper.VirtualDesktopBounds();
        int x = (int)(bounds.Left + Canvas.GetLeft(Selection));
        int y = (int)(bounds.Top + Canvas.GetTop(Selection));
        int w = (int)Selection.Width;
        int h = (int)Selection.Height;
        if (w < 32 || h < 32) { Confirmed = false; return; }

        RegionX = x; RegionY = y; RegionWidth = w; RegionHeight = h;

        // 找出该区域中心落在哪个显示器上
        var monitors = ScreenHelper.EnumerateMonitors();
        int cx = x + w / 2, cy = y + h / 2;
        MonitorIndex = 0;
        foreach (var m in monitors)
        {
            if (cx >= m.Bounds.Left && cx < m.Bounds.Right && cy >= m.Bounds.Top && cy < m.Bounds.Bottom)
            {
                MonitorIndex = m.Index;
                break;
            }
        }

        Confirmed = true;
        DialogResult = true;
        Close();
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Confirmed = false;
            DialogResult = false;
            Close();
        }
    }
}
