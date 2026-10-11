using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using ScreenRecorder.Shell;

namespace ScreenRecorder.Capture.Encoding;

/// <summary>
/// 用 ffmpeg 做编码与封装，分两阶段：
///   阶段一（录制中）：BGRA 原始帧由**独立的写线程**送入 ffmpeg 的 stdin（H.264 编码到临时 MP4）；
///                     音频 PCM 先攒在内存里。
///   阶段二（停止时）：把内存 PCM 写成临时 WAV，再用同一个 ffmpeg 与视频混流成最终 MP4。
///
/// 之所以单独起写线程：管道写会在 ffmpeg 跟不上时阻塞，
/// 如果直接在录制主循环里写，停止录制时就可能卡住界面。这里用有界队列丢旧帧，写线程阻塞也不影响计时线程。
/// </summary>
internal sealed class FFmpegRecorder : IDisposable
{
    private readonly string _ffmpegPath;
    private readonly int _width;
    private readonly int _height;
    private readonly int _fps;
    private readonly int _videoBitrateKbps;
    private readonly int _audioBitrateKbps;
    private readonly bool _hasAudio;
    private readonly string _outputPath;
    private readonly string _videoTempPath;
    private readonly string _audioTempPath;

    private Process? _process;
    private Stream? _videoIn;
    private Task<string>? _errorTask;
    private Thread? _writerThread;
    private volatile bool _writerRunning;
    private readonly object _queueGate = new();
    private readonly Queue<byte[]> _queue = new();
    private readonly SemaphoreSlim _queued = new(0);
    private const int MaxQueuedFrames = 3;      // 允许积压的帧数，超出丢最旧的
    private long _writtenBytes;
    private long _droppedFrames;

    private bool _disposed;
    private bool _completed;
    private readonly List<byte[]> _audioBlocks = new();
    private long _audioBytes;

    public string OutputPath => _outputPath;
    public bool HasAudio => _hasAudio;
    public int AudioSampleRate { get; }
    public int AudioChannels { get; }
    public bool EncoderAlive => _process is { HasExited: false };
    public long DroppedFrames => Interlocked.Read(ref _droppedFrames);

