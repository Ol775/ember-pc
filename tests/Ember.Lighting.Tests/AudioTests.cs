using Ember.Lighting;
using Color = Ember.Lighting.Color;

namespace Ember.Lighting.Tests;

public class AudioTests
{
    const int Rate = 48000;

    static float[] Tone(double hz, double amplitude = 0.5, int samples = 4096) =>
        Enumerable.Range(0, samples).Select(i => (float)(amplitude * Math.Sin(2 * Math.PI * hz * i / Rate))).ToArray();

    static AudioLevels Settle(AudioAnalyzer a, float[] signal)
    {
        AudioLevels last = AudioLevels.Silent;
        for (var i = 0; i < 40; i++) last = a.Analyze(signal);
        return last;
    }

    [Fact]
    public void A_low_tone_is_bass_and_a_high_tone_is_treble()
    {
        var low = Settle(new AudioAnalyzer(Rate), Tone(100));
        Assert.True(low.Bass > 0.5 && low.Bass > low.Treble * 3, $"bass {low.Bass}, treble {low.Treble}");
        var high = Settle(new AudioAnalyzer(Rate), Tone(6000));
        Assert.True(high.Treble > 0.1 && high.Treble > high.Bass * 3, $"bass {high.Bass}, treble {high.Treble}");
    }

    [Fact]
    public void The_tone_lights_the_band_it_sits_in()
    {
        var levels = Settle(new AudioAnalyzer(Rate), Tone(1000));
        var loudest = levels.Bands.ToList().IndexOf(levels.Bands.Max());
        // 1 kHz is about band 7 of 16 on a 40 Hz to 16 kHz scale.
        Assert.InRange(loudest, 6, 9);
    }

    [Fact]
    public void Silence_stays_silent_and_noise_below_the_floor_does_not_flicker()
    {
        var a = new AudioAnalyzer(Rate);
        var silent = Settle(a, new float[4096]);
        Assert.Equal(0, silent.Level);
        Assert.All(silent.Bands, b => Assert.Equal(0, b));
        var hiss = Settle(new AudioAnalyzer(Rate), Tone(2000, 0.00001));
        Assert.True(hiss.Level < 0.5);
    }

    [Fact]
    public void A_quiet_song_and_a_loud_one_reach_the_same_height_once_the_gain_has_settled()
    {
        var quiet = Settle(new AudioAnalyzer(Rate), Tone(200, 0.05));
        var loud = Settle(new AudioAnalyzer(Rate), Tone(200, 0.8));
        Assert.True(Math.Abs(quiet.Bass - loud.Bass) < 0.1, $"{quiet.Bass} vs {loud.Bass}");
    }

    [Fact]
    public void Short_input_is_padded_not_rejected() =>
        Assert.True(new AudioAnalyzer(Rate).Analyze(Tone(100, 0.5, 100)).Level >= 0);

    static readonly Color Dark = Color.Black, Bright = new(1, 1, 1);

    [Fact]
    public void The_pulse_follows_the_chosen_band_and_shows_the_quiet_colour_without_sound()
    {
        var pulse = new AudioPulseEffect(Dark, Bright, AudioBand.Bass);
        Assert.Equal(Dark, pulse.Sample(0, 0, new LightingInputs(0)));
        var loudBass = new AudioLevels(0.5, 1, 0, 0, new double[16]);
        Assert.Equal(1, pulse.Sample(0, 0, new LightingInputs(0, Audio: loudBass)).R, 6);
        Assert.Equal(0, (pulse with { Band = AudioBand.Treble }).Sample(0, 0, new LightingInputs(0, Audio: loudBass)).R, 6);
    }

    [Fact]
    public void The_spectrum_is_brightest_where_the_sound_is()
    {
        var bands = new double[16];
        bands[0] = 1;
        var inputs = new LightingInputs(0, Audio: new AudioLevels(1, 1, 0, 0, bands));
        var spectrum = new AudioSpectrumEffect(Bright, Bright);
        Assert.Equal(1, spectrum.Sample(0.01, 0.5, inputs).R, 6);
        Assert.Equal(0, spectrum.Sample(0.9, 0.5, inputs).R, 6);
        Assert.Equal(Color.Black, spectrum.Sample(0.5, 0.5, new LightingInputs(0)));
    }

    [Fact]
    public async Task The_music_effects_are_saved_and_the_manager_only_asks_for_sound_when_one_is_on()
    {
        var path = Path.Combine(Path.GetTempPath(), "ember-audio-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var scene = new Scene("s", [new Layer("a", "a", new AudioPulseEffect(Dark, Bright, AudioBand.Mid, 2)), new Layer("b", "b", new AudioSpectrumEffect(Dark, Bright))], []);
            var store = new LightingStore(path);
            Assert.True(store.TrySave(new LightingState(true, "s", [scene])));
            var back = store.Load().Current.Layers;
            Assert.Equal(new AudioPulseEffect(Dark, Bright, AudioBand.Mid, 2), back[0].Effect);
            Assert.IsType<AudioSpectrumEffect>(back[1].Effect);

            await using var on = new LightingManager(store, TimeProvider.System, []);
            Assert.True(on.WantsAudio);
            store.TrySave(new LightingState(true, "s", [scene with { Layers = [new Layer("c", "c", new StaticEffect(Bright))] }]));
            await using var other = new LightingManager(store, TimeProvider.System, []);
            Assert.False(other.WantsAudio);
        }
        finally { File.Delete(path); }
    }
}
