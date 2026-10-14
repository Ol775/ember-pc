using System.Runtime.InteropServices;
using System.Text;

namespace Ember.Lighting.Corsair;

/// <summary>One Corsair device as iCUE lists it: its id, model and the ids of its LEDs in the order Ember paints them.</summary>
public sealed record CorsairDevice(string Id, string Model, int Type, uint[] LedIds);

public enum CorsairState { Closed, Connecting, Unavailable, Connected }

/// <summary>The few calls of Corsair's iCUE SDK that Ember uses.</summary>
public interface ICorsairApi : IDisposable
{
    /// <summary>Starts (once) the connection to iCUE and says where it stands now. Connecting finishes in the background.</summary>
    CorsairState Connect();
    IReadOnlyList<CorsairDevice> Devices();
    /// <summary>One colour per LED id, in order (red, green, blue bytes). False when iCUE refused (not connected, no control, device gone).</summary>
    bool SetColors(string deviceId, uint[] ledIds, byte[] rgb);
    /// <summary>Hands the lighting back to iCUE.</summary>
    void Disconnect();
}

/// <summary>Where the iCUE SDK file Ember ships sits: next to the app, in an "iCUE" folder.</summary>
public static class CorsairLocator
{
    public const string FileName = "iCUESDK.x64_2019.dll";

    public static string? Find(string? appFolder = null)
    {
        var folder = appFolder ?? AppContext.BaseDirectory;
        var path = Path.Combine(folder, "iCUE", FileName);
        return File.Exists(path) ? path : null;
    }
}

/// <summary>Loads Corsair's iCUE SDK library (shipped unmodified with Ember) and talks to the iCUE program on this PC through it.</summary>
public sealed class NativeCorsairApi : ICorsairApi
{
    // Sizes of the SDK's structs: CorsairDeviceInfo is an int plus three 128-byte strings plus two ints; CorsairLedPosition is
    // an id and two doubles (padded); CorsairLedColor is an id and four bytes.
    const int IdSize = 128, DeviceInfoSize = 4 + IdSize * 3 + 8, LedPositionSize = 24, LedColorSize = 8, MaxDevices = 64, MaxLeds = 512;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void StateHandler(nint context, nint stateChanged);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int ConnectFn(nint handler, nint context);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int DisconnectFn();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int GetDevicesFn(nint filter, int sizeMax, nint devices, out int size);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int GetPositionsFn([MarshalAs(UnmanagedType.LPArray)] byte[] id, int sizeMax, nint positions, out int size);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int SetColorsFn([MarshalAs(UnmanagedType.LPArray)] byte[] id, int size, nint colors);

    readonly nint _lib;
    readonly ConnectFn _connect;
    readonly DisconnectFn _disconnect;
    readonly GetDevicesFn _getDevices;
    readonly GetPositionsFn _getPositions;
    readonly SetColorsFn _setColors;
    readonly StateHandler _onState;
    readonly object _gate = new();
    readonly Dictionary<string, byte[]> _idBytes = [];
    volatile int _state; // the SDK's session state: 1 closed, 2 connecting, 3 timeout, 4 refused, 5 lost, 6 connected
    bool _started;

    NativeCorsairApi(nint lib)
    {
        _lib = lib;
        _connect = Get<ConnectFn>("CorsairConnect");
        _disconnect = Get<DisconnectFn>("CorsairDisconnect");
        _getDevices = Get<GetDevicesFn>("CorsairGetDevices");
        _getPositions = Get<GetPositionsFn>("CorsairGetLedPositions");
        _setColors = Get<SetColorsFn>("CorsairSetLedColors");
        _onState = (_, e) => _state = Marshal.ReadInt32(e);
    }

    /// <summary>Null when the SDK file is missing or cannot be loaded.</summary>
    public static NativeCorsairApi? TryLoad(string? path = null)
    {
        path ??= CorsairLocator.Find();
        if (path is null) return null;
        nint lib = 0;
        try { lib = NativeLibrary.Load(path); return new NativeCorsairApi(lib); }
        catch (Exception)
        {
            if (lib != 0) NativeLibrary.Free(lib);
            return null;
        }
    }

