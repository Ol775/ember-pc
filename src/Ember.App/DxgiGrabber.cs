using Ember.Lighting;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace Ember.App;

/// <summary>
/// Reads the main screen through DXGI desktop duplication: the graphics card hands over the finished frame, so it is much
/// cheaper and faster than copying it through GDI, and it works with most games in borderless and many in full screen.
/// Only the 64 x 36 sample points Ember needs are read from the frame. Returns null whenever it cannot (a locked screen, a
/// mode change, an HDR desktop): the caller then falls back to the GDI grabber, and this object starts over next time.
/// </summary>
public sealed class DxgiGrabber : IDisposable
{
    const int Width = 64, Height = 36;
    const float HdrWhite = 2.5f;

    ID3D11Device? _device;
    ID3D11DeviceContext? _context;
    IDXGIOutputDuplication? _duplication;
    ID3D11Texture2D? _staging;
    int _stagingW, _stagingH;
    Color[]? _last;
    DateTime _nextTry = DateTime.MinValue;
    string? _monitor;

    public bool Active => _duplication is not null;

    /// <summary>Why DXGI could not be used the last time (for the diagnostics line), or null.</summary>
    public string? LastError { get; private set; }
    public Format Format { get; private set; }

    /// <summary>The screen as zone colours (the previous picture when nothing on screen changed), or null if DXGI cannot be used right now.</summary>
    public Color[]? Capture(int zones = ScreenZones.DefaultZones, string? monitor = null)
    {
        try
        {
            if (!string.Equals(monitor, _monitor, StringComparison.OrdinalIgnoreCase)) { Close(); _monitor = monitor; _nextTry = DateTime.MinValue; }
            if (_duplication is null)
            {
                if (DateTime.UtcNow < _nextTry) return null;
                if (!Open()) { LastError ??= "no screen at the origin"; _nextTry = DateTime.UtcNow.AddSeconds(5); return null; }
            }

            var dup = _duplication!;
            var result = dup.AcquireNextFrame(0, out var info, out var resource);
            if (result.Failure)
            {
                if (result == Vortice.DXGI.ResultCode.WaitTimeout) return _last; // nothing changed since the last frame
                Close(); // access lost: a mode change, a locked screen, another full-screen app
                _nextTry = DateTime.UtcNow.AddSeconds(2);
                return null;
            }
            try
            {
                // A frame that carries no picture update (only the mouse pointer or metadata changed) can have an empty texture.
                // Read it and the lights go black, which an HDR desktop does all the time, so keep the last real picture.
                if (info.AccumulatedFrames == 0 && info.LastPresentTime == 0) return _last;
                using var frame = resource.QueryInterface<ID3D11Texture2D>();
                var desc = frame.Description;
                if (desc.Format is not (Format.B8G8R8A8_UNorm or Format.R10G10B10A2_UNorm or Format.R16G16B16A16_Float))
                {
                    LastError = "unsupported desktop format " + desc.Format;
                    Close(); _nextTry = DateTime.UtcNow.AddMinutes(1); return null; // use GDI
                }
                Format = desc.Format;
                EnsureStaging(desc);
                _context!.CopyResource(_staging!, frame);
                var map = _context.Map(_staging!, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
                try
                {
                    var pixels = new byte[Width * Height * 4];
                    unsafe
                    {
                        var basePtr = (byte*)map.DataPointer;
                        for (var ty = 0; ty < Height; ty++)
                        {
                            var sy = Math.Min(desc.Height - 1, (int)((ty + 0.5) * desc.Height / Height));
                            var row = basePtr + (long)sy * map.RowPitch;
                            for (var tx = 0; tx < Width; tx++)
                            {
                                var sx = Math.Min(desc.Width - 1, (int)((tx + 0.5) * desc.Width / Width));
                                var o = (ty * Width + tx) * 4;
                                switch (desc.Format)
                                {
                                    case Format.R10G10B10A2_UNorm:
                                    {
                                        var v = *(uint*)(row + sx * 4);
                                        pixels[o] = (byte)(((v >> 20) & 0x3FF) >> 2); pixels[o + 1] = (byte)(((v >> 10) & 0x3FF) >> 2); pixels[o + 2] = (byte)((v & 0x3FF) >> 2);
                                        break;
                                    }
                                    case Format.R16G16B16A16_Float:
                                    {
                                        // HDR desktop (scRGB, 1.0 = 80 nits): Windows shows SDR white at about 2.5 times that by default, so treat that as full brightness.
                                        var h = (Half*)(row + sx * 8);
                                        pixels[o] = ToByte((float)h[2] / HdrWhite); pixels[o + 1] = ToByte((float)h[1] / HdrWhite); pixels[o + 2] = ToByte((float)h[0] / HdrWhite);
                                        break;
                                    }
                                    default:
                                    {
                                        var p = row + sx * 4;
                                        pixels[o] = p[0]; pixels[o + 1] = p[1]; pixels[o + 2] = p[2];
                                        break;
                                    }
                                }
                                pixels[o + 3] = 255;
                            }
                        }
                    }
                    _last = ScreenZones.FromBgra(pixels, Width, Height, zones);
                }
                finally { _context.Unmap(_staging!, 0); }
            }
            finally
            {
                resource.Dispose();
                dup.ReleaseFrame();
            }
            return _last;
        }
        catch (Exception ex)
        {
            LastError = ex.ToString();
            Close();
            _nextTry = DateTime.UtcNow.AddSeconds(5);
            return null;
        }
    }

    /// <summary>Linear light to the 8-bit value the screen would show (sRGB curve).</summary>
    static byte ToByte(float linear)
    {
        var c = Math.Clamp(linear, 0f, 1f);
        var srgb = c <= 0.0031308f ? 12.92f * c : 1.055f * MathF.Pow(c, 1 / 2.4f) - 0.055f;
        return (byte)Math.Round(srgb * 255f);
    }

    bool Open()
    {
        using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
        for (uint a = 0; factory.EnumAdapters1(a, out var adapter).Success; a++)
        {
            using (adapter)
            {
                for (uint o = 0; adapter.EnumOutputs(o, out var output).Success; o++)
                {
                    using (output)
                    {
                        var bounds = output.Description.DesktopCoordinates;
                        var wanted = _monitor is null ? bounds.Left == 0 && bounds.Top == 0 : string.Equals(output.Description.DeviceName, _monitor, StringComparison.OrdinalIgnoreCase); // the main screen sits at the origin
                        if (!wanted) continue;
                        D3D11.D3D11CreateDevice(adapter, DriverType.Unknown, DeviceCreationFlags.BgraSupport, Array.Empty<FeatureLevel>(), out _device, out _context).CheckError();
                        using var output1 = output.QueryInterface<IDXGIOutput1>();
                        _duplication = output1.DuplicateOutput(_device);
                        return true;
                    }
                }
            }
        }
        return false;
    }

    void EnsureStaging(Texture2DDescription source)
    {
        if (_staging is not null && _stagingW == source.Width && _stagingH == source.Height) return;
        _staging?.Dispose();
        _staging = _device!.CreateTexture2D(new Texture2DDescription
        {
            Width = source.Width, Height = source.Height, MipLevels = 1, ArraySize = 1, Format = source.Format,
            SampleDescription = new SampleDescription(1, 0), Usage = ResourceUsage.Staging, BindFlags = BindFlags.None,
            CPUAccessFlags = CpuAccessFlags.Read,
        });
        (_stagingW, _stagingH) = ((int)source.Width, (int)source.Height);
    }

    void Close()
    {
        _duplication?.Dispose(); _duplication = null;
        _staging?.Dispose(); _staging = null;
        _context?.Dispose(); _context = null;
        _device?.Dispose(); _device = null;
        _last = null;
    }

    public void Dispose() => Close();
}

/// <summary>One screen attached to the PC: its Windows name (for example \.\DISPLAY2), size and whether it is the main one.</summary>
public sealed record ScreenInfo(string Name, int Width, int Height, bool Main)
{
    public string Label() => $"{(Main ? "Main screen" : "Screen " + new string(Name.Where(char.IsDigit).ToArray()))} ({Width} x {Height})";

    /// <summary>Every screen DXGI can see, the main one first, then left to right.</summary>
    public static IReadOnlyList<ScreenInfo> All()
    {
        var list = new List<ScreenInfo>();
        try
        {
            using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
            for (uint a = 0; factory.EnumAdapters1(a, out var adapter).Success; a++)
                using (adapter)
                    for (uint o = 0; adapter.EnumOutputs(o, out var output).Success; o++)
                        using (output)
                        {
                            var d = output.Description;
                            var b = d.DesktopCoordinates;
                            if (!d.AttachedToDesktop) continue;
                            list.Add(new ScreenInfo(d.DeviceName, b.Right - b.Left, b.Bottom - b.Top, b.Left == 0 && b.Top == 0) { X = b.Left, Y = b.Top });
                        }
        }
        catch (Exception) { /* no list: the picker only offers the main screen */ }
        return list.OrderByDescending(s => s.Main).ThenBy(s => s.X).ToList();
    }

    public int X { get; init; }
    public int Y { get; init; }
}
