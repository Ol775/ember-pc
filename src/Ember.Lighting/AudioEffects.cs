namespace Ember.Lighting;

public enum AudioBand { Level, Bass, Mid, Treble }

/// <summary>One colour that moves from Quiet to Loud with the chosen part of the sound. With no sound playing it shows the quiet colour.</summary>
public sealed record AudioPulseEffect(Color Quiet, Color Loud, AudioBand Band = AudioBand.Bass, double Sensitivity = 1) : Effect
{
    public override Color Sample(double x, double y, LightingInputs inputs)
    {
        var a = inputs.Audio ?? AudioLevels.Silent;
        var level = Band switch { AudioBand.Bass => a.Bass, AudioBand.Mid => a.Mid, AudioBand.Treble => a.Treble, _ => a.Level };
        return Color.Lerp(Quiet, Loud, level * Math.Clamp(Sensitivity, 0.1, 4));
    }

    public override string Label => "Music pulse";
}

/// <summary>A colour gradient from Low to High across the canvas whose brightness at each spot follows the sound at that pitch (low pitches on the left).</summary>
public sealed record AudioSpectrumEffect(Color Low, Color High, double Sensitivity = 1) : Effect
{
    public override Color Sample(double x, double y, LightingInputs inputs)
    {
        var a = inputs.Audio ?? AudioLevels.Silent;
        if (a.Bands.Count == 0) return Color.Black;
        var band = Math.Clamp((int)Math.Floor(x * a.Bands.Count), 0, a.Bands.Count - 1);
        var level = Math.Clamp(a.Bands[band] * Math.Clamp(Sensitivity, 0.1, 4), 0, 1);
        return Color.Lerp(Low, High, Math.Clamp(x, 0, 1)).Scaled(level);
    }

    public override string Label => "Music spectrum";
}
