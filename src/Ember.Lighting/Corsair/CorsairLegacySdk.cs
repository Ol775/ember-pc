using System.Runtime.InteropServices;
using System.Text;

namespace Ember.Lighting.Corsair;

/// <summary>
/// Corsair's older "CUE SDK" (version 3). Some iCUE installs let programs in through this older interface when they refuse the
/// newer iCUE SDK. It has no session: every call talks to iCUE straight away, so "connecting" is one handshake that either works
/// or says iCUE is not running or has third-party control switched off.
/// </summary>
public sealed class NativeCorsairLegacyApi : ICorsairApi
{
    // x64 layouts of the SDK's structs. CorsairDeviceInfo: type, model pointer, layouts, caps, LED count, channels, 128-byte id.
    const int DeviceInfoModel = 8, DeviceInfoLeds = 28, DeviceInfoId = 48, IdSize = 128;
    const int LedPositionSize = 40, LedColorSize = 16;

    [StructLayout(LayoutKind.Sequential)]
    struct ProtocolDetails { public nint SdkVersion, ServerVersion; public int SdkProtocol, ServerProtocol; public byte BreakingChanges; }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate ProtocolDetails HandshakeFn();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int LastErrorFn();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int DeviceCountFn();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate nint DeviceInfoFn(int index);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate nint PositionsFn(int index);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] delegate bool BufferFn(int index, int size, nint colors);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] delegate bool FlushFn();

    readonly nint _lib;
    readonly HandshakeFn _handshake;
    readonly LastErrorFn _lastError;
    readonly DeviceCountFn _count;
    readonly DeviceInfoFn _info;
    readonly PositionsFn _positions;
    readonly BufferFn _buffer;
    readonly FlushFn _flush;
    readonly object _gate = new();
    readonly Dictionary<string, int> _index = [];

    public string Detail { get; private set; } = "";

    NativeCorsairLegacyApi(nint lib)
    {
        _lib = lib;
        _handshake = Get<HandshakeFn>("CorsairPerformProtocolHandshake");
        _lastError = Get<LastErrorFn>("CorsairGetLastError");
        _count = Get<DeviceCountFn>("CorsairGetDeviceCount");
        _info = Get<DeviceInfoFn>("CorsairGetDeviceInfo");
        _positions = Get<PositionsFn>("CorsairGetLedPositionsByDeviceIndex");
        _buffer = Get<BufferFn>("CorsairSetLedsColorsBufferByDeviceIndex");
        _flush = Get<FlushFn>("CorsairSetLedsColorsFlushBuffer");
    }

    /// <summary>Null when the library is missing or cannot be loaded.</summary>
    public static NativeCorsairLegacyApi? TryLoad(string? path = null)
    {
        path ??= CorsairLocator.FindLegacy();
        if (path is null) return null;
        nint lib = 0;
        try { lib = NativeLibrary.Load(path); return new NativeCorsairLegacyApi(lib); }
        catch (Exception)
        {
            if (lib != 0) NativeLibrary.Free(lib);
            return null;
        }
    }

    T Get<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(_lib, name));

    static string ErrorName(int e) => e switch
    {
        0 => "ok", 1 => "iCUE not found, or third-party control (Enable SDK) is off", 2 => "no control (another program has it)",
        3 => "handshake missing", 4 => "incompatible protocol", 5 => "invalid arguments", _ => "error " + e,
    };

    public CorsairState Connect()
    {
        lock (_gate)
        {
            var d = _handshake();
            var error = _lastError();
            var server = d.ServerVersion == 0 ? "not found" : Marshal.PtrToStringAnsi(d.ServerVersion);
            Detail = $"older SDK: {ErrorName(error)}, iCUE {server}, protocol {d.ServerProtocol}";
            Ember.Core.DiagLog.Changed("Corsair", "older SDK handshake", Detail);
            return error == 0 && d.ServerProtocol > 0 ? CorsairState.Connected : CorsairState.Unavailable;
        }
    }

    public IReadOnlyList<CorsairDevice> Devices()
    {
        lock (_gate)
        {
            var list = new List<CorsairDevice>();
            var n = _count();
            _index.Clear();
            for (var i = 0; i < n; i++)
            {
                var info = _info(i);
                if (info == 0) continue;
                var type = Marshal.ReadInt32(info);
                var model = Marshal.PtrToStringAnsi(Marshal.ReadIntPtr(info, DeviceInfoModel)) ?? "device";
                var id = ReadString(info + DeviceInfoId, IdSize);
                if (id.Length == 0) id = "index" + i;
                var positions = _positions(i);
                if (positions == 0) { Ember.Core.DiagLog.Changed("Corsair", "older SDK LEDs of " + model, "none listed"); continue; }
                var count = Marshal.ReadInt32(positions);
                var array = Marshal.ReadIntPtr(positions, 8);
                if (count <= 0 || count > 1024 || array == 0) continue;
                var leds = new uint[count];
                for (var k = 0; k < count; k++) leds[k] = (uint)Marshal.ReadInt32(array + k * LedPositionSize);
                _index[id] = i;
                list.Add(new CorsairDevice(id, model, ToV4Type(type), leds));
                Ember.Core.DiagLog.Changed("Corsair", "older SDK device " + i, $"{model}, {count} LEDs");
            }
            return list;
        }
    }

    public bool SetColors(string deviceId, uint[] ledIds, byte[] rgb)
    {
        lock (_gate)
        {
            if (!_index.TryGetValue(deviceId, out var index) || rgb.Length < ledIds.Length * 3) return false;
            var buf = Marshal.AllocHGlobal(LedColorSize * ledIds.Length);
            try
            {
                for (var i = 0; i < ledIds.Length; i++)
                {
                    var at = buf + i * LedColorSize;
                    Marshal.WriteInt32(at, (int)ledIds[i]);
                    Marshal.WriteInt32(at, 4, rgb[i * 3]);
                    Marshal.WriteInt32(at, 8, rgb[i * 3 + 1]);
                    Marshal.WriteInt32(at, 12, rgb[i * 3 + 2]);
                }
                var ok = _buffer(index, ledIds.Length, buf) && _flush();
                Ember.Core.DiagLog.Changed("Corsair", "older SDK SetLedsColors " + deviceId, ok ? "ok" : ErrorName(_lastError()));
                if (!ok) Detail = "older SDK: colours refused, " + ErrorName(_lastError());
                return ok;
            }
            finally { Marshal.FreeHGlobal(buf); }
        }
    }

    // The older SDK has no connection to close: iCUE gets the lighting back as soon as the program stops sending.
    public void Disconnect() { }

    public void Dispose() => NativeLibrary.Free(_lib);

    /// <summary>The older SDK numbers its device types differently from the newer one; the rest of Ember uses the newer numbers.</summary>
    static int ToV4Type(int old) => old switch
    {
        1 => 0x0002, 2 => 0x0001, 3 => 0x0008, 4 => 0x0004, 5 => 0x0010, 6 => 0x0020, 7 => 0x0040,
        8 => 0x0080, 9 => 0x0100, 10 => 0x0200, 11 => 0x0400, 12 => 0x0800, _ => 0,
    };

    static string ReadString(nint at, int max)
    {
        var bytes = new byte[max];
        Marshal.Copy(at, bytes, 0, max);
        var end = Array.IndexOf(bytes, (byte)0);
        return Encoding.UTF8.GetString(bytes, 0, end < 0 ? max : end);
    }
}

