using System;
using System.Windows;

namespace ScreenRecorder;

/// <summary>
/// 占位类型。真正的应用启动逻辑在 <see cref="EntryPoint"/> 中，
/// 主题资源以 pack URI 形式加载（见 EntryPoint.LoadThemeResources）。
/// </summary>
internal static class AppInfo
{
    public const string DisplayName = "屏录球";
    public const string Version = "1.0.0";
}
