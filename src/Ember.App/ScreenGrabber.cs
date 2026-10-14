using System.Runtime.InteropServices;
using Ember.Lighting;

namespace Ember.App;

/// <summary>
/// Takes a very small picture of the main screen (Windows shrinks it for us) and turns it into zone colours. A game in true
/// exclusive full screen, or protected video, may show up black; a borderless window works.
/// </summary>
static class ScreenGrabber
{
    const int Width = 64, Height = 36;
    const uint SrcCopy = 0x00CC0020;
    const int Halftone = 4;

    [StructLayout(LayoutKind.Sequential)]
    struct BitmapInfoHeader
    {
        public int Size, Width, Height;
        public short Planes, BitCount;
        public int Compression, SizeImage, XPelsPerMeter, YPelsPerMeter, ClrUsed, ClrImportant;
    }

    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("user32.dll")] static extern int GetSystemMetrics(int index);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int w, int h);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] static extern int SetStretchBltMode(IntPtr dc, int mode);
    [DllImport("gdi32.dll")] static extern bool StretchBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, int sw, int sh, uint rop);
    [DllImport("gdi32.dll")] static extern int GetDIBits(IntPtr dc, IntPtr bitmap, uint start, uint lines, byte[] bits, ref BitmapInfoHeader info, uint usage);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);

    /// <summary>The screen as zone colours, or null if Windows would not give a picture.</summary>
    public static Ember.Lighting.Color[]? Capture(int zones = ScreenZones.DefaultZones, string? monitor = null)
    {
        var screen = GetDC(IntPtr.Zero);
        if (screen == IntPtr.Zero) return null;
        IntPtr mem = IntPtr.Zero, bmp = IntPtr.Zero, old = IntPtr.Zero;
        try
        {
            mem = CreateCompatibleDC(screen);
            bmp = CreateCompatibleBitmap(screen, Width, Height);
            if (mem == IntPtr.Zero || bmp == IntPtr.Zero) return null;
            old = SelectObject(mem, bmp);
            SetStretchBltMode(mem, Halftone);
            var (sx, sy, sw, sh) = (0, 0, GetSystemMetrics(0), GetSystemMetrics(1));
            if (monitor is not null && ScreenInfo.All().FirstOrDefault(s => string.Equals(s.Name, monitor, StringComparison.OrdinalIgnoreCase)) is { } picked)
                (sx, sy, sw, sh) = (picked.X, picked.Y, picked.Width, picked.Height);
            if (sw <= 0 || sh <= 0 || !StretchBlt(mem, 0, 0, Width, Height, screen, sx, sy, sw, sh, SrcCopy)) return null;

            var info = new BitmapInfoHeader { Size = Marshal.SizeOf<BitmapInfoHeader>(), Width = Width, Height = -Height, Planes = 1, BitCount = 32 };
            var pixels = new byte[Width * Height * 4];
            SelectObject(mem, old);
            old = IntPtr.Zero;
            if (GetDIBits(mem, bmp, 0, Height, pixels, ref info, 0) == 0) return null;
            return ScreenZones.FromBgra(pixels, Width, Height, zones);
        }
        finally
        {
            if (old != IntPtr.Zero) SelectObject(mem, old);
            if (bmp != IntPtr.Zero) DeleteObject(bmp);
            if (mem != IntPtr.Zero) DeleteDC(mem);
            ReleaseDC(IntPtr.Zero, screen);
        }
    }
}
