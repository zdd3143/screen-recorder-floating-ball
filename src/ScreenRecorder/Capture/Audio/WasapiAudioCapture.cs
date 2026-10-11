using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;

namespace ScreenRecorder.Capture.Audio;

public enum AudioKind
{
    SystemLoopback,
    Microphone,
}

/// <summary>把各种设备格式统一转换成 16bit PCM / 48kHz / 双声道。</summary>
[SupportedOSPlatform("windows")]
internal sealed class AudioFormatConverter
{
    private readonly int _srcRate;
    private readonly int _srcChannels;
    private readonly int _dstRate;
    private readonly int _dstChannels;
    private readonly bool _isFloat;
    private readonly int _bytesPerSample;

    private readonly float[] _scratch = new float[16384];
    private readonly byte[] _outBytes = new byte[8192];
    private double _pos;

    public AudioFormatConverter(int srcRate, int srcChannels, bool isFloat, int dstRate, int dstChannels)
    {
        _srcRate = srcRate <= 0 ? dstRate : srcRate;
        _srcChannels = Math.Max(1, srcChannels);
        _dstRate = dstRate;
        _dstChannels = Math.Max(1, dstChannels);
        _isFloat = isFloat;
        _bytesPerSample = isFloat ? 4 : 2;
    }

    /// <summary>输入一段交错采样，输出目标格式的字节（返回实际写入长度）。</summary>
    public int Convert(byte[] source, int sourceCount, ref byte[] destination)
    {
        int frameBytes = _srcChannels * _bytesPerSample;
        if (frameBytes <= 0) return 0;
        int srcFrames = sourceCount / frameBytes;
        if (srcFrames <= 0) return 0;

        if (_scratch.Length < srcFrames * _srcChannels)
        {
            // 极少发生，直接同步扩容（数组足够大时不会走到这里）
            return 0;
        }

        // 1) 解码为 float
        for (int i = 0; i < srcFrames; i++)
        {
            for (int c = 0; c < _srcChannels; c++)
            {
                int offset = i * frameBytes + c * _bytesPerSample;
                float v;
                if (_isFloat) v = BitConverter.ToSingle(source, offset);
                else v = BitConverter.ToInt16(source, offset) / 32768f;
                _scratch[i * _srcChannels + c] = v;
            }
        }

        // 2) 重采样 + 声道映射 → int16
        double step = (double)_srcRate / _dstRate;
        int produced = 0;
        int maxFrames = _outBytes.Length / (2 * _dstChannels);

        while (_pos < srcFrames - 1 && produced < maxFrames)
        {
            int i0 = (int)_pos;
            double frac = _pos - i0;
            for (int c = 0; c < _dstChannels; c++)
            {
                int sc = c < _srcChannels ? c : _srcChannels - 1;
                float a = _scratch[i0 * _srcChannels + sc];
                float b = _scratch[(i0 + 1) * _srcChannels + sc];
                float v = a + (b - a) * (float)frac;
                if (v > 1f) v = 1f; else if (v < -1f) v = -1f;
                short s = (short)(v * 32767f);
                _outBytes[produced * 2 * _dstChannels + c * 2] = (byte)(s & 0xFF);
                _outBytes[produced * 2 * _dstChannels + c * 2 + 1] = (byte)((s >> 8) & 0xFF);
            }
            produced++;
            _pos += step;
        }
        if (_pos >= srcFrames) _pos -= srcFrames;
        else _pos -= srcFrames - 1 > 0 ? Math.Max(0, srcFrames - 1) : 0;

        int bytes = produced * 2 * _dstChannels;
        if (destination.Length < bytes) destination = new byte[Math.Max(bytes, 8192)];
        Buffer.BlockCopy(_outBytes, 0, destination, 0, bytes);
        return bytes;
    }
}

/// <summary>WASAPI 采集（系统回环或麦克风），统一输出 16bit/48kHz/立体声。</summary>
[SupportedOSPlatform("windows")]
internal sealed class WasapiAudioCapture : IDisposable
{
    private readonly AudioKind _kind;
    private readonly string? _deviceId;
    private readonly float _volume;
    private readonly int _targetRate;
    private readonly int _targetChannels;