    /// <summary>按优先级查找可用的 ffmpeg.exe。</summary>
    public static string? FindFFmpeg()
    {
        var candidates = new List<string>();
        string baseDir = AppContext.BaseDirectory;
        candidates.Add(Path.Combine(baseDir, "ffmpeg.exe"));
        candidates.Add(Path.Combine(baseDir, "ffmpeg", "ffmpeg.exe"));
        try
        {
            var dir = new DirectoryInfo(baseDir);
            for (int i = 0; i < 6 && dir != null; i++, dir = dir.Parent)
                candidates.Add(Path.Combine(dir.FullName, "tools", "ffmpeg", "ffmpeg.exe"));
        }
        catch { }

        foreach (var c in candidates)
        {
            try { if (File.Exists(c)) return c; } catch { }
        }

        try
        {
            string path = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (var part in path.Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(part)) continue;
                try
                {
                    string full = Path.Combine(part.Trim(), "ffmpeg.exe");
                    if (File.Exists(full)) return full;
                }
                catch { }
            }
        }
        catch { }
        return null;
    }

    public FFmpegRecorder(string ffmpegPath, string outputPath, int width, int height, int fps,
        int videoBitrateKbps, bool hasAudio, int audioBitrateKbps,
        int audioSampleRate = 48000, int audioChannels = 2)
    {
        _ffmpegPath = ffmpegPath;
        _outputPath = outputPath;
        _width = width;
        _height = height;
        _fps = fps;
        _videoBitrateKbps = videoBitrateKbps;
        _audioBitrateKbps = audioBitrateKbps;
        _hasAudio = hasAudio;
        // 临时文件放在输出目录旁边：与最终文件同一个卷，移动是原子的，
        // 也避免系统临时目录被策略限制导致编码器写不出来。
        string outDir = Path.GetDirectoryName(outputPath) ?? Path.GetTempPath();
        string tag = $"{Environment.ProcessId}_{DateTime.Now:HHmmssfff}";
        _videoTempPath = Path.Combine(outDir, $".screenrec_v_{tag}.mp4");
        _audioTempPath = Path.Combine(outDir, $".screenrec_a_{tag}.wav");
        AudioSampleRate = audioSampleRate;
        AudioChannels = audioChannels;
    }

    /// <summary>启动前预检：ffmpeg 可用、输出目录可写、能写出文件。</summary>
    public static void Preflight(string ffmpegPath, string outputDirectory)
    {
        if (string.IsNullOrWhiteSpace(ffmpegPath) || !File.Exists(ffmpegPath))
            throw new InvalidOperationException("找不到 ffmpeg.exe");
        if (string.IsNullOrWhiteSpace(outputDirectory))
            throw new InvalidOperationException("保存目录为空");

        Directory.CreateDirectory(outputDirectory);

        string probe = Path.Combine(outputDirectory, $".screenrec_probe_{Environment.ProcessId}");
        try
        {
            File.WriteAllText(probe, "ok");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"保存目录不可写：{outputDirectory}", ex);
        }
        finally
        {
            try { File.Delete(probe); } catch { }
        }

        // 确认 ffmpeg 本身能跑起来
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            psi.ArgumentList.Add("-version");
            using var proc = Process.Start(psi);
            if (proc == null) throw new InvalidOperationException("无法启动 ffmpeg 进程");
            proc.StandardOutput.ReadToEnd();
            proc.StandardError.ReadToEnd();
            if (!proc.WaitForExit(10000))
            {
                try { proc.Kill(true); } catch { }
                throw new InvalidOperationException("ffmpeg 启动后无响应");
            }
        }
        catch (InvalidOperationException) { throw; }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"ffmpeg 无法运行：{ffmpegPath}", ex);
        }
    }

    public void Start()
    {
        string? dir = Path.GetDirectoryName(_outputPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var args = new List<string>
        {
            "-hide_banner", "-loglevel", "warning", "-y",
            "-f", "rawvideo",
            "-pixel_format", "bgra",
            "-video_size", $"{_width}x{_height}",
            "-framerate", _fps.ToString(),
            "-thread_queue_size", "1024",
            "-i", "pipe:0",
            "-an",
            "-c:v", "libx264",
            "-preset", "veryfast",
            "-tune", "zerolatency",
            "-pix_fmt", "yuv420p",
            "-b:v", $"{_videoBitrateKbps}k",
            "-maxrate", $"{_videoBitrateKbps * 2}k",
            "-bufsize", $"{_videoBitrateKbps * 4}k",
            "-g", (_fps * 2).ToString(),
            _videoTempPath,
        };

        var psi = new ProcessStartInfo
        {
            FileName = _ffmpegPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        Log.Info("启动 ffmpeg（视频编码）: " + string.Join(' ', args));
        _process = Process.Start(psi) ?? throw new InvalidOperationException("无法启动 ffmpeg 进程");
        _errorTask = _process.StandardError.ReadToEndAsync();
        _videoIn = _process.StandardInput.BaseStream;

        _writerRunning = true;
        _writerThread = new Thread(WriteLoop)
        {
            IsBackground = true,
            Name = "FFmpegWriter",
        };
        _writerThread.Start();
    }

    /// <summary>提交一帧 BGRA 图像（非阻塞；队列满时丢弃最旧的帧）。</summary>
    public bool WriteFrame(byte[] bgra, int count)
    {
        if (!_writerRunning || _disposed) return false;
        if (_process is { HasExited: true }) return false;

        var copy = new byte[count];
        Buffer.BlockCopy(bgra, 0, copy, 0, count);

        lock (_queueGate)
        {
            while (_queue.Count >= MaxQueuedFrames)
            {
                _queue.Dequeue();
                Interlocked.Increment(ref _droppedFrames);
                try { _queued.Wait(0); } catch { }
            }
            _queue.Enqueue(copy);
        }
        try { _queued.Release(); } catch { }
        return true;
    }

    private void WriteLoop()
    {
        try
        {
            while (_writerRunning || _queued.CurrentCount > 0)
            {
                if (!_queued.Wait(50)) continue;

                byte[]? frame;
                lock (_queueGate)
                {
                    if (_queue.Count == 0) continue;
                    frame = _queue.Dequeue();
                }

                var stream = _videoIn;
                if (stream == null || frame == null) break;
                try
                {
                    stream.Write(frame, 0, frame.Length);
                    Interlocked.Add(ref _writtenBytes, frame.Length);
                }
                catch (Exception ex)
                {
                    Log.Warn("写视频帧失败（编码器已退出？）: " + ex.Message);
                    break;
                }
            }

            try { _videoIn?.Flush(); } catch { }
            Log.Info($"视频写线程结束：累计 {Interlocked.Read(ref _writtenBytes) / 1024.0 / 1024.0:F1} MB, " +
                     $"丢弃 {Interlocked.Read(ref _droppedFrames)} 帧");
        }
        catch (Exception ex)
        {
            Log.Error("视频写线程异常", ex);
        }
    }

    /// <summary>缓存一段 16bit 交错 PCM（录制结束后与视频混流）。</summary>
    public void WriteAudio(byte[] pcm, int count)
    {
        if (count <= 0) return;
        var block = new byte[count];
        Buffer.BlockCopy(pcm, 0, block, 0, count);
        _audioBlocks.Add(block);
        _audioBytes += count;
    }

    /// <summary>
    /// 关闭视频管道（让可能阻塞的写线程立刻返回），但不等待。
    /// 停止录制时先调用它，保证界面不会被卡住。
    /// </summary>
    public void AbortPipe()
    {
        _writerRunning = false;
        try { _videoIn?.Close(); } catch { }
        _videoIn = null;
        try { _queued.Release(); } catch { }
    }

    /// <summary>
    /// 收尾：等待写线程与 ffmpeg 结束，写出 WAV，混流成最终 MP4。
    /// 分成多级回退，任何一步失败都不会丢掉已编码好的视频。
    /// </summary>
    public void Complete(TimeSpan timeout)
    {
        if (_completed) return;
        _completed = true;

        AbortPipe();
        try { _writerThread?.Join(2000); } catch { }
        _writerThread = null;

        if (_process != null)
        {
            try
            {
                if (!_process.WaitForExit((int)Math.Max(2000, timeout.TotalMilliseconds)))
                {
                    Log.Warn("ffmpeg 视频编码未按时结束，强制结束");
                    try { _process.Kill(true); } catch { }
                    _process.WaitForExit(5000);
                }
            }
            catch (Exception ex) { Log.Error("等待 ffmpeg 结束失败", ex); }

            string err = "";
            try { err = _errorTask?.Result ?? ""; } catch { }
            if (!string.IsNullOrWhiteSpace(err)) Log.Warn("ffmpeg 视频阶段: " + err.Trim());
            Log.Info($"视频编码结束：退出码={SafeExitCode()}，写入原始帧 {Interlocked.Read(ref _writtenBytes) / 1024.0 / 1024.0:F1} MB，" +
                     $"文件={(File.Exists(_videoTempPath) ? new FileInfo(_videoTempPath).Length : 0)} 字节，路径={_videoTempPath}");
        }

        long videoTempSize = FileSize(_videoTempPath);
        if (videoTempSize < 1024)
        {
            Log.Error($"视频阶段没有产生有效文件（{videoTempSize} 字节）。可能原因：编码器无法启动、磁盘不可写、或录制时长过短。");
            LastError = $"编码器没有产出有效视频（{videoTempSize} 字节）";
            return;
        }

        // 1) 有音频：先混流，失败则退回无音轨视频
        if (_hasAudio && _audioBytes > 0 && WriteWav())
        {
            if (MuxAudioVideo() && FileSize(_outputPath) > 1024)
            {
                Log.Info($"保存成功（含音轨）：{_outputPath} ({FileSize(_outputPath)} 字节)");
                CleanupTemps();
                return;
            }

            Log.Warn("混流没有产出有效文件，回退为「仅画面」保存");
            TryDelete(_outputPath);
        }

        // 2) 直接落盘视频（无音轨或混流失败）
        if (PublishVideoOnly(videoTempSize))
        {
            CleanupTemps();
            return;
        }

        // 3) 还失败就保留临时文件，方便排查
        LastError = $"无法写出最终文件：{_outputPath}";
        Log.Error($"{LastError}（临时视频仍保留在 {_videoTempPath}，大小 {videoTempSize} 字节）");
    }

    /// <summary>把已编码好的临时视频搬到最终路径。</summary>
    private bool PublishVideoOnly(long tempSize)
    {
        try
        {
            TryDelete(_outputPath);
            File.Move(_videoTempPath, _outputPath);
            long size = FileSize(_outputPath);
            if (size > 1024)
            {
                Log.Info($"保存成功（无音轨）：{_outputPath} ({size} 字节)");
                return true;
            }
            Log.Error($"移动后文件过小：{size} 字节");
            return false;
        }
        catch (Exception ex)
        {
            Log.Error($"把临时视频移动到最终路径失败（源 {_videoTempPath} {tempSize} 字节 → 目标 {_outputPath}）", ex);
            return false;
        }
    }

    private static long FileSize(string path)
    {
        try { return File.Exists(path) ? new FileInfo(path).Length : 0; }
        catch { return 0; }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    /// <summary>保存过程中的失败原因（供界面提示）。</summary>
    public string? LastError { get; private set; }

    private bool WriteWav()
    {
        try
        {
            using var fs = new FileStream(_audioTempPath, FileMode.Create, FileAccess.Write, FileShare.Read, 1 << 20);
            using var w = new BinaryWriter(fs);
            int blockAlign = AudioChannels * 2;
            long dataLen = _audioBytes;
            w.Write(new[] { 'R', 'I', 'F', 'F' });
            w.Write((int)(36 + dataLen));
            w.Write(new[] { 'W', 'A', 'V', 'E' });
            w.Write(new[] { 'f', 'm', 't', ' ' });
            w.Write(16);
            w.Write((short)1);                       // PCM
            w.Write((short)AudioChannels);
            w.Write(AudioSampleRate);
            w.Write(AudioSampleRate * blockAlign);   // byte rate
            w.Write((short)blockAlign);
            w.Write((short)16);
            w.Write(new[] { 'd', 'a', 't', 'a' });
            w.Write((int)dataLen);
            foreach (var block in _audioBlocks) w.Write(block);
            Log.Info($"音频写入完成: {_audioBytes / 1024.0 / 1024.0:F1} MB, {dataLen / blockAlign} 采样帧");
            _audioBlocks.Clear();
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("写 WAV 失败", ex);
            return false;
        }
    }

    private bool MuxAudioVideo()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = _ffmpegPath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                StandardErrorEncoding = System.Text.Encoding.UTF8,
            };
            foreach (var a in new List<string>
            {
                "-hide_banner", "-loglevel", "warning", "-y",
                "-i", _videoTempPath,
                "-i", _audioTempPath,
                "-map", "0:v:0", "-map", "1:a:0",
                "-c:v", "copy",
                "-c:a", "aac", "-b:a", $"{_audioBitrateKbps}k",
                "-movflags", "+faststart",
                _outputPath,
            }) psi.ArgumentList.Add(a);

            Log.Info("启动 ffmpeg（混流）: " + string.Join(' ', psi.ArgumentList));
            using var proc = Process.Start(psi);
            if (proc == null)
            {
                Log.Error("无法启动 ffmpeg 混流进程");
                return false;
            }
            string err = proc.StandardError.ReadToEnd();
            if (!proc.WaitForExit(180000)) { try { proc.Kill(true); } catch { } }
            if (!string.IsNullOrWhiteSpace(err)) Log.Warn("ffmpeg 混流阶段: " + err.Trim());
            Log.Info($"混流退出码={proc.ExitCode}，输出={_outputPath} ({FileSize(_outputPath)} 字节)");
            return proc.ExitCode == 0;
        }
        catch (Exception ex)
        {
            Log.Error("混流失败", ex);
            return false;
        }
    }

    private int SafeExitCode()
    {
        try { return _process?.ExitCode ?? -1; } catch { return -1; }
    }

    private void CleanupTemps()
    {
        try { if (File.Exists(_videoTempPath)) File.Delete(_videoTempPath); } catch { }
        try { if (File.Exists(_audioTempPath)) File.Delete(_audioTempPath); } catch { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        AbortPipe();
        try { _writerThread?.Join(500); } catch { }
        try
        {
            if (_process is { HasExited: false })
            {
                if (!_process.WaitForExit(2000)) { try { _process.Kill(true); } catch { } }
            }
        }
        catch { }
        try { _process?.Dispose(); } catch { }
        _process = null;
        _videoIn = null;
        CleanupTemps();
    }
}
