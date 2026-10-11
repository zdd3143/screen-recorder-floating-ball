using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using ScreenRecorder.Shell;

namespace ScreenRecorder.UI;

/// <summary>
/// 桌面/开始菜单快捷方式。优先用 WScript.Shell，失败时回退到原生 IShellLink + IPersistFile。
/// </summary>
[SupportedOSPlatform("windows")]
public static class ShortcutHelper
{
    public const string ShortcutName = "屏录球.lnk";

    public static string DesktopDirectory =>
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

    public static string ShortcutPath => System.IO.Path.Combine(DesktopDirectory, ShortcutName);

    public static string ExecutablePath
    {
        get
        {
            string? exe = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exe) && exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                return exe;
            return System.IO.Path.Combine(AppContext.BaseDirectory, "屏录球.exe");
        }
    }

    /// <summary>桌面缺少快捷方式（或指向了别处）时创建/修正它。</summary>
    public static bool EnsureDesktopShortcut()
    {
        try
        {
            string exe = ExecutablePath;
            if (!System.IO.File.Exists(exe))
            {
                Log.Warn($"找不到可执行文件，跳过创建快捷方式: {exe}");
                return false;
            }

            string lnk = ShortcutPath;
            if (System.IO.File.Exists(lnk))
            {
                string? target = ReadTarget(lnk);
                if (string.Equals(target, exe, StringComparison.OrdinalIgnoreCase))
                    return false;   // 已经指向本程序，无需重复创建
                Log.Info($"桌面快捷方式指向 {target}，将更新为 {exe}");
                try { System.IO.File.Delete(lnk); } catch { }
            }

            bool ok = CreateShortcut(exe, lnk);
            if (ok) Log.Info($"已创建桌面快捷方式: {lnk} -> {exe}");
            else Log.Warn("创建桌面快捷方式失败（桌面可能不可写）");
            return ok;
        }
        catch (Exception ex)
        {
            Log.Warn("创建桌面快捷方式失败: " + ex.Message);
            return false;
        }
    }

    /// <summary>在指定位置创建快捷方式。</summary>
    public static bool CreateShortcut(string targetExe, string lnkPath)
    {
        if (string.IsNullOrWhiteSpace(targetExe) || !System.IO.File.Exists(targetExe))
        {
            Log.Warn("目标程序不存在: " + targetExe);
            return false;
        }
        return CreateViaWsScript(targetExe, lnkPath) || CreateViaShellLink(targetExe, lnkPath);
    }

    // ---------------------------------------------------------------- WScript.Shell

    private static bool CreateViaWsScript(string exe, string lnk)
    {
        dynamic? shell = null;
        try
        {
            var type = Type.GetTypeFromProgID("WScript.Shell");
            if (type == null) return false;
            shell = Activator.CreateInstance(type);
            dynamic link = shell!.CreateShortcut(lnk);
            link.TargetPath = exe;
            link.WorkingDirectory = System.IO.Path.GetDirectoryName(exe) ?? AppContext.BaseDirectory;
            link.Description = "屏录球 - 点击悬浮球录制屏幕与声音";
            link.IconLocation = exe + ",0";
            link.WindowStyle = 1;
            link.Save();
            return System.IO.File.Exists(lnk);
        }
        catch (Exception ex)
        {
            Log.Warn("WScript.Shell 创建快捷方式失败: " + ex.Message);
            return false;
        }
        finally { Release(shell); }
    }

    private static string? ReadTarget(string lnk)
    {
        dynamic? shell = null;
        try
        {
            var type = Type.GetTypeFromProgID("WScript.Shell");
            if (type == null) return null;
            shell = Activator.CreateInstance(type);
            dynamic link = shell!.CreateShortcut(lnk);
            return (string)link.TargetPath;
        }
        catch { return null; }
        finally { Release(shell); }
    }

    // ---------------------------------------------------------------- IShellLink

    private static bool CreateViaShellLink(string exe, string lnk)
    {
        object? shellLink = null;
        try
        {
            shellLink = new ShellLink();
            var link = (IShellLinkW)shellLink;
            link.SetPath(exe);
            link.SetWorkingDirectory(System.IO.Path.GetDirectoryName(exe) ?? AppContext.BaseDirectory);
            link.SetDescription("屏录球 - 点击悬浮球录制屏幕与声音");
            link.SetIconLocation(exe, 0);

            var file = (IPersistFile)shellLink;
            file.Save(lnk, true);
            return System.IO.File.Exists(lnk);
        }
        catch (Exception ex)
        {
            Log.Warn("IShellLink 创建快捷方式失败: " + ex.Message);
            return false;
        }
        finally { Release(shellLink); }
    }

    private static void Release(object? comObject)
    {
        try
        {
            if (comObject != null && Marshal.IsComObject(comObject)) Marshal.ReleaseComObject(comObject);
        }
        catch { }
    }

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink
    {
    }

    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, IntPtr pfd, int fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, int dwReserved);
        void Resolve(IntPtr hwnd, int fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    [ComImport, Guid("0000010b-0000-0000-C000-000000000046"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPersistFile
    {
        void GetClassID(out Guid pClassID);
        [PreserveSig] int IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, int dwMode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string? pszFileName, [MarshalAs(UnmanagedType.Bool)] bool fRemember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
        void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
    }
}
