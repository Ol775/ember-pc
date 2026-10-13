namespace Ember.Lighting;

/// <summary>What the music is doing right now, each from 0 (silent) to 1 (as loud as it has recently been).</summary>
public sealed record AudioLevels(double Level, double Bass, double Mid, double Treble, IReadOnlyList<double> Bands)
{
    public static readonly AudioLevels Silent = new(0, 0, 0, 0, new double[AudioAnalyzer.BandCount]);
}

/// <summary>
/// Turns a stream of audio samples into <see cref="AudioLevels"/>: a Hann-windowed FFT, 16 bands from 40 Hz up, and an automatic
/// gain that follows the loudest recent sound, so a quiet song and a loud one both reach the top of the range.
/// Pure and free of hardware, so it is tested with generated tones.
/// </summary>
public sealed class AudioAnalyzer(int sampleRate)
{
    public const int FftSize = 1024, BandCount = 16;
    const double MinHz = 40, MaxHz = 16000;

    readonly float[] _window = Enumerable.Range(0, FftSize).Select(i => (float)(0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (FftSize - 1)))).ToArray();
    readonly double[] _peak = new double[BandCount + 1]; // per band, then overall
    readonly double[] _smoothed = new double[BandCount + 1];
    readonly double[] _re = new double[FftSize], _im = new double[FftSize];

    /// <summary>Analyses the last <see cref="FftSize"/> mono samples (fewer are padded with silence).</summary>
    public AudioLevels Analyze(ReadOnlySpan<float> mono, double dtSeconds = 0.03)
    {
        var n = Math.Min(mono.Length, FftSize);
        var offset = mono.Length - n;
        var rms = 0.0;
        for (var i = 0; i < FftSize; i++)
        {
            var s = i < n ? mono[offset + i] : 0f;
            rms += s * s;
            _re[i] = s * _window[i];
            _im[i] = 0;
        }
        rms = Math.Sqrt(rms / Math.Max(1, n));
        Fft(_re, _im);

        var raw = new double[BandCount + 1];
        var binHz = (double)sampleRate / FftSize;
        for (var b = 0; b < BandCount; b++)
        {
            var lo = MinHz * Math.Pow(MaxHz / MinHz, (double)b / BandCount);
            var hi = MinHz * Math.Pow(MaxHz / MinHz, (double)(b + 1) / BandCount);
            var first = Math.Max(1, (int)Math.Floor(lo / binHz));
            var last = Math.Min(FftSize / 2 - 1, Math.Max(first, (int)Math.Ceiling(hi / binHz) - 1));
            var peak = 0.0;
            for (var k = first; k <= last; k++) peak = Math.Max(peak, Math.Sqrt(_re[k] * _re[k] + _im[k] * _im[k]));
            raw[b] = peak;
        }
        raw[BandCount] = rms;

        // One gain for the whole spectrum, so the shape of the sound is kept: it follows the loudest recent value, falling
        // slowly (about 8 s to halve) and never below a floor, so silence stays silent.
        var decay = Math.Pow(0.5, dtSeconds / 8);
        var loudest = raw[..BandCount].Max();
        _peak[0] = Math.Max(Math.Max(loudest, _peak[0] * decay), 0.5);
        _peak[BandCount] = Math.Max(Math.Max(raw[BandCount], _peak[BandCount] * decay), 0.01);

        var bands = new double[BandCount];
        for (var b = 0; b <= BandCount; b++)
        {
            var target = Math.Clamp(raw[b] / (b == BandCount ? _peak[BandCount] : _peak[0]), 0, 1);
            // Fast attack, slower release, so beats pop and then fade.
            var k = target > _smoothed[b] ? 1 - Math.Pow(0.5, dtSeconds / 0.03) : 1 - Math.Pow(0.5, dtSeconds / 0.15);
            _smoothed[b] += (target - _smoothed[b]) * k;
            if (b < BandCount) bands[b] = _smoothed[b];
        }
        var bass = Peak(bands, 0, 4);      // about 40 to 200 Hz
        var mid = Peak(bands, 4, 10);      // about 200 Hz to 3.5 kHz
        var treble = Peak(bands, 10, 16);  // above that
        return new AudioLevels(_smoothed[BandCount], bass, mid, treble, bands);
    }
    static double Peak(double[] v, int from, int to)
    {
        var m = 0.0;
        for (var i = from; i < to; i++) m = Math.Max(m, v[i]);
        return m;
    }

    /// <summary>In-place radix-2 FFT.</summary>
    static void Fft(double[] re, double[] im)
    {
        var n = re.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            var bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j) { (re[i], re[j]) = (re[j], re[i]); (im[i], im[j]) = (im[j], im[i]); }
        }
        for (var len = 2; len <= n; len <<= 1)
        {
            var ang = -2 * Math.PI / len;
            var (wr, wi) = (Math.Cos(ang), Math.Sin(ang));
            for (var i = 0; i < n; i += len)
            {
                double cr = 1, ci = 0;
                for (var k = 0; k < len / 2; k++)
                {
                    var (ur, ui) = (re[i + k], im[i + k]);
                    var (xr, xi) = (re[i + k + len / 2], im[i + k + len / 2]);
                    var (vr, vi) = (xr * cr - xi * ci, xr * ci + xi * cr);
                    re[i + k] = ur + vr; im[i + k] = ui + vi;
                    re[i + k + len / 2] = ur - vr; im[i + k + len / 2] = ui - vi;
                    (cr, ci) = (cr * wr - ci * wi, cr * wi + ci * wr);
                }
            }
        }
    }
}
