using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace ScreenRecorder.Capture;
/// <summary>
/// GDI BitBlt 抓屏（兼容性兜底方案）。
/// 当 Windows.Graphics.Capture 不可用（远程桌面、部分虚拟显示驱动、受策略限制的会话）时使用。
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class GdiScreenCapture : IDisposable
{
    private readonly object _gate = new();
    private Thread? _thread;
    private volatile bool _running;
    private readonly int _x, _y, _width, _height;
    private readonly int _fps;
    private byte[] _buffer = Array.Empty<byte>();
    private int _version;
    private long _lastTick;

    public event Action<CapturedFrame>? FrameArrived;

    public bool IsRunning => _running;
    public int Width => _width;
    public int Height => _height;

    /// <summary>采集线程内部的异常（用于诊断）。</summary>
    public Exception? LastError { get; private set; }

    /// <summary>采集线程是否已经完成初始化。</summary>
    public volatile bool Initialized;

    /// <summary>已经产出的帧数。</summary>
    public long FrameCount => Interlocked.Read(ref _frameCount);
    private long _frameCount;

    /// <summary>当前帧的字节内容（BGRA），供同线程内拷贝使用。</summary>
    public byte[] CurrentBuffer
    {
        get { lock (_gate) return _buffer; }
    }

    public GdiScreenCapture(int x, int y, int width, int height, int fps)
    {
        _x = x; _y = y; _width = width; _height = height;
        _fps = Math.Clamp(fps, 1, 120);
    }

    public void Start()
    {
        if (_running) return;
        _running = true;
        _thread = new Thread(CaptureLoop)
        {
            IsBackground = true,
            Name = "GdiScreenCapture",
            Priority = ThreadPriority.AboveNormal,
        };
        _thread.Start();
    }

    private void CaptureLoop()
    {
        int stride = _width * 4;
        int imageSize = stride * _height;
        var buffer = new byte[imageSize];
        _buffer = buffer;

        IntPtr screenDc = IntPtr.Zero, memDc = IntPtr.Zero, dib = IntPtr.Zero, oldBitmap = IntPtr.Zero;
        IntPtr dibBits = IntPtr.Zero;
        try
        {
            screenDc = GetDC(IntPtr.Zero);
            if (screenDc == IntPtr.Zero) throw new InvalidOperationException("GetDC(NULL) 失败");

            memDc = CreateCompatibleDC(screenDc);
            if (memDc == IntPtr.Zero) throw new InvalidOperationException("CreateCompatibleDC 失败");

            var bmi = new BITMAPINFO
            {
                bmiHeader = new BITMAPINFOHEADER
                {
                    biSize = Marshal.SizeOf<BITMAPINFOHEADER>(),
                    biWidth = _width,
                    biHeight = -_height,          // 负数 = 自上而下
                    biPlanes = 1,
                    biBitCount = 32,
                    biCompression = 0,            // BI_RGB
                    biSizeImage = (uint)imageSize,
                },
            };

            // 用 NULL 作为参考 DC 创建 DIB section，拿到 GDI 自己分配的像素内存
            dib = CreateDIBSection(IntPtr.Zero, ref bmi, DIB_RGB_COLORS, out dibBits, IntPtr.Zero, 0);
            if (dib == IntPtr.Zero || dibBits == IntPtr.Zero)
                throw new InvalidOperationException("CreateDIBSection 失败");
            oldBitmap = SelectObject(memDc, dib);
            Initialized = true;
            Shell.Log.Info($"GDI 抓屏线程就绪: {_width}x{_height} @ ({_x},{_y}), {_fps} fps, bits=0x{dibBits.ToInt64():X}");

            int interval = Math.Max(1, 1000 / _fps);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            long nextTick = 0;
            var frame = new CapturedFrame
            {
                Buffer = buffer,
                Width = _width,
                Height = _height,
                Stride = stride,
            };

            while (_running)
            {
                long elapsed = sw.ElapsedMilliseconds;
                if (elapsed < nextTick) { Thread.Sleep(1); continue; }
                nextTick += interval;
                if (nextTick < elapsed) nextTick = elapsed + interval;

                if (!BitBlt(memDc, 0, 0, _width, _height, screenDc, _x, _y, SRCCOPY))
                    continue;

                GdiFlush();
                // 从 GDI 的像素内存拷回托管数组
                unsafe
                {
                    new Span<byte>((void*)dibBits, imageSize).CopyTo(buffer);
                }

                _version++;
                _lastTick = elapsed;
                frame.SystemRelativeTimeMs = elapsed;
                Interlocked.Increment(ref _frameCount);
                FrameArrived?.Invoke(frame);
            }
        }
        catch (Exception ex)
        {
            LastError = ex;
            Shell.Log.Error("GDI 抓屏循环异常", ex);
        }
        finally
        {
            if (oldBitmap != IntPtr.Zero && memDc != IntPtr.Zero) SelectObject(memDc, oldBitmap);
            if (dib != IntPtr.Zero) DeleteObject(dib);
            if (memDc != IntPtr.Zero) DeleteDC(memDc);
            if (screenDc != IntPtr.Zero) ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    public void Dispose()
    {
        _running = false;
        try { _thread?.Join(1500); } catch { }
        _thread = null;
    }

    // ---------------- P/Invoke ----------------

    private const int SRCCOPY = 0x00CC0020;
    private const int CAPTUREBLT = 0x40000000;
    private const uint DIB_RGB_COLORS = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public int biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO
    {
        public BITMAPINFOHEADER bmiHeader;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
        public uint[] bmiColors;
    }

    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("gdi32.dll", ExactSpelling = true)] private static extern bool BitBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, int rop);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateDIBSection(IntPtr dc, ref BITMAPINFO info, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [DllImport("gdi32.dll")] private static extern bool GdiFlush();
}