/// <summary>
/// Uses the newer iCUE SDK first and the older CUE SDK when the newer one cannot get in (iCUE refuses it, or does not answer).
/// Whichever connects is used for the devices; if it stops working, both are tried again from the start.
/// </summary>
public sealed class CorsairApiChain(ICorsairApi newer, ICorsairApi? older) : ICorsairApi
{
    ICorsairApi? _active;

    public string Detail { get; private set; } = "";

    public CorsairState Connect()
    {
        if (_active is { } a && a.Connect() == CorsairState.Connected) { Detail = a.Detail; return CorsairState.Connected; }
        _active = null;
        var first = newer.Connect();
        if (first == CorsairState.Connected) { _active = newer; Detail = newer.Detail; return first; }
        if (first == CorsairState.Connecting) { Detail = newer.Detail; return first; }   // give the newer SDK its moment before trying the older one
        if (older is not null && older.Connect() == CorsairState.Connected)
        {
            _active = older;
            Ember.Core.DiagLog.Changed("Corsair", "SDK in use", "the older CUE SDK (the newer iCUE SDK said: " + newer.Detail + ")");
            Detail = older.Detail;
            return CorsairState.Connected;
        }
        Detail = newer.Detail + (older is null ? "" : " | " + older.Detail);
        return first;
    }

    public IReadOnlyList<CorsairDevice> Devices() => _active?.Devices() ?? [];

    public bool SetColors(string deviceId, uint[] ledIds, byte[] rgb) => _active?.SetColors(deviceId, ledIds, rgb) ?? false;

    public void Disconnect()
    {
        _active = null;
        newer.Disconnect();
        older?.Disconnect();
    }

    public void Dispose() { newer.Dispose(); older?.Dispose(); }

    /// <summary>Loads whichever of the two libraries are shipped next to the app; null when neither can be loaded.</summary>
    public static ICorsairApi? Open()
    {
        var newer = NativeCorsairApi.TryLoad();
        var older = NativeCorsairLegacyApi.TryLoad();
        if (newer is null && older is null) return null;
        if (newer is null) return older;
        return new CorsairApiChain(newer, older);
    }
}
