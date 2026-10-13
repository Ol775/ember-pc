using Ember.Lighting;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Ember.App;

/// <summary>
/// Listens to what the PC is playing (the default output, "loopback") and turns it into <see cref="AudioLevels"/> for the music
/// effects. It never records a microphone, keeps no audio, and runs only while a music layer is on.
/// </summary>
public sealed class AudioCapture(Action<AudioLevels?> publish) : IDisposable
{
    WasapiLoopbackCapture? _capture;
    AudioAnalyzer? _analyzer;
    readonly float[] _ring = new float[AudioAnalyzer.FftSize * 2];
    int _filled;
    DateTime _lastData = DateTime.MinValue;

    public bool Running => _capture is not null;

    public void Start()
    {
        if (_capture is not null) return;
        try
        {
            var capture = new WasapiLoopbackCapture();
            _analyzer = new AudioAnalyzer(capture.WaveFormat.SampleRate);
            capture.DataAvailable += OnData;
            capture.RecordingStopped += (_, _) => { };
            capture.StartRecording();
            _capture = capture;
            _lastData = DateTime.UtcNow;
        }
        catch (Exception)
        {
            _capture?.Dispose();
            _capture = null; // no output device, or it is busy: the music effects stay at their quiet colour
        }
    }

    public void Stop()
    {
        var c = _capture;
        _capture = null;
        if (c is null) return;
        try { c.DataAvailable -= OnData; c.StopRecording(); } catch (Exception) { /* already stopped */ }
        c.Dispose();
        publish(null);
        _filled = 0;
    }

    /// <summary>Windows sends nothing while nothing plays; call this now and then so the effects fall quiet.</summary>
    public void CheckSilence()
    {
        if (_capture is not null && DateTime.UtcNow - _lastData > TimeSpan.FromMilliseconds(400)) publish(AudioLevels.Silent);
    }

    void OnData(object? sender, WaveInEventArgs e)
    {
        try
        {
            var capture = _capture;
            var analyzer = _analyzer;
            if (capture is null || analyzer is null || e.BytesRecorded == 0) return;
            var format = capture.WaveFormat;
            var channels = Math.Max(1, format.Channels);
            var bytesPerSample = format.BitsPerSample / 8;
            var frames = e.BytesRecorded / (bytesPerSample * channels);
            var isFloat = format.BitsPerSample == 32 && format.Encoding != WaveFormatEncoding.Pcm;
            for (var f = 0; f < frames; f++)
            {
                var sum = 0f;
                for (var c = 0; c < channels; c++)
                {
                    var o = (f * channels + c) * bytesPerSample;
                    sum += isFloat ? BitConverter.ToSingle(e.Buffer, o)
                        : bytesPerSample == 2 ? BitConverter.ToInt16(e.Buffer, o) / 32768f
                        : 0f;
                }
                Push(sum / channels);
            }
            _lastData = DateTime.UtcNow;
            var dt = Math.Clamp(frames / (double)format.SampleRate, 0.005, 0.2);
            publish(analyzer.Analyze(_ring.AsSpan(0, _filled), dt));
        }
        catch (Exception) { /* a bad buffer must never take the app down */ }
    }

    void Push(float sample)
    {
        if (_filled == _ring.Length)
        {
            Array.Copy(_ring, _ring.Length / 2, _ring, 0, _ring.Length / 2); // keep the newest half
            _filled = _ring.Length / 2;
        }
        _ring[_filled++] = sample;
    }

    public void Dispose() => Stop();
}