    private IMMDeviceEnumerator? _enumerator;
    private IMMDevice? _device;
    private IAudioClient? _client;
    private IAudioCaptureClient? _capture;
    private IntPtr _mixFormatPtr = IntPtr.Zero;
    private AudioFormatConverter? _converter;
    private Thread? _thread;
    private EventWaitHandle? _signal;
    private volatile bool _running;
    private long _framesWritten;
    private long _pendingBytes;
    private byte[] _pending = new byte[1 << 16];
    private byte[] _convertBuffer = new byte[1 << 16];

    /// <summary>输出回调：PCM16 交错数据、字节数、采样时间（hns）。</summary>
    public Action<byte[], int, long, long>? DataAvailable { get; set; }

    public string DeviceName { get; private set; } = "";
    public int SampleRate => _targetRate;
    public int Channels => _targetChannels;
    public Exception? LastError { get; private set; }
    public bool IsRunning => _running;

    /// <summary>本路音频的绝对起点（hns），用于多路混音的相对对齐。</summary>
    public long StartOffsetHns { get; set; }

    public WasapiAudioCapture(AudioKind kind, string? deviceId, float volume, int targetRate = 48000, int targetChannels = 2)
    {
        _kind = kind;
        _deviceId = deviceId;
        _volume = volume;
        _targetRate = targetRate;
        _targetChannels = targetChannels;
    }

    public static IMMDeviceEnumerator CreateEnumerator()
    {
        var type = Type.GetTypeFromCLSID(WasapiGuids.CLSID_MMDeviceEnumerator, throwOnError: true)!;
        return (IMMDeviceEnumerator)Activator.CreateInstance(type)!;
    }

