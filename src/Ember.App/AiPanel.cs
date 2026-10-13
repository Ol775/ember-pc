using System.Windows;
using System.Windows.Controls;
using Ember.Core;
using static Ember.App.LightsUi;

namespace Ember.App;

/// <summary>
/// The "AI assist" panel on the Fans page: learns how hot this PC runs (temperatures only, kept on this PC), suggests a curve for
/// the chosen quiet-to-cool balance, and applies it only when the owner presses Apply. Nothing changes a fan on its own.
/// </summary>
public sealed class AiPanel : StackPanel
{
    readonly ThermalProfile _profile;
    readonly Func<AppPreferences> _prefs;
    readonly Action<AppPreferences> _setPrefs;
    readonly Func<CurveSuggestion, Task<string?>> _apply;
    readonly Action _forget;
    readonly TextBlock _status = Text("", muted: true);
    readonly StackPanel _result = new() { Margin = new Thickness(0, 8, 0, 0) };
    CurveSuggestion? _suggestion;

    public AiPanel(ThermalProfile profile, Func<AppPreferences> prefs, Action<AppPreferences> setPrefs, Func<CurveSuggestion, Task<string?>> apply, Action forget)
    {
        _profile = profile; _prefs = prefs; _setPrefs = setPrefs; _apply = apply; _forget = forget;

        Children.Add(Text("Ember can learn how hot your PC normally runs and suggest fan curves that stay quiet through that range and only climb above it. It only suggests: nothing changes until you press Apply, and the safety rules (full speed at 90 °C) always stay on.", muted: true));
        var learn = Switch("Learn from my temperatures", prefs().AiLearning, on => { _setPrefs(_prefs() with { AiLearning = on }); Refresh(); });
        learn.Margin = new Thickness(0, 12, 0, 0);
        Children.Add(learn);
        Children.Add(_status);
        Children.Add(Labelled("Noise or cooling", 0, 100, prefs().AiPreference * 100, v => v < 34 ? "Quieter" : v > 66 ? "Cooler" : "Balanced",
            v => _setPrefs(_prefs() with { AiPreference = v / 100 }), 5));
        var buttons = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        buttons.Children.Add(Pill("Suggest a curve", Suggest));
        buttons.Children.Add(Pill("Forget what it learned", () => { _forget(); _suggestion = null; _result.Children.Clear(); Refresh(); }));
        Children.Add(buttons);
        Children.Add(_result);
        Children.Add(Text("It stores one count per degree for the CPU and the case, in a small file in your Ember folder on this PC, and nothing about what you do. Nothing is sent anywhere.", muted: true, size: 12));
        Refresh();
    }

    /// <summary>Updates the "watched for" line. Cheap: called from the page timer.</summary>
    public void Refresh()
    {
        var on = _prefs().AiLearning;
        var hours = _profile.Hours;
        _status.Text = !on && hours <= 0 ? "Learning is off."
            : (on ? "Learning: " : "Learning is paused. ") + $"{hours:0.#} hours of readings so far" + (hours < CurveAdvisor.MinHours ? $" (it needs about {CurveAdvisor.MinHours:0}, and a few days is better)." : ".");
    }

    void Suggest()
    {
        _result.Children.Clear();
        var r = CurveAdvisor.Suggest(_profile, _prefs().AiPreference);
        _suggestion = r.Suggestion;
        if (r.Suggestion is not { } s) { _result.Children.Add(Text(r.Message)); return; }
        foreach (var note in s.Notes) _result.Children.Add(Text(note, size: 13));
        var row = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        var done = Text("", muted: true);
        row.Children.Add(Pill("Apply as my Manual curves", async () =>
        {
            if (_suggestion is not { } chosen) return;
            var error = await _apply(chosen);
            done.Text = error is null ? "Applied. Ember is now in Manual mode with these curves; you can still edit them in the curve editor above." : error;
        }));
        row.Children.Add(Pill("Dismiss", () => { _suggestion = null; _result.Children.Clear(); }));
        _result.Children.Add(row);
        _result.Children.Add(done);
    }
}