    T Get<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(_lib, name));

    public CorsairState Connect()
    {
        lock (_gate)
        {
            if (!_started)
            {
                _started = true;
                _state = 2;
                if (_connect(Marshal.GetFunctionPointerForDelegate(_onState), 0) != 0) _state = 1;
            }
            return _state switch { 6 => CorsairState.Connected, 2 => CorsairState.Connecting, 3 or 4 or 5 => CorsairState.Unavailable, _ => CorsairState.Closed };
        }
    }

    public IReadOnlyList<CorsairDevice> Devices()
    {
        lock (_gate)
        {
            var list = new List<CorsairDevice>();
            var buf = Marshal.AllocHGlobal(DeviceInfoSize * MaxDevices);
            var filter = Marshal.AllocHGlobal(4);
            var pos = Marshal.AllocHGlobal(LedPositionSize * MaxLeds);
            try
            {
                Marshal.WriteInt32(filter, -1); // every device type
                if (_getDevices(filter, MaxDevices, buf, out var count) != 0) return list;
                for (var i = 0; i < count; i++)
                {
                    var at = buf + i * DeviceInfoSize;
                    var type = Marshal.ReadInt32(at);
                    var id = ReadString(at + 4, IdSize);
                    var model = ReadString(at + 4 + IdSize * 2, IdSize);
                    if (!_idBytes.TryGetValue(id, out var idBytes)) _idBytes[id] = idBytes = IdBytes(at + 4);
                    if (_getPositions(idBytes, MaxLeds, pos, out var n) != 0 || n <= 0) continue;
                    var leds = new uint[n];
                    for (var k = 0; k < n; k++) leds[k] = (uint)Marshal.ReadInt32(pos + k * LedPositionSize);
                    list.Add(new CorsairDevice(id, model, type, leds));
                }
                return list;
            }
            finally { Marshal.FreeHGlobal(buf); Marshal.FreeHGlobal(filter); Marshal.FreeHGlobal(pos); }
        }
    }

    public bool SetColors(string deviceId, uint[] ledIds, byte[] rgb)
    {
        lock (_gate)
        {
            if (!_idBytes.TryGetValue(deviceId, out var idBytes) || rgb.Length < ledIds.Length * 3) return false;
            var buf = Marshal.AllocHGlobal(LedColorSize * ledIds.Length);
            try
            {
                for (var i = 0; i < ledIds.Length; i++)
                {
                    var at = buf + i * LedColorSize;
                    Marshal.WriteInt32(at, (int)ledIds[i]);
                    Marshal.WriteByte(at, 4, rgb[i * 3]);
                    Marshal.WriteByte(at, 5, rgb[i * 3 + 1]);
                    Marshal.WriteByte(at, 6, rgb[i * 3 + 2]);
                    Marshal.WriteByte(at, 7, 255);
                }
                return _setColors(idBytes, ledIds.Length, buf) == 0;
            }
            finally { Marshal.FreeHGlobal(buf); }
        }
    }

    public void Disconnect()
    {
        lock (_gate)
        {
            if (_started) { try { _disconnect(); } catch (Exception) { /* closing anyway */ } }
            _started = false;
            _state = 1;
        }
    }

    public void Dispose() { Disconnect(); NativeLibrary.Free(_lib); }

    static string ReadString(nint at, int max)
    {
        var bytes = new byte[max];
        Marshal.Copy(at, bytes, 0, max);
        var end = Array.IndexOf(bytes, (byte)0);
        return Encoding.UTF8.GetString(bytes, 0, end < 0 ? max : end);
    }

    static byte[] IdBytes(nint at)
    {
        var bytes = new byte[IdSize];
        Marshal.Copy(at, bytes, 0, IdSize);
        return bytes;
    }
}