    public static System.Collections.Generic.List<AudioDeviceInfo> EnumerateDevices(bool microphones)
    {
        var list = new System.Collections.Generic.List<AudioDeviceInfo>();
        IMMDeviceEnumerator? en = null;
        try
        {
            en = CreateEnumerator();
            int flow = microphones ? WasapiConst.eCapture : WasapiConst.eRender;
            int hr = en.EnumAudioEndpoints(flow, WasapiConst.DEVICE_STATE_ACTIVE, out IntPtr collectionPtr);
            if (hr < 0 || collectionPtr == IntPtr.Zero) return list;

            // IMMDeviceCollection 的 vtable: GetCount(3), Item(4)
            var vtbl = Marshal.ReadIntPtr(collectionPtr);
            var getCount = Marshal.GetDelegateForFunctionPointer<CollectionGetCount>(Marshal.ReadIntPtr(vtbl, IntPtr.Size * 3));
            var getItem = Marshal.GetDelegateForFunctionPointer<CollectionGetItem>(Marshal.ReadIntPtr(vtbl, IntPtr.Size * 4));
            try
            {
                getCount(collectionPtr, out uint count);
                en.GetDefaultAudioEndpoint(flow, 0, out IMMDevice? def);
                string defaultId = "";
                try { def?.GetId(out defaultId); } catch { }
                if (def != null) Marshal.ReleaseComObject(def);

                for (uint i = 0; i < count; i++)
                {
                    if (getItem(collectionPtr, i, out IntPtr devPtr) < 0 || devPtr == IntPtr.Zero) continue;
                    try
                    {
                        var dev = (IMMDevice)Marshal.GetObjectForIUnknown(devPtr);
                        try
                        {
                            dev.GetId(out string id);
                            list.Add(new AudioDeviceInfo
                            {
                                Id = id,
                                Name = GetFriendlyName(dev) is { Length: > 0 } n ? n : id,
                                IsDefault = string.Equals(id, defaultId, StringComparison.OrdinalIgnoreCase),
                            });
                        }
                        finally { Marshal.ReleaseComObject(dev); }
                    }
                    finally { Marshal.Release(devPtr); }
                }
            }
            finally { Marshal.Release(collectionPtr); }
        }
        catch (Exception ex)
        {
            Shell.Log.Error("枚举音频设备失败", ex);
        }
        finally
        {
            if (en != null) Marshal.ReleaseComObject(en);
        }
        return list;
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CollectionGetCount(IntPtr self, out uint count);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CollectionGetItem(IntPtr self, uint index, out IntPtr device);

    private static string GetFriendlyName(IMMDevice device)
    {
        IPropertyStore? store = null;
        try
        {
            if (device.OpenPropertyStore(0 /*STGM_READ*/, out store) < 0 || store == null) return "";
            var key = new PROPERTYKEY { fmtid = WasapiGuids.PKEY_Device_FriendlyName, pid = 14 };
            if (store.GetValue(ref key, out PROPVARIANT value) < 0) return "";
            try
            {
                if (value.vt == PROPVARIANT.VT_LPWSTR && value.p != IntPtr.Zero)
                    return Marshal.PtrToStringUni(value.p) ?? "";
                return "";
            }
            finally { value.Clear(); }
        }
        catch { return ""; }
        finally { if (store != null) Marshal.ReleaseComObject(store); }
    }

    public void Start()
    {
        if (_running) return;
        LastError = null;

        _enumerator = CreateEnumerator();
        int flow = _kind == AudioKind.Microphone ? WasapiConst.eCapture : WasapiConst.eRender;
        int hr;
        if (!string.IsNullOrEmpty(_deviceId))
        {
            hr = _enumerator.GetDevice(_deviceId, out IMMDevice? dev);
            _device = hr < 0 ? null : dev;
        }
        else
        {
            hr = _enumerator.GetDefaultAudioEndpoint(flow, 0 /*eConsole*/, out IMMDevice? dev);
            _device = hr < 0 ? null : dev;
        }
        if (_device == null)
            throw new InvalidOperationException(_kind == AudioKind.Microphone ? "找不到可用的麦克风设备" : "找不到可用的扬声器设备");

        _device.GetId(out string id);
        DeviceName = GetFriendlyName(_device);
        if (string.IsNullOrEmpty(DeviceName)) DeviceName = id;

        Guid iidAudioClient = WasapiGuids.IID_IAudioClient;
        hr = _device.Activate(ref iidAudioClient, 0x17 /*CLSCTX_ALL*/, IntPtr.Zero, out object clientObj);
        if (hr < 0) throw new InvalidOperationException($"激活 IAudioClient 失败 0x{hr:X8}");
        _client = (IAudioClient)clientObj;

        hr = _client.GetMixFormat(out _mixFormatPtr);
        if (hr < 0 || _mixFormatPtr == IntPtr.Zero)
            throw new InvalidOperationException($"GetMixFormat 失败 0x{hr:X8}");

        var fmt = Marshal.PtrToStructure<WAVEFORMATEX>(_mixFormatPtr);
        int srcRate = (int)fmt.nSamplesPerSec;
        int srcChannels = fmt.nChannels;
        bool isFloat = IsFloatFormat(fmt, _mixFormatPtr);

        _converter = new AudioFormatConverter(srcRate, srcChannels, isFloat, _targetRate, _targetChannels);

        int flags = WasapiConst.AUDCLNT_STREAMFLAGS_EVENTCALLBACK;
        if (_kind == AudioKind.SystemLoopback) flags |= WasapiConst.AUDCLNT_STREAMFLAGS_LOOPBACK;

        _signal = new EventWaitHandle(false, EventResetMode.AutoReset);
        hr = _client.Initialize(WasapiConst.AUDCLNT_SHAREMODE_SHARED, flags, 2_000_000 /*200ms*/, 0, _mixFormatPtr, IntPtr.Zero);
        if (hr < 0) throw new InvalidOperationException($"IAudioClient.Initialize 失败 0x{hr:X8}");

        hr = _client.SetEventHandle(_signal.SafeWaitHandle.DangerousGetHandle());
        if (hr < 0) throw new InvalidOperationException($"SetEventHandle 失败 0x{hr:X8}");

        Guid iidCapture = WasapiGuids.IID_IAudioCaptureClient;
        hr = _client.GetService(ref iidCapture, out object captureObj);
        if (hr < 0) throw new InvalidOperationException($"GetService(IAudioCaptureClient) 失败 0x{hr:X8}");
        _capture = (IAudioCaptureClient)captureObj;

        hr = _client.Start();
        if (hr < 0) throw new InvalidOperationException($"IAudioClient.Start 失败 0x{hr:X8}");

        _running = true;
        _thread = new Thread(CaptureLoop)
        {
            IsBackground = true,
            Name = $"WasapiCapture-{_kind}",
            Priority = ThreadPriority.AboveNormal,
        };
        _thread.Start();
        Shell.Log.Info($"音频采集已启动 [{_kind}] {DeviceName} ({srcRate}Hz/{srcChannels}ch/{(isFloat ? "float" : "pcm")}) → {_targetRate}Hz/{_targetChannels}ch");
    }

    private static bool IsFloatFormat(WAVEFORMATEX fmt, IntPtr ptr)
    {
        if (fmt.wFormatTag == WasapiConst.WAVE_FORMAT_IEEE_FLOAT) return true;
        if (fmt.wFormatTag == WasapiConst.WAVE_FORMAT_PCM) return false;
        if (fmt.wFormatTag == WasapiConst.WAVE_FORMAT_EXTENSIBLE && fmt.cbSize >= 22)
        {
            var ext = Marshal.PtrToStructure<WAVEFORMATEXTENSIBLE>(ptr);
            if (ext.SubFormat == WasapiGuids.KSDATAFORMAT_SUBTYPE_IEEE_FLOAT) return true;
            if (ext.SubFormat == WasapiGuids.KSDATAFORMAT_SUBTYPE_PCM) return false;
        }
        return fmt.wBitsPerSample == 32;
    }

    private void CaptureLoop()
    {
        try
        {
            while (_running)
            {
                if (_signal == null || !_signal.WaitOne(200)) continue;
                if (!_running) break;
                DrainPackets();
            }
        }
        catch (Exception ex)
        {
            LastError = ex;
            Shell.Log.Error($"音频采集循环异常 [{_kind}]", ex);
        }
    }

    private void DrainPackets()
    {
        if (_capture == null || _client == null || _converter == null) return;
        while (_running)
        {
            int hr = _capture.GetNextPacketSize(out uint packetFrames);
            if (hr < 0 || packetFrames == 0) break;

            hr = _capture.GetBuffer(out IntPtr data, out uint frames, out uint flags, out _, out _);
            if (hr < 0) break;

            bool silent = (flags & 0x2 /*AUDCLNT_BUFFERFLAGS_SILENT*/) != 0;

            if (frames > 0)
            {
                var fmt = Marshal.PtrToStructure<WAVEFORMATEX>(_mixFormatPtr);
                int bytesPerFrame = fmt.nBlockAlign;
                int byteCount = (int)frames * bytesPerFrame;

                byte[] source = new byte[byteCount];
                if (!silent && data != IntPtr.Zero) Marshal.Copy(data, source, 0, byteCount);

                if (_volume != 1.0f) ApplyVolume(source, byteCount, fmt);

                int produced = _converter.Convert(source, byteCount, ref _convertBuffer);
                if (produced > 0)
                {
                    Emit(_convertBuffer, produced);
                }
            }
            _capture.ReleaseBuffer(frames);
        }
    }

    private void ApplyVolume(byte[] data, int count, WAVEFORMATEX fmt)
    {
        if (fmt.wFormatTag == WasapiConst.WAVE_FORMAT_IEEE_FLOAT || fmt.wBitsPerSample == 32)
        {
            for (int i = 0; i + 4 <= count; i += 4)
            {
                float v = BitConverter.ToSingle(data, i) * _volume;
                Buffer.BlockCopy(BitConverter.GetBytes(v), 0, data, i, 4);
            }
        }
        else
        {
            for (int i = 0; i + 2 <= count; i += 2)
            {
                short s = (short)(BitConverter.ToInt16(data, i) * _volume);
                data[i] = (byte)(s & 0xFF);
                data[i + 1] = (byte)((s >> 8) & 0xFF);
            }
        }
    }

    private void Emit(byte[] data, int count)
    {
        long frames = count / (2 * _targetChannels);
        long hns = _framesWritten * 10_000_000L / _targetRate + StartOffsetHns;
        DataAvailable?.Invoke(data, count, hns, frames * 10_000_000L / _targetRate);
        _framesWritten += frames;
    }

    public void Stop()
    {
        if (!_running) return;
        _running = false;
        try { _signal?.Set(); } catch { }
        try { _thread?.Join(1500); } catch { }
        try { _client?.Stop(); } catch { }
    }

    public void Dispose()
    {
        Stop();
        try { if (_capture != null) Marshal.ReleaseComObject(_capture); } catch { }
        try { if (_client != null) Marshal.ReleaseComObject(_client); } catch { }
        try { if (_device != null) Marshal.ReleaseComObject(_device); } catch { }
        try { if (_enumerator != null) Marshal.ReleaseComObject(_enumerator); } catch { }
        if (_mixFormatPtr != IntPtr.Zero) { Marshal.FreeCoTaskMem(_mixFormatPtr); _mixFormatPtr = IntPtr.Zero; }
        _signal?.Dispose();
        _capture = null;
        _client = null;
        _device = null;
        _enumerator = null;
    }
}
