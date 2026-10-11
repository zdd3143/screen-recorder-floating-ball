using System;
using System.Drawing;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Controls;
using ScreenRecorder.Capture;
using ScreenRecorder.Shell;

namespace ScreenRecorder.UI;

/// <summary>托盘图标（用 WinForms 的 NotifyIcon，图标动态绘制）。</summary>
[SupportedOSPlatform("windows")]
internal sealed class TrayIcon : IDisposable
{
    private readonly System.Windows.Forms.NotifyIcon _notify;
    private readonly Icon _idle;
    private readonly Icon _recording;
    private readonly System.Windows.Window _owner;
    private bool _disposed;

    public TrayIcon(System.Windows.Window owner)
    {
        _owner = owner;
        _idle = CreateIcon(System.Drawing.Color.FromArgb(0x2E, 0x7B, 0xF6));
        _recording = CreateIcon(System.Drawing.Color.FromArgb(0xE2, 0x3B, 0x3B));

        _notify = new System.Windows.Forms.NotifyIcon
        {
            Icon = _idle,
            Text = "屏录球 - 单击开始录制",
            Visible = true,
            ContextMenuStrip = BuildMenu(),
        };
        _notify.MouseClick += (_, e) =>
        {
            if (e.Button == System.Windows.Forms.MouseButtons.Left) RestoreBall();
        };
        _notify.DoubleClick += (_, _) => RestoreBall();
    }

    private System.Windows.Forms.ContextMenuStrip BuildMenu()
    {
        var menu = new System.Windows.Forms.ContextMenuStrip();
        var show = new System.Windows.Forms.ToolStripMenuItem("显示悬浮球");
        show.Click += (_, _) => RestoreBall();
        menu.Items.Add(show);

        var start = new System.Windows.Forms.ToolStripMenuItem("开始 / 停止录制");
        start.Click += (_, _) => ((FloatBallWindow)_owner).ToggleRecording();
        menu.Items.Add(start);

        var pause = new System.Windows.Forms.ToolStripMenuItem("暂停 / 继续");
        pause.Click += (_, _) => ((FloatBallWindow)_owner).TogglePause();
        menu.Items.Add(pause);

        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());

        var settings = new System.Windows.Forms.ToolStripMenuItem("设置…");
        settings.Click += (_, _) => ((FloatBallWindow)_owner).OpenSettings();
        menu.Items.Add(settings);

        var folder = new System.Windows.Forms.ToolStripMenuItem("打开保存文件夹");
        folder.Click += (_, _) => ((FloatBallWindow)_owner).OpenOutputFolder();
        menu.Items.Add(folder);

        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());

        var exit = new System.Windows.Forms.ToolStripMenuItem("退出");
        exit.Click += (_, _) => ((FloatBallWindow)_owner).ExitApp();
        menu.Items.Add(exit);

        return menu;
    }

    public void RestoreBall()
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            _owner.Show();
            _owner.Activate();
            _owner.Topmost = true;
        });
    }

    public void UpdateState(RecorderState state)
    {
        _notify.Icon = state is RecorderState.Recording or RecorderState.Paused ? _recording : _idle;
        _notify.Text = state switch
        {
            RecorderState.Recording => "屏录球 - 正在录制",
            RecorderState.Paused => "屏录球 - 已暂停",
            _ => "屏录球 - 就绪",
        };
    }

    public void ShowBalloon(string title, string text)
    {
        try
        {
            _notify.BalloonTipTitle = title;
            _notify.BalloonTipText = text;
            _notify.ShowBalloonTip(3000);
        }
        catch { }
    }

    private static Icon CreateIcon(System.Drawing.Color color)
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(System.Drawing.Color.Transparent);
            using var brush = new SolidBrush(color);
            g.FillEllipse(brush, 3, 3, 26, 26);
            using var white = new SolidBrush(System.Drawing.Color.White);
            g.FillEllipse(white, 12, 12, 8, 8);
        }
        IntPtr h = bmp.GetHicon();
        try
        {
            using var tmp = Icon.FromHandle(h);
            return (Icon)tmp.Clone();
        }
        finally
        {
            DestroyIcon(h);
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _notify.Visible = false; } catch { }
        try { _notify.Dispose(); } catch { }
        try { _idle.Dispose(); } catch { }
        try { _recording.Dispose(); } catch { }
    }
}
