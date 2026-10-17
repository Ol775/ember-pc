using System.Globalization;
using System.Security;
using System.Text;

namespace Ember.Core;

public static class StreamDeckActions
{
    public const string Prefix = "dev.ember.pc.";
    public const string Scene = Prefix + "scene";
    public const string Power = Prefix + "power";
    public const string Brightness = Prefix + "brightness";
    public const string FanMode = Prefix + "fanmode";
    public const string CycleMode = Prefix + "cyclemode";
    public const string Open = Prefix + "open";
    public const string Readout = Prefix + "readout";
}

public enum FaceTint { Normal, Warm, Hot, Dim }

/// <summary>What a key shows: a small picture (a name from <see cref="StreamDeckGlyphs"/>), a label, the main value, whether it is the "selected" one, and a colour hint.</summary>
public sealed record KeyFace(string Label, string Value, bool Highlight, FaceTint Tint, string? Glyph = null);

/// <summary>What the plugin currently knows about Ember. <see cref="AppOnline"/> is whether the app answered; mode and readings come from the service.</summary>
public sealed record EmberState(bool AppOnline, Readouts Readouts, FanMode? Mode, AppStatus? Lights);

public static class StreamDeckFaces
{
    public const int MaxValueChars = 12;
    public const double WarmFromC = 70, HotFromC = 85;

    /// <summary>The GPU fan in RPM; a card that idles with its fans stopped reports 0 RPM, so the percentage the card is asked for is shown instead when it has one.</summary>
    static string GpuFanText(Readouts r) =>
        r.GpuFanRpm is null or 0 && r.GpuFanPercent is { } p and > 0 ? p.ToString("0", System.Globalization.CultureInfo.InvariantCulture) + "%" : StreamDeckLogic.FormatRpm(r.GpuFanRpm);

    public static KeyFace For(string action, IReadOnlyDictionary<string, string> settings, EmberState s)
    {
        string Setting(string key) => settings.TryGetValue(key, out var v) ? v : "";
        KeyFace Offline(string label, string glyph) => new(label, "offline", false, FaceTint.Dim, glyph);

        switch (action)
        {
            case StreamDeckActions.Scene:
                if (!s.AppOnline || s.Lights is null) return Offline("Scene", "scene");
                var scene = Setting("scene");
                return scene.Length == 0
                    ? new KeyFace("Scene", "Choose", false, FaceTint.Dim, "scene")
                    : new KeyFace("Scene", Short(scene), scene == s.Lights.CurrentScene, FaceTint.Normal, "scene");
            case StreamDeckActions.Power:
                if (!s.AppOnline || s.Lights is null) return Offline("Lights", "bulb");
                return new KeyFace("Lights", s.Lights.On ? "On" : "Off", s.Lights.On, FaceTint.Normal, "bulb");
            case StreamDeckActions.Brightness:
                if (!s.AppOnline || s.Lights is null) return Offline("Brightness", "sun");
                var down = double.TryParse(Setting("step"), NumberStyles.Float, CultureInfo.InvariantCulture, out var step) && step < 0;
                return new KeyFace(down ? "Dimmer" : "Brighter", Math.Round(s.Lights.Brightness * 100).ToString("F0", CultureInfo.InvariantCulture) + "%", false, FaceTint.Normal, "sun");
            case StreamDeckActions.FanMode:
                if (!Enum.TryParse<FanMode>(Setting("mode"), out var mode) || !StreamDeckLogic.CycleOrder.Contains(mode))
                    return new KeyFace("Fan", "Choose", false, FaceTint.Dim, "fan");
                return new KeyFace("Fan", StreamDeckLogic.ModeLabel(mode), s.Mode == mode, FaceTint.Normal, "fan");
            case StreamDeckActions.CycleMode:
                return s.Mode is { } m
                    ? new KeyFace("Fan mode", StreamDeckLogic.ModeLabel(m), false, FaceTint.Normal, "cycle")
                    : new KeyFace("Fan mode", "-", false, FaceTint.Dim, "cycle");
            case StreamDeckActions.Open:
                return new KeyFace("Ember", "Open", false, FaceTint.Normal, "open");
            case StreamDeckActions.Readout:
                var r = s.Readouts;
                return (Setting("value") is { Length: > 0 } which ? which : "cpuTemp") switch
                {
                    "cpuTemp" => new KeyFace("CPU", StreamDeckLogic.FormatTemp(r.CpuTemp), false, TempTint(r.CpuTemp), "thermometer"),
                    "gpuTemp" => new KeyFace("GPU", StreamDeckLogic.FormatTemp(r.GpuTemp), false, TempTint(r.GpuTemp), "thermometer"),
                    "cpuFan" => new KeyFace("CPU FAN", StreamDeckLogic.FormatRpm(r.CpuFanRpm), false, FaceTint.Normal, "fan"),
                    "gpuFan" => new KeyFace("GPU FAN", GpuFanText(r), false, FaceTint.Normal, "fan"),
                    _ => new KeyFace("Readout", "Choose", false, FaceTint.Dim, "thermometer"),
                };
            default:
                return new KeyFace("Ember", "-", false, FaceTint.Dim);
        }
    }

    static FaceTint TempTint(double? c) => c is { } t ? t >= HotFromC ? FaceTint.Hot : t >= WarmFromC ? FaceTint.Warm : FaceTint.Normal : FaceTint.Dim;

    static string Short(string s) => s.Length <= MaxValueChars ? s : s[..(MaxValueChars - 1)] + "…";

    /// <summary>
    /// A 144 by 144 picture of the face: dark rounded square (Ember orange when selected), the glyph at the top, then the small
    /// label and the large value. Text from scene names is escaped.
    /// </summary>
    public static string Svg(KeyFace f)
    {
        var bg = f.Highlight ? "#ff7a2f" : "#1c1c1e"; // Ember's accent orange when selected
        var fg = f.Highlight ? "#1c1c1e" : f.Tint switch { FaceTint.Warm => "#ffb340", FaceTint.Hot => "#ff453a", FaceTint.Dim => "#8e8e93", _ => "#ffffff" };
        var labelFg = f.Highlight ? "#5a2a0c" : "#8e8e93";
        var size = f.Value.Length <= 4 ? 40 : f.Value.Length <= 7 ? 30 : 22;
        var glyph = f.Glyph is null ? "" : StreamDeckGlyphs.Svg(f.Glyph, fg, 57, 10, 1.25);
        // With a picture the label and value move down to make room; without one they keep the old, higher positions.
        var (labelY, valueY) = glyph.Length > 0 ? (72, 118) : (42, 92);
        var sb = new StringBuilder();
        sb.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"144\" height=\"144\" viewBox=\"0 0 144 144\">");
        sb.Append("<rect width=\"144\" height=\"144\" rx=\"22\" fill=\"").Append(bg).Append("\"/>");
        sb.Append(glyph);
        sb.Append("<text x=\"72\" y=\"").Append(labelY).Append("\" font-family=\"Segoe UI, sans-serif\" font-size=\"18\" text-anchor=\"middle\" fill=\"").Append(labelFg).Append("\">").Append(SecurityElement.Escape(f.Label)).Append("</text>");
        sb.Append("<text x=\"72\" y=\"").Append(valueY).Append("\" font-family=\"Segoe UI, sans-serif\" font-weight=\"600\" font-size=\"").Append(size).Append("\" text-anchor=\"middle\" fill=\"").Append(fg).Append("\">").Append(SecurityElement.Escape(f.Value)).Append("</text>");
        sb.Append("</svg>");
        return sb.ToString();
    }
}
