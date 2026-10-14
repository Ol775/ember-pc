using System.Text.Json.Serialization;

namespace Ember.Lighting;

public enum TempSensor { Cpu, Gpu }

/// <summary>Everything an effect may look at for one frame.</summary>
public sealed record LightingInputs(double Seconds, double? CpuTemp = null, double? GpuTemp = null, IReadOnlyList<Color>? ScreenZones = null, AudioLevels? Audio = null,
    IReadOnlyList<KeyPulse>? Pulses = null, LedLocal? Local = null);

/// <summary>A pattern over the unit square (x and y from 0 to 1) that changes with time and sensors.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(StaticEffect), "static")]
[JsonDerivedType(typeof(WaveEffect), "wave")]
[JsonDerivedType(typeof(BreathingEffect), "breathing")]
[JsonDerivedType(typeof(TemperatureGlowEffect), "temperature")]
[JsonDerivedType(typeof(ScreenSyncEffect), "screen")]
[JsonDerivedType(typeof(AudioPulseEffect), "audiopulse")]
[JsonDerivedType(typeof(AudioSpectrumEffect), "audiospectrum")]
[JsonDerivedType(typeof(RippleEffect), "ripple")]
[JsonDerivedType(typeof(AudioBarsEffect), "audiobars")]
[JsonDerivedType(typeof(ReactiveKeysEffect), "reactivekeys")]
[JsonDerivedType(typeof(FireEffect), "fire")]
[JsonDerivedType(typeof(RainEffect), "rain")]
[JsonDerivedType(typeof(KeyGroupEffect), "keygroup")]
[JsonDerivedType(typeof(KeyHighlightEffect), "keys")]
public abstract record Effect
{
    public abstract Color Sample(double x, double y, LightingInputs inputs);

    [JsonIgnore] public abstract string Label { get; }
}

public sealed record StaticEffect(Color Color) : Effect
{
    public override Color Sample(double x, double y, LightingInputs inputs) => Color;
    public override string Label => "Static colour";
}

/// <summary>Two colours rolling across the canvas. Speed is in cycles per second, wavelength in canvas widths.</summary>
public sealed record WaveEffect(Color A, Color B, double Speed = 0.4, double Wavelength = 1.0, double AngleDegrees = 0) : Effect
{
    public override Color Sample(double x, double y, LightingInputs inputs)
    {
        var angle = AngleDegrees * Math.PI / 180;
        var along = x * Math.Cos(angle) + y * Math.Sin(angle);
        var phase = along / Math.Max(0.05, Wavelength) - Speed * inputs.Seconds;
        return Color.Lerp(A, B, 0.5 + 0.5 * Math.Sin(2 * Math.PI * phase));
    }

    public override string Label => "Wave";
}

public sealed record BreathingEffect(Color Color, double PeriodSeconds = 4) : Effect
{
    public override Color Sample(double x, double y, LightingInputs inputs)
    {
        var level = 0.5 - 0.5 * Math.Cos(2 * Math.PI * inputs.Seconds / Math.Max(0.5, PeriodSeconds));
        return Color.Scaled(level);
    }

    public override string Label => "Breathing";
}

/// <summary>Cold colour at or below MinC, hot colour at or above MaxC, blended in between. With no reading it shows the cold colour.</summary>
public sealed record TemperatureGlowEffect(TempSensor Source, Color Cold, Color Hot, double MinC = 35, double MaxC = 85) : Effect
{
    public override Color Sample(double x, double y, LightingInputs inputs)
    {
        var temp = Source == TempSensor.Cpu ? inputs.CpuTemp : inputs.GpuTemp;
        if (temp is not { } t || MaxC <= MinC) return Cold;
        return Color.Lerp(Cold, Hot, (t - MinC) / (MaxC - MinC));
    }

    public override string Label => "Temperature glow";
}

/// <summary>The colours of one screen (null: the main screen, otherwise the Windows name of the screen, such as \.DISPLAY2), in zones from left to right. With no picture it covers nothing.</summary>
public sealed record ScreenSyncEffect(string? Monitor = null) : Effect
{
    public override Color Sample(double x, double y, LightingInputs inputs)
    {
        if (inputs.ScreenZones is not { Count: > 0 } zones) return new Color(0, 0, 0, 0);
        var i = Math.Clamp((int)Math.Floor(x * zones.Count), 0, zones.Count - 1);
        return zones[i];
    }

    public override string Label => "Screen sync";
}
