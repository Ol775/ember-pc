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

/// <summary>What a key shows: a small label, the main value, whether it is the "selected" one, and a colour hint.</summary>
public sealed record KeyFace(string Label, string Value, bool Highlight, FaceTint Tint);

/// <summary>What the plugin currently knows about Ember. <see cref="AppOnline"/> is whether the app answered; mode and readings come from the service.</summary>
public sealed record EmberState(bool AppOnline, Readouts Readouts, FanMode? Mode, AppStatus? Lights);

public static class StreamDeckFaces
{
    public const int MaxValueChars = 12;
    public const double WarmFromC = 70, HotFromC = 85;

    public static KeyFace For(string action, IReadOnlyDictionary<string, string> settings, EmberState s)
    {
        string Setting(string key) => settings.TryGetValue(key, out var v) ? v : "";
        KeyFace Offline(string label) => new(label, "offline", false, FaceTint.Dim);

        switch (action)
        {
            case StreamDeckActions.Scene:
                if (!s.AppOnline || s.Lights is null) return Offline("Scene");
                var scene = Setting("scene");
                return scene.Length == 0
                    ? new KeyFace("Scene", "Choose", false, FaceTint.Dim)
                    : new KeyFace("Scene", Short(scene), scene == s.Lights.CurrentScene, FaceTint.Normal);
            case StreamDeckActions.Power:
                if (!s.AppOnline || s.Lights is null) return Offline("Lights");
                return new KeyFace("Lights", s.Lights.On ? "On" : "Off", s.Lights.On, FaceTint.Normal);
            case StreamDeckActions.Brightness:
                if (!s.AppOnline || s.Lights is null) return Offline("Brightness");
                var down = double.TryParse(Setting("step"), NumberStyles.Float, CultureInfo.InvariantCulture, out var step) && step < 0;
                return new KeyFace(down ? "Dimmer" : "Brighter", Math.Round(s.Lights.Brightness * 100).ToString("F0", CultureInfo.InvariantCulture) + "%", false, FaceTint.Normal);
            case StreamDeckActions.FanMode:
                if (!Enum.TryParse<FanMode>(Setting("mode"), out var mode) || !StreamDeckLogic.CycleOrder.Contains(mode))
                    return new KeyFace("Fan", "Choose", false, FaceTint.Dim);
                return new KeyFace("Fan", StreamDeckLogic.ModeLabel(mode), s.Mode == mode, FaceTint.Normal);
            case StreamDeckActions.CycleMode:
                return s.Mode is { } m ? new KeyFace("Fan mode", StreamDeckLogic.ModeLabel(m), false, FaceTint.Normal) : new KeyFace("Fan mode", "-", false, FaceTint.Dim);
            case StreamDeckActions.Open:
                return new KeyFace("Ember", "Open", false, FaceTint.Normal);
            case StreamDeckActions.Readout:
                var r = s.Readouts;
                return Setting("value") switch
                {
                    "cpuTemp" => new KeyFace("CPU", StreamDeckLogic.FormatTemp(r.CpuTemp), false, TempTint(r.CpuTemp)),
                    "gpuTemp" => new KeyFace("GPU", StreamDeckLogic.FormatTemp(r.GpuTemp), false, TempTint(r.GpuTemp)),
                    "cpuFan" => new KeyFace("CPU FAN", StreamDeckLogic.FormatRpm(r.CpuFanRpm), false, FaceTint.Normal),
                    "gpuFan" => new KeyFace("GPU FAN", StreamDeckLogic.FormatRpm(r.GpuFanRpm), false, FaceTint.Normal),
                    _ => new KeyFace("Readout", "Choose", false, FaceTint.Dim),
                };
            default:
                return new KeyFace("Ember", "-", false, FaceTint.Dim);
        }
    }

    static FaceTint TempTint(double? c) => c is { } t ? t >= HotFromC ? FaceTint.Hot : t >= WarmFromC ? FaceTint.Warm : FaceTint.Normal : FaceTint.Dim;

    static string Short(string s) => s.Length <= MaxValueChars ? s : s[..(MaxValueChars - 1)] + "…";

    /// <summary>A 144 by 144 picture of the face: dark rounded square (accent blue when highlighted), small label above a large value.</summary>
    public static string Svg(KeyFace f)
    {
        var bg = f.Highlight ? "#0a84ff" : "#1c1c1e";
        var fg = f.Tint switch { FaceTint.Warm => "#ff9f0a", FaceTint.Hot => "#ff453a", FaceTint.Dim => "#8e8e93", _ => "#ffffff" };
        var labelFg = f.Highlight ? "#d6e9ff" : "#8e8e93";
        var size = f.Value.Length <= 4 ? 44 : f.Value.Length <= 7 ? 32 : 24;
        var sb = new StringBuilder();
        sb.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"144\" height=\"144\" viewBox=\"0 0 144 144\">");
        sb.Append("<rect width=\"144\" height=\"144\" rx=\"22\" fill=\"").Append(bg).Append("\"/>");
        sb.Append("<text x=\"72\" y=\"42\" font-family=\"Segoe UI, sans-serif\" font-size=\"20\" text-anchor=\"middle\" fill=\"").Append(labelFg).Append("\">").Append(SecurityElement.Escape(f.Label)).Append("</text>");
        sb.Append("<text x=\"72\" y=\"92\" font-family=\"Segoe UI, sans-serif\" font-weight=\"600\" font-size=\"").Append(size).Append("\" text-anchor=\"middle\" fill=\"").Append(fg).Append("\">").Append(SecurityElement.Escape(f.Value)).Append("</text>");
        sb.Append("</svg>");
        return sb.ToString();
    }
}
