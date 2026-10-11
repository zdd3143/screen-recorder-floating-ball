using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;
using System.Threading;
using ScreenRecorder.Capture.Audio;
using ScreenRecorder.Capture.Encoding;
using ScreenRecorder.Shell;

namespace ScreenRecorder.Capture;

public enum RecorderState
{
    Idle,
    Starting,
    Recording,
    Paused,
    Stopping,
}

public sealed class RecordingResult
{
    public string FilePath { get; init; } = "";
    public TimeSpan Duration { get; init; }
    public long FileSize { get; init; }
    public int FrameCount { get; init; }
    public bool Success { get; init; } = true;
    public string? Error { get; init; }
}

/// <summary>
/// 录制引擎：Windows.Graphics.Capture 抓屏 + WASAPI 抓音 + ffmpeg 编码封装。
/// 时钟以单调递增的 Stopwatch 为准，音视频都按同一条时间轴推进，保证同步。
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class RecordingEngine : IDisposable
{
    private readonly object _lifecycle = new();
    private readonly object _frameGate = new();
    private readonly object _sinkGate = new();

    private ScreenCapture? _screen;    private WasapiAudioCapture? _systemAudio;
    private WasapiAudioCapture? _micAudio;
    private FFmpegRecorder? _sink;

    private Thread? _tickThread;
    private volatile bool _tickRunning;

    private Stopwatch _clock = new();
    private long _pauseAccumMs;
    private long _pausedAtMs;

    private byte[]? _latestFrame;
    private int _latestFrameVersion;
    private readonly List<byte[]> _framePool = new();
    private byte[] _blackFrame = Array.Empty<byte>();

    private readonly RingBuffer _systemBuffer = new();
    private readonly RingBuffer _micBuffer = new();
    private byte[] _mixBuffer = Array.Empty<byte>();
    private byte[] _systemChunk = Array.Empty<byte>();
    private byte[] _micChunk = Array.Empty<byte>();
    private long _audioCursorBytes;

    private int _width;
    private int _height;
    private int _fps;
    private int _frameCount;
    private string _outputPath = "";
    private bool _audioEnabled;
    private long _droppedFrames;
    private string? _lastSinkError;

    public RecorderState State { get; private set; } = RecorderState.Idle;

    public TimeSpan Elapsed => State switch
    {
        RecorderState.Recording => TimeSpan.FromMilliseconds(Math.Max(0, _clock.ElapsedMilliseconds - _pauseAccumMs)),
        RecorderState.Paused => TimeSpan.FromMilliseconds(Math.Max(0, _pausedAtMs - _pauseAccumMs)),
        _ => TimeSpan.Zero,
    };

    public event Action<string>? StatusChanged;
    public event Action<Exception>? Failed;

    public string LastFilePath { get; private set; } = "";
    public RecordingResult? LastResult { get; private set; }
    public long DroppedFrames => Interlocked.Read(ref _droppedFrames);

    // ------------------------------------------------------------------ 公共 API

    public void Start(RecordingOptions options)
    {
        lock (_lifecycle)
        {
            if (State != RecorderState.Idle)
                throw new InvalidOperationException("当前正在录制中");

            State = RecorderState.Starting;
            try
            {
                StartCore(options);
                State = RecorderState.Recording;
                StatusChanged?.Invoke("录制中");
            }
            catch
            {
                CleanupCore();
                State = RecorderState.Idle;
                throw;
            }
        }
    }

    public void Pause()
    {
        lock (_lifecycle)
        {
            if (State != RecorderState.Recording) return;
            _pausedAtMs = _clock.ElapsedMilliseconds;
            State = RecorderState.Paused;
            StatusChanged?.Invoke("已暂停");
            Log.Info("录制已暂停");
        }
    }

    public void Resume()
    {
        lock (_lifecycle)
        {
            if (State != RecorderState.Paused) return;
            _pauseAccumMs += _clock.ElapsedMilliseconds - _pausedAtMs;
            State = RecorderState.Recording;
            StatusChanged?.Invoke("录制中");
            Log.Info("录制已继续");
        }
    }

    public RecordingResult Stop()
    {
        string path;
        int frames;
        TimeSpan duration;
        string? sinkError = null;
        lock (_lifecycle)
        {
            if (State == RecorderState.Idle)
                return LastResult ?? new RecordingResult { FilePath = LastFilePath };

            duration = Elapsed;
            State = RecorderState.Stopping;
            StatusChanged?.Invoke("正在保存…");
            path = _outputPath;
            frames = _frameCount;

            // 1) 先让录制主循环停下（有超时，不会无限等待）
            StopTick();

            // 2) 立刻关闭编码器管道：即使写线程正阻塞在管道写，也会马上返回
            try { _sink?.AbortPipe(); } catch { }

            // 3) 补上最后一段音频，再等编码器收尾
            try { PumpAudio(_clock.ElapsedMilliseconds - _pauseAccumMs + 80); } catch { }

            CleanupCore();
            sinkError = _lastSinkError;

            State = RecorderState.Idle;
        }

        long size = 0;
        try { if (File.Exists(path)) size = new FileInfo(path).Length; } catch { }

        const long minAcceptable = 20 * 1024;   // 低于 20KB 基本可以确定是坏文件
        bool success = size >= minAcceptable;

        LastFilePath = path;
        var result = new RecordingResult
        {
            FilePath = path,
            Duration = duration,
            FileSize = size,
            FrameCount = frames,
            Success = success,
            Error = success ? null : BuildSaveError(sinkError, path, size, frames, duration),
        };
        LastResult = result;
        StatusChanged?.Invoke(result.Success ? "已保存" : "保存失败");
        Log.Info($"录制结束: {path} ({size / 1024.0 / 1024.0:F2} MB, {frames} 帧, {duration:hh\\:mm\\:ss}, 成功={success})");
        return result;
    }

    private string BuildSaveError(string? sinkError, string path, long size, int frames, TimeSpan duration)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"输出文件不完整（{size} 字节）。");
        sb.AppendLine();
        sb.AppendLine($"· 文件：{path}");
        sb.AppendLine($"· 时长：{duration:hh\\:mm\\:ss}，写入帧数：{frames}");
        sb.AppendLine($"· 保存目录：{(Path.GetDirectoryName(path) ?? "?")}");
        if (!string.IsNullOrEmpty(sinkError)) sb.AppendLine("· 编码器：" + sinkError);
        if (duration.TotalSeconds < 1.5)
            sb.AppendLine("· 提示：录制时间过短（不足 1.5 秒）时可能来不及产出有效文件，请录久一点再停止。");
        sb.AppendLine();
        sb.Append("完整过程记录见程序目录 logs\\app.log（搜索「视频编码结束」与「混流」）。");
        return sb.ToString();
    }

    public void Dispose()
    {
        try { Stop(); } catch { }
    }

    // ------------------------------------------------------------------ 启动

    private void StartCore(RecordingOptions options)
    {
        _fps = Math.Clamp(options.Fps, 1, 120);

        var monitors = ScreenHelper.EnumerateMonitors();
        var virtualBounds = ScreenHelper.VirtualDesktopBounds(monitors);

        // 计算采集矩形（虚拟桌面坐标）
        int capX, capY, capW, capH;
        if (options.Area == CaptureAreaMode.FullScreen)
        {
            capX = virtualBounds.Left; capY = virtualBounds.Top;
            capW = virtualBounds.Width; capH = virtualBounds.Height;
        }
        else if (options.Area == CaptureAreaMode.RegionSelect &&
                 options.RegionWidth >= 16 && options.RegionHeight >= 16)
        {
            capX = options.RegionX; capY = options.RegionY;
            capW = options.RegionWidth; capH = options.RegionHeight;
        }
        else
        {
            var monitor = (options.MonitorIndex >= 0 && options.MonitorIndex < monitors.Count)
                ? monitors[options.MonitorIndex]
                : ScreenHelper.Primary(monitors);
            capX = monitor.Bounds.Left; capY = monitor.Bounds.Top;
            capW = monitor.Width; capH = monitor.Height;
        }

        // 不能超出虚拟桌面
        capX = Math.Max(capX, virtualBounds.Left);
        capY = Math.Max(capY, virtualBounds.Top);
        capW = Math.Min(capW, virtualBounds.Right - capX);
        capH = Math.Min(capH, virtualBounds.Bottom - capY);

        _width = Math.Max(16, capW - (capW % 2));
        _height = Math.Max(16, capH - (capH % 2));

        string dir = string.IsNullOrWhiteSpace(options.OutputDirectory)
            ? AppSettings.DefaultOutputDirectory
            : options.OutputDirectory;
        try
        {
            Directory.CreateDirectory(dir);
        }
        catch (Exception ex)
        {
            Log.Warn($"保存目录不可用（{dir}）：{ex.Message}，改用默认目录");
            dir = AppSettings.DefaultOutputDirectory;
            Directory.CreateDirectory(dir);
        }
        _outputPath = Path.Combine(dir, $"录屏_{DateTime.Now:yyyyMMdd_HHmmss_fff}.mp4");

        _audioEnabled = options.Audio != AudioSourceMode.None;

        string ffmpeg = FFmpegRecorder.FindFFmpeg()
            ?? throw new InvalidOperationException("找不到 ffmpeg.exe，无法启动编码器");

        // 预检：目录可写 + ffmpeg 能运行，避免录完才发现存不下来
        FFmpegRecorder.Preflight(ffmpeg, dir);

        if (!_audioEnabled)
            Log.Info("本次录制不包含音频");

        _sink = new FFmpegRecorder(ffmpeg, _outputPath, _width, _height, _fps,
            options.VideoBitrateKbps, _audioEnabled, options.AudioBitrateKbps);

        if (_audioEnabled)
        {
            if (options.Audio is AudioSourceMode.SystemOnly or AudioSourceMode.Both)
            {
                try
                {
                    _systemAudio = new WasapiAudioCapture(AudioKind.SystemLoopback, null, options.SystemVolume);
                    _systemAudio.DataAvailable = (data, count, _, _) => _systemBuffer.Write(data, count);
                    _systemAudio.Start();
                }
                catch (Exception ex)
                {
                    Log.Error("启动系统声音采集失败，将继续录制视频", ex);
                    _systemAudio?.Dispose();
                    _systemAudio = null;
                }
            }
            if (options.Audio is AudioSourceMode.MicrophoneOnly or AudioSourceMode.Both)
            {
                try
                {
                    _micAudio = new WasapiAudioCapture(AudioKind.Microphone, options.MicrophoneDeviceId, options.MicVolume);
                    _micAudio.DataAvailable = (data, count, _, _) => _micBuffer.Write(data, count);
                    _micAudio.Start();
                }
                catch (Exception ex)
                {
                    Log.Error("启动麦克风采集失败，将继续录制视频", ex);
                    _micAudio?.Dispose();
                    _micAudio = null;
                }
            }
        }

        _sink.Start();

        _screen = new ScreenCapture();
        _screen.FrameArrived += OnFrameArrived;
        _screen.Start(capX, capY, _width, _height, _fps);

        _frameCount = 0;
        _audioCursorBytes = 0;
        _pauseAccumMs = 0;
        _pausedAtMs = 0;
        _droppedFrames = 0;
        _blackFrame = new byte[_width * 4 * _height];
        _clock = Stopwatch.StartNew();

        StartTick();
        Log.Info($"开始录制：{_width}x{_height}@{_fps}, 音频={options.Audio}, 文件={_outputPath}");
    }

    private void OnFrameArrived(CapturedFrame frame)
    {
        lock (_frameGate)
        {
            byte[] target = RentFrameBuffer(frame.Buffer.Length);
            Buffer.BlockCopy(frame.Buffer, 0, target, 0, frame.Buffer.Length);
            _latestFrame = target;
            _latestFrameVersion++;
        }
    }

    private byte[] RentFrameBuffer(int size)
    {
        for (int i = 0; i < _framePool.Count; i++)
            if (_framePool[i].Length == size) { var b = _framePool[i]; _framePool.RemoveAt(i); return b; }
        if (_framePool.Count > 0) { var b = _framePool[0]; _framePool.RemoveAt(0); return b; }
        return new byte[size];
    }

    // ------------------------------------------------------------------ 主循环

    private void StartTick()
    {
        _tickRunning = true;
        _tickThread = new Thread(TickLoop)
        {
            IsBackground = true,
            Name = "RecordingTick",
            Priority = ThreadPriority.AboveNormal,
        };
        _tickThread.Start();
    }

    private void StopTick()
    {
        _tickRunning = false;
        // 写线程可能正阻塞在管道写；先把管道关掉，让主循环马上能退出
        try { _sink?.AbortPipe(); } catch { }
        try { _tickThread?.Join(2500); } catch { }
        _tickThread = null;
    }

    private void TickLoop()
    {
        const int tickMs = 4;
        long slotDurationMs = Math.Max(1, 1000 / _fps);
        long nextSlot = 0;
        var sw = Stopwatch.StartNew();

        while (_tickRunning)
        {
            try
            {
                if (State == RecorderState.Paused) { Thread.Sleep(tickMs); continue; }

                long elapsed = _clock.ElapsedMilliseconds - _pauseAccumMs;
                PumpVideo(elapsed, ref nextSlot, slotDurationMs);
                PumpAudio(elapsed);
            }
            catch (Exception ex)
            {
                Log.Error("录制主循环异常", ex);
                Failed?.Invoke(ex);
                _tickRunning = false;
                break;
            }

            int sleep = tickMs - (int)(sw.ElapsedMilliseconds % tickMs);
            if (sleep > 0) Thread.Sleep(sleep);
        }
    }

    private void PumpVideo(long elapsedMs, ref long nextSlot, long slotDurationMs)
    {
        if (_sink == null) return;

        while (elapsedMs >= nextSlot)
        {
            long slot = nextSlot;
            nextSlot += slotDurationMs;

            byte[] payload;
            lock (_frameGate)
            {
                payload = _latestFrame ?? _blackFrame;
            }

            int count = _width * 4 * _height;
            if (payload.Length < count) payload = _blackFrame;

            bool ok;
            lock (_sinkGate)
            {
                ok = _sink.WriteFrame(payload, count);
            }
            if (!ok) { Interlocked.Increment(ref _droppedFrames); return; }
            _frameCount++;
        }
    }

    /// <summary>把音频按真实时间轴推进到 elapsed 时刻（不足补静音），与视频同轴。</summary>
    private void PumpAudio(long elapsedMs)
    {
        if (!_audioEnabled || _sink == null) return;

        long bytesPerSecond = (long)_sink.AudioSampleRate * _sink.AudioChannels * 2;
        long targetBytes = elapsedMs * bytesPerSecond / 1000;
        long remaining = targetBytes - _audioCursorBytes;
        if (remaining <= 0) return;

        const int maxChunkBytes = 48000 * 2 * 2 / 4; // 单次最多 250ms，避免长时间静音后一次性灌入
        int want = (int)Math.Min(remaining, maxChunkBytes);
        want -= want % (_sink.AudioChannels * 2);
        if (want <= 0) return;

        if (_mixBuffer.Length < want) _mixBuffer = new byte[want];
        Array.Clear(_mixBuffer, 0, want);

        bool hasSystem = _systemAudio is { IsRunning: true };
        bool hasMic = _micAudio is { IsRunning: true };

        if (hasSystem)
        {
            if (_systemChunk.Length < want) _systemChunk = new byte[want];
            int n = _systemBuffer.Read(_systemChunk, want);
            if (n > 0) MixInto(_mixBuffer, _systemChunk, n);
        }
        if (hasMic)
        {
            if (_micChunk.Length < want) _micChunk = new byte[want];
            int n = _micBuffer.Read(_micChunk, want);
            if (n > 0) MixInto(_mixBuffer, _micChunk, n);
        }

        lock (_sinkGate)
        {
            _sink.WriteAudio(_mixBuffer, want);
        }
        _audioCursorBytes += want;
    }

    private static void MixInto(byte[] destination, byte[] source, int count)
    {
        for (int i = 0; i + 2 <= count; i += 2)
        {
            short a = (short)(destination[i] | (destination[i + 1] << 8));
            short b = (short)(source[i] | (source[i + 1] << 8));
            int sum = a + b;
            if (sum > 32767) sum = 32767; else if (sum < -32768) sum = -32768;
            destination[i] = (byte)(sum & 0xFF);
            destination[i + 1] = (byte)((sum >> 8) & 0xFF);
        }
    }

    // ------------------------------------------------------------------ 收尾

    private void CleanupCore()
    {
        try
        {
            if (_screen != null) { _screen.FrameArrived -= OnFrameArrived; _screen.Dispose(); }
        }
        catch (Exception ex) { Log.Warn("释放屏幕采集: " + ex.Message); }
        _screen = null;

        try { _systemAudio?.Dispose(); } catch { }
        try { _micAudio?.Dispose(); } catch { }
        _systemAudio = null;
        _micAudio = null;

        // 收尾：等编码器写盘 + 混流（Complete 内部有超时，最坏情况也会结束）
        var sw = Stopwatch.StartNew();
        try { _sink?.Complete(TimeSpan.FromSeconds(30)); } catch (Exception ex) { Log.Error("关闭编码器", ex); }
        long completeMs = sw.ElapsedMilliseconds;
        _lastSinkError = _sink?.LastError;
        try { _sink?.Dispose(); } catch { }
        _sink = null;
        if (completeMs > 300) Log.Info($"编码器收尾用时 {completeMs} ms");

        _latestFrame = null;
        _framePool.Clear();
        _systemBuffer.Clear();
        _micBuffer.Clear();
        _clock.Stop();
        _blackFrame = Array.Empty<byte>();
    }

    /// <summary>音频采集线程写入、主循环读取的环形字节缓冲。</summary>
    private sealed class RingBuffer
    {
        private readonly object _gate = new();
        private byte[] _data = new byte[48000 * 2 * 2 * 6]; // 6 秒
        private int _read;
        private int _write;

        public void Write(byte[] source, int count)
        {
            lock (_gate)
            {
                if (count <= 0) return;
                int free = _data.Length - (_write - _read);
                if (count > free)
                {
                    int drop = count - free;
                    drop -= drop % 4;
                    _read += drop;
                    if (_read > _write) _read = _write;
                }
                if (_write + count > _data.Length)
                {
                    int keep = _write - _read;
                    if (keep > 0) Buffer.BlockCopy(_data, _read, _data, 0, keep);
                    _read = 0;
                    _write = keep;
                }
                if (_write + count > _data.Length) return;
                Buffer.BlockCopy(source, 0, _data, _write, count);
                _write += count;
            }
        }

        public int Read(byte[] destination, int count)
        {
            lock (_gate)
            {
                int available = _write - _read;
                int take = Math.Min(available, count);
                take -= take % 4;
                if (take <= 0) return 0;
                Buffer.BlockCopy(_data, _read, destination, 0, take);
                _read += take;
                if (_read == _write) { _read = 0; _write = 0; }
                return take;
            }
        }

        public void Clear()
        {
            lock (_gate) { _read = 0; _write = 0; }
        }
    }
}

/// <summary>启动录制所需的参数。</summary>
public sealed class RecordingOptions
{
    public CaptureAreaMode Area { get; set; } = CaptureAreaMode.PrimaryScreen;
    public int MonitorIndex { get; set; } = 0;
    public int RegionX { get; set; }
    public int RegionY { get; set; }
    public int RegionWidth { get; set; }
    public int RegionHeight { get; set; }
    public AudioSourceMode Audio { get; set; } = AudioSourceMode.Both;
    public float SystemVolume { get; set; } = 1f;
    public float MicVolume { get; set; } = 1f;
    public string? MicrophoneDeviceId { get; set; }
    public int Fps { get; set; } = 30;
    public int VideoBitrateKbps { get; set; } = 8000;
    public int AudioBitrateKbps { get; set; } = 192;
    public string OutputDirectory { get; set; } = AppSettings.DefaultOutputDirectory;
}
