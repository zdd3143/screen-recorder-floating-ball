using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Windows;
using ScreenRecorder.Capture;
using ScreenRecorder.Capture.Audio;
using ScreenRecorder.Capture.Encoding;
using ScreenRecorder.Shell;

namespace ScreenRecorder.UI;

[SupportedOSPlatform("windows")]
public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly List<MonitorInfo> _monitors;

    public SettingsWindow(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;
        _monitors = ScreenHelper.EnumerateMonitors();

        foreach (var m in _monitors) CmbMonitors.Items.Add(m.Display);
        foreach (var fps in new[] { 15, 20, 24, 25, 30, 45, 60 }) CmbFps.Items.Add(fps + " fps");
        CmbQuality.Items.Add("流畅（体积最小，4 Mbps）");
        CmbQuality.Items.Add("均衡（推荐，8 Mbps）");
        CmbQuality.Items.Add("清晰（12 Mbps）");
        CmbQuality.Items.Add("极致（20 Mbps）");

        foreach (var d in WasapiAudioCapture.EnumerateDevices(true)) CmbMics.Items.Add(d);
        if (CmbMics.Items.Count == 0) CmbMics.Items.Add("（未检测到麦克风）");

        LoadFromSettings();

        BtnOk.Click += (_, _) => SaveAndClose();
        BtnCancel.Click += (_, _) => { DialogResult = false; Close(); };
        BtnBrowse.Click += (_, _) => BrowseFolder();
        BtnPickRegion.Click += (_, _) => PickRegion();

        var ffmpeg = FFmpegRecorder.FindFFmpeg();
        TxtInfo.Text = ffmpeg == null
            ? "⚠ 未找到 ffmpeg.exe，无法录制。请把 ffmpeg.exe 放到程序目录或 tools\\ffmpeg 目录下。"
            : $"编码器：ffmpeg（H.264 + AAC）\n路径：{ffmpeg}\n\n快捷键：Ctrl+Alt+R 开始/停止，Ctrl+Alt+P 暂停/继续";
    }

    private void LoadFromSettings()
    {
        switch (_settings.Area)
        {
            case CaptureAreaMode.FullScreen: RbFull.IsChecked = true; break;
            case CaptureAreaMode.RegionSelect: RbRegion.IsChecked = true; break;
            default:
                if (_settings.MonitorIndex > 0 && _settings.MonitorIndex < _monitors.Count) RbMonitor.IsChecked = true;
                else RbPrimary.IsChecked = true;
                break;
        }
        CmbMonitors.SelectedIndex = Math.Clamp(_settings.MonitorIndex, 0, Math.Max(0, _monitors.Count - 1));
        UpdateRegionText();

        switch (_settings.Audio)
        {
            case AudioSourceMode.SystemOnly: RbAudioSystem.IsChecked = true; break;
            case AudioSourceMode.MicrophoneOnly: RbAudioMic.IsChecked = true; break;
            case AudioSourceMode.None: RbAudioNone.IsChecked = true; break;
            default: RbAudioBoth.IsChecked = true; break;
        }

        SldSysVol.Value = Math.Clamp(_settings.SystemVolume * 100, 0, 150);
        SldMicVol.Value = Math.Clamp(_settings.MicVolume * 100, 0, 150);

        int fpsIndex = new[] { 15, 20, 24, 25, 30, 45, 60 }.ToList().IndexOf(_settings.Fps);
        CmbFps.SelectedIndex = fpsIndex >= 0 ? fpsIndex : 4;

        CmbQuality.SelectedIndex = _settings.VideoBitrateKbps switch
        {
            <= 5000 => 0,
            <= 9000 => 1,
            <= 14000 => 2,
            _ => 3,
        };

        TxtOutput.Text = _settings.EffectiveOutputDirectory;
        ChkOpenFolder.IsChecked = _settings.OpenFolderWhenSaved;
        ChkSound.IsChecked = _settings.PlaySoundOnStartStop;
        ChkTray.IsChecked = _settings.ShowTrayIcon;
        SldBallSize.Value = Math.Clamp(_settings.BallSize, 48, 120);

        // 选择默认麦克风
        if (!string.IsNullOrEmpty(_settings.MicrophoneDeviceId))
        {
            for (int i = 0; i < CmbMics.Items.Count; i++)
            {
                if (CmbMics.Items[i] is AudioDeviceInfo info && info.Id == _settings.MicrophoneDeviceId)
                {
                    CmbMics.SelectedIndex = i;
                    break;
                }
            }
        }
        if (CmbMics.SelectedIndex < 0)
        {
            for (int i = 0; i < CmbMics.Items.Count; i++)
                if (CmbMics.Items[i] is AudioDeviceInfo { IsDefault: true }) { CmbMics.SelectedIndex = i; break; }
        }
    }

    private void UpdateRegionText()
    {
        TxtRegion.Text = _settings.RegionWidth > 0 && _settings.RegionHeight > 0
            ? $"{_settings.RegionWidth} × {_settings.RegionHeight} @ ({_settings.RegionX},{_settings.RegionY})"
            : "未选择";
    }

    private void PickRegion()
    {
        Hide();
        try
        {
            var selector = new RegionSelectorWindow();
            bool? ok = selector.ShowDialog();
            if (ok == true && selector.Confirmed)
            {
                _settings.RegionX = selector.RegionX;
                _settings.RegionY = selector.RegionY;
                _settings.RegionWidth = selector.RegionWidth;
                _settings.RegionHeight = selector.RegionHeight;
                _settings.MonitorIndex = selector.MonitorIndex;
                _settings.Area = CaptureAreaMode.RegionSelect;
                CmbMonitors.SelectedIndex = Math.Clamp(selector.MonitorIndex, 0, Math.Max(0, _monitors.Count - 1));
                RbRegion.IsChecked = true;
                UpdateRegionText();
            }
        }
        catch (Exception ex)
        {
            Log.Error("框选区域失败", ex);
        }
        finally
        {
            Show();
            Activate();
        }
    }

    private void BrowseFolder()
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "选择录制文件的保存位置",
            SelectedPath = TxtOutput.Text,
            ShowNewFolderButton = true,
        };
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            TxtOutput.Text = dialog.SelectedPath;
    }

    private void SaveAndClose()
    {
        try
        {
            if (RbFull.IsChecked == true) _settings.Area = CaptureAreaMode.FullScreen;
            else if (RbRegion.IsChecked == true) _settings.Area = CaptureAreaMode.RegionSelect;
            else if (RbMonitor.IsChecked == true)
            {
                _settings.Area = CaptureAreaMode.PrimaryScreen;
                _settings.MonitorIndex = Math.Max(0, CmbMonitors.SelectedIndex);
            }
            else
            {
                _settings.Area = CaptureAreaMode.PrimaryScreen;
                int primary = _monitors.FindIndex(m => m.IsPrimary);
                _settings.MonitorIndex = primary >= 0 ? primary : 0;
            }

            if (_settings.Area == CaptureAreaMode.RegionSelect && (_settings.RegionWidth < 32 || _settings.RegionHeight < 32))
            {
                MessageBox.Show("请先框选一个有效的录制区域。", "屏录球", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _settings.Audio = RbAudioSystem.IsChecked == true ? AudioSourceMode.SystemOnly
                : RbAudioMic.IsChecked == true ? AudioSourceMode.MicrophoneOnly
                : RbAudioNone.IsChecked == true ? AudioSourceMode.None
                : AudioSourceMode.Both;

            _settings.SystemVolume = (float)(SldSysVol.Value / 100.0);
            _settings.MicVolume = (float)(SldMicVol.Value / 100.0);

            if (CmbMics.SelectedItem is AudioDeviceInfo mic) _settings.MicrophoneDeviceId = mic.Id;

            int[] fpsValues = { 15, 20, 24, 25, 30, 45, 60 };
            _settings.Fps = fpsValues[Math.Clamp(CmbFps.SelectedIndex, 0, fpsValues.Length - 1)];

            _settings.VideoBitrateKbps = Math.Clamp(CmbQuality.SelectedIndex, 0, 3) switch
            {
                0 => 4000,
                1 => 8000,
                2 => 12000,
                _ => 20000,
            };

            string dir = TxtOutput.Text.Trim();
            if (!string.IsNullOrWhiteSpace(dir))
            {
                try { Directory.CreateDirectory(dir); _settings.OutputDirectory = dir; }
                catch (Exception ex)
                {
                    MessageBox.Show("保存目录不可用：" + ex.Message, "屏录球", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            _settings.OpenFolderWhenSaved = ChkOpenFolder.IsChecked == true;
            _settings.PlaySoundOnStartStop = ChkSound.IsChecked == true;
            _settings.ShowTrayIcon = ChkTray.IsChecked == true;
            _settings.BallSize = SldBallSize.Value;
            _settings.Save();

            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            Log.Error("保存设置失败", ex);
            MessageBox.Show("保存设置失败：" + ex.Message, "屏录球", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
