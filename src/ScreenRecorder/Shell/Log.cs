using System;
using System.IO;
using System.Text;

namespace ScreenRecorder.Shell;

/// <summary>极简文件日志：%LOCALAPPDATA%\ScreenRecorder\logs\app.log</summary>
public static class Log
{
    private static readonly object Gate = new();
    private static string? _path;

    /// <summary>
    /// 日志目录：优先程序目录下的 logs（便于随程序一起查看），不可写时退回 %LOCALAPPDATA%。
    /// </summary>
    public static string LogDirectory
    {
        get
        {
            if (_dir != null) return _dir;
            var candidates = new List<string>();
            try { candidates.Add(Path.Combine(AppContext.BaseDirectory, "logs")); } catch { }
            try
            {
                candidates.Add(Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "ScreenRecorder", "logs"));
            }
            catch { }
            try { candidates.Add(Path.Combine(Path.GetTempPath(), "ScreenRecorderLogs")); } catch { }

            foreach (var dir in candidates)
            {
                if (string.IsNullOrWhiteSpace(dir)) continue;
                try { Directory.CreateDirectory(dir); _dir = dir; return dir; } catch { }
            }
            _dir = Path.GetTempPath();
            return _dir;
        }
    }

    private static string? _dir;

    public static string FilePath => _path ??= Path.Combine(LogDirectory, "app.log");

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                if (File.Exists(FilePath) && new FileInfo(FilePath).Length > 1024 * 512)
                    File.Delete(FilePath);
                File.AppendAllText(FilePath,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}",
                    Encoding.UTF8);
            }
        }
        catch { /* 日志失败不影响主流程 */ }
    }

    public static void Info(string message) => Write("INFO  " + message);
    public static void Warn(string message) => Write("WARN  " + message);
    public static void Error(string message) => Write("ERROR " + message);

    public static void Error(string message, Exception ex) =>
        Write($"ERROR {message} :: {ex.GetType().Name}: {ex.Message}{Environment.NewLine}{ex}");
}
