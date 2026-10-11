using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ScreenRecorder.Shell;

public enum AudioSourceMode
{
    SystemOnly = 0,
    MicrophoneOnly = 1,
    Both = 2,
    None = 3,
}

public enum CaptureAreaMode
{
    FullScreen = 0,
    PrimaryScreen = 1,
    RegionSelect = 2,
}

public sealed class AppSettings
{
    public CaptureAreaMode Area { get; set; } = CaptureAreaMode.FullScreen;
    public int MonitorIndex { get; set; } = 0;
    /// <summary>区域录制：屏幕坐标（虚拟桌面坐标系）。</summary>
    public int RegionX { get; set; }
    public int RegionY { get; set; }
    public int RegionWidth { get; set; } = 1280;
    public int RegionHeight { get; set; } = 720;

    public AudioSourceMode Audio { get; set; } = AudioSourceMode.Both;
    public float SystemVolume { get; set; } = 1.0f;
    public float MicVolume { get; set; } = 1.0f;
    public string? MicrophoneDeviceId { get; set; }

    public int Fps { get; set; } = 30;
    public int VideoBitrateKbps { get; set; } = 8000;
    public int AudioBitrateKbps { get; set; } = 192;

    public string OutputDirectory { get; set; } = "";
    public bool BallVisibleInRecording { get; set; }
    public bool ShowTrayIcon { get; set; } = true;
    public bool OpenFolderWhenSaved { get; set; } = true;
    public bool PlaySoundOnStartStop { get; set; } = true;

    public double BallLeft { get; set; } = double.NaN;
    public double BallTop { get; set; } = double.NaN;
    public double BallSize { get; set; } = 72;

    [JsonIgnore]
    public string EffectiveOutputDirectory
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(OutputDirectory) && Directory.Exists(OutputDirectory))
                return OutputDirectory;
            return DefaultOutputDirectory;
        }
    }

    private static string? _defaultOutputDir;

    /// <summary>默认输出目录：我的视频\屏录球（不可写时退回「我的文档」或临时目录）。</summary>
    public static string DefaultOutputDirectory
    {
        get
        {
            if (_defaultOutputDir != null) return _defaultOutputDir;

            var candidates = new List<string>();
            try
            {
                candidates.Add(Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "屏录球"));
            }
            catch { }
            try
            {
                candidates.Add(Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "屏录球"));
            }
            catch { }
            candidates.Add(Path.Combine(AppContext.BaseDirectory, "recordings"));

            foreach (var dir in candidates)
            {
                if (string.IsNullOrWhiteSpace(dir)) continue;
                try
                {
                    Directory.CreateDirectory(dir);
                    _defaultOutputDir = dir;
                    return dir;
                }
                catch { }
            }
            _defaultOutputDir = Path.Combine(AppContext.BaseDirectory, "recordings");
            return _defaultOutputDir;
        }
    }

    // ---------- 持久化 ----------

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        // 万一将来又冒出 NaN/∞ 字段，也不至于整个设置存不下去
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    /// <summary>设置目录：优先 %LOCALAPPDATA%（可写时），否则退回程序目录 / 临时目录。</summary>
    public static string SettingsDirectory
    {
        get
        {
            if (_settingsDir != null) return _settingsDir;
            var candidates = new List<string>();
            try
            {
                candidates.Add(Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScreenRecorder"));
            }
            catch { }
            try { candidates.Add(AppContext.BaseDirectory); } catch { }
            try { candidates.Add(Path.Combine(AppContext.BaseDirectory, "recordings")); } catch { }

            foreach (var dir in candidates)
            {
                if (string.IsNullOrWhiteSpace(dir)) continue;
                try
                {
                    Directory.CreateDirectory(dir);
                    // 真正验证可写，而不是只看目录能否创建
                    string probe = Path.Combine(dir, ".write-probe");
                    File.WriteAllText(probe, "x");
                    File.Delete(probe);
                    _settingsDir = dir;
                    return dir;
                }
                catch { }
            }
            _settingsDir = Path.Combine(AppContext.BaseDirectory, "recordings");
            return _settingsDir;
        }
    }

    private static string? _settingsDir;

    public static string SettingsPath => Path.Combine(SettingsDirectory, "settings.json");

    private static AppSettings? _current;

    public static AppSettings Current => _current ??= Load();

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath), JsonOpts);
                if (s != null)
                {
                    if (s.Fps is < 1 or > 120) s.Fps = 30;
                    if (s.BallSize is < 40 or > 160) s.BallSize = 72;
                    return s;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error("读取设置失败，使用默认设置", ex);
        }
        return new AppSettings();
    }

    /// <summary>把 NaN / ±∞ 这类写不进 JSON 的数值洗成安全值（默认值）。</summary>
    /* 注意：BallLeft / BallTop 的 NaN 是「还没定位」的合法含义，
       由 JsonNumberHandling.AllowNamedFloatingPointLiterals 负责写成 "NaN" 并读回，
       所以这里**不要**去动它们（洗成 NaN 等于没洗）。 */
    private void SanitizeNonFinite()
    {
        if (!float.IsFinite(SystemVolume)) SystemVolume = 1.0f;
        if (!float.IsFinite(MicVolume)) MicVolume = 1.0f;
        if (!double.IsFinite(BallSize)) BallSize = 72;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(SettingsDirectory);
            string tmp = SettingsPath + ".tmp";
            // 先把 NaN / ±∞ 洗掉：这些值写不进 JSON，会抛 ArgumentException（日志里那条「保存设置失败」）
            SanitizeNonFinite();
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, JsonOpts));
            File.Move(tmp, SettingsPath, overwrite: true);
        }
        catch (Exception ex)
        {
            Log.Error("保存设置失败", ex);
        }
    }
}
