using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace ScreenRecorder.Capture;

/// <summary>
/// 屏幕采集的统一入口。
/// 目前使用 GDI BitBlt（通过 CreateDIBSection 直接写入像素内存），
/// 在远程桌面、多显示器、DPR 缩放等场景下都稳定可用。
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class ScreenCapture : IDisposable
{
    private readonly object _gate = new();
    private GdiScreenCapture? _impl;
    private bool _disposed;

    /// <summary>抓取到一帧（BGRA，自上而下，Stride = Width * 4）。</summary>
    public event Action<CapturedFrame>? FrameArrived;

    public int Width { get; private set; }
    public int Height { get; private set; }
    public bool IsRunning => _impl?.IsRunning ?? false;

    /// <summary>诊断信息。</summary>
    public string Status => _impl == null
        ? "(未启动)"
        : $"初始化={_impl.Initialized} 帧数={_impl.FrameCount} 错误={(_impl.LastError?.Message ?? "无")}";

    /// <summary>启动采集：指定屏幕矩形（虚拟桌面坐标）与目标帧率。</summary>
    public void Start(int x, int y, int width, int height, int fps)
    {
        lock (_gate)
        {
            if (_impl != null) throw new InvalidOperationException("屏幕采集已经在运行");

            Width = Math.Max(16, width - (width % 2));
            Height = Math.Max(16, height - (height % 2));
            _impl = new GdiScreenCapture(x, y, Width, Height, fps);
            _impl.FrameArrived += OnFrame;
            _impl.Start();
            Shell.Log.Info($"屏幕采集已启动: {Width}x{Height} @ ({x},{y}), {fps} fps");
        }
    }

    private void OnFrame(CapturedFrame frame)
    {
        if (_disposed) return;
        FrameArrived?.Invoke(frame);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            if (_impl != null)
            {
                _impl.FrameArrived -= OnFrame;
                _impl.Dispose();
                _impl = null;
            }
        }
    }
}

/// <summary>一帧 BGRA 画面。</summary>
[SupportedOSPlatform("windows")]
internal sealed class CapturedFrame
{
    public byte[] Buffer = Array.Empty<byte>();
    public int Width;
    public int Height;
    public int Stride;
    public long SystemRelativeTimeMs;
}
