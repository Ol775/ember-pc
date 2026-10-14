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
    /// <summary>What the SDK last said, in plain words (session state, iCUE version, error codes), for the status line and the log.</summary>
    string Detail => "";
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
    bool _started, _loggedWaiting;

    public string Detail { get; private set; } = "";

    static string StateName(int s) => s switch { 1 => "closed", 2 => "connecting", 3 => "timed out", 4 => "refused (is Enable SDK on in iCUE?)", 5 => "lost", 6 => "connected", _ => "unknown " + s };

    static string ErrorName(int e) => e switch { 0 => "ok", 1 => "not connected", 2 => "no control (another program has it)", 3 => "incompatible protocol", 4 => "invalid arguments", 5 => "invalid operation", 6 => "device not found", 7 => "not allowed", _ => "error " + e };

    NativeCorsairApi(nint lib)
    {
        _lib = lib;
        _connect = Get<ConnectFn>("CorsairConnect");
        _disconnect = Get<DisconnectFn>("CorsairDisconnect");
        _getDevices = Get<GetDevicesFn>("CorsairGetDevices");
        _getPositions = Get<GetPositionsFn>("CorsairGetLedPositions");
        _setColors = Get<SetColorsFn>("CorsairSetLedColors");
        _onState = (_, e) =>
        {
            _state = Marshal.ReadInt32(e);
            var host = $"{Marshal.ReadInt32(e, 28)}.{Marshal.ReadInt32(e, 32)}.{Marshal.ReadInt32(e, 36)}";
            var server = $"{Marshal.ReadInt32(e, 16)}.{Marshal.ReadInt32(e, 20)}.{Marshal.ReadInt32(e, 24)}";
            Detail = $"session {StateName(_state)}, iCUE {host}, SDK server {server}";
            // With iCUE not running the SDK cycles "connecting" and "timed out" every second: say that once, not forever.
            var waiting = _state is 2 or 3;
            if (!waiting || !_loggedWaiting) Ember.Core.DiagLog.Info("Corsair", "iCUE " + Detail);
            _loggedWaiting = waiting;
        };
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

    DateTime _startedAt;

    public CorsairState Connect()
    {
        lock (_gate)
        {
            // A connection the SDK says is refused or lost is started again, but not on every look: give it time first.
            if (_started && _state is 4 or 5 or 1 && DateTime.UtcNow - _startedAt > TimeSpan.FromSeconds(15))
            {
                try { _disconnect(); } catch (Exception) { /* starting over anyway */ }
                _started = false;
            }
            if (!_started)
            {
                _started = true;
                _startedAt = DateTime.UtcNow;
                _state = 2;
                var rc = _connect(Marshal.GetFunctionPointerForDelegate(_onState), 0);
                Ember.Core.DiagLog.Info("Corsair", "CorsairConnect: " + ErrorName(rc));
                if (rc != 0) { _state = 1; Detail = "CorsairConnect said " + ErrorName(rc); }
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
                var rc = _getDevices(filter, MaxDevices, buf, out var count);
                Ember.Core.DiagLog.Changed("Corsair", "CorsairGetDevices", ErrorName(rc) + ", " + count + " devices");
                if (rc != 0) { Detail = "GetDevices said " + ErrorName(rc); return list; }
                for (var i = 0; i < count; i++)
                {
                    var at = buf + i * DeviceInfoSize;
                    var type = Marshal.ReadInt32(at);
                    var id = ReadString(at + 4, IdSize);
                    var model = ReadString(at + 4 + IdSize * 2, IdSize);
                    if (!_idBytes.TryGetValue(id, out var idBytes)) _idBytes[id] = idBytes = IdBytes(at + 4);
                    var prc = _getPositions(idBytes, MaxLeds, pos, out var n);
                    Ember.Core.DiagLog.Changed("Corsair", "LED positions of " + model, ErrorName(prc) + ", " + n + " LEDs");
                    if (prc != 0 || n <= 0) continue;
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
                var src = _setColors(idBytes, ledIds.Length, buf);
                Ember.Core.DiagLog.Changed("Corsair", "SetLedColors " + deviceId, ErrorName(src));
                if (src != 0) Detail = "SetLedColors said " + ErrorName(src);
                return src == 0;
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
