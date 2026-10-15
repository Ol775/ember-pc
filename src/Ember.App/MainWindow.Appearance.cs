using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Ember.Core;

namespace Ember.App;

/// <summary>Settings, Appearance: theme (dark, OLED black, light), colour scheme, accent colour and highlight strength.</summary>
public partial class MainWindow
{
    readonly List<(AccentChoice Accent, RadioButton Swatch)> _swatches = [];
    readonly List<(AccentChoice Accent, RadioButton Swatch)> _gpuSwatches = [];
    Button? _gpuAuto;
    double _scale = 1;
    bool _scaleApplied;
    TextBox? _customHex;
    TextBlock? _customNote;
    bool _syncingCustom;

    /// <summary>The production build has no roadmap page, and no game detection or GPU fan control (still being proven). Every integration is there; Nanoleaf and Razer are marked Beta.</summary>
    void ApplyBuildChannel()
    {
        if (!BuildFeatures.IsProduction) return;
        NavRoadmap.Visibility = Visibility.Collapsed;
        RoadmapBox.Tag = "off";
        MerossBox.Tag = "off";
        MerossBox.Visibility = Visibility.Collapsed;
    }

    UpdateChannel UpdateChannelInUse => _vm.Prefs.Channel;

    string? ChangelogText() => BuildFeatures.IsProduction ? ReleaseNotesFilter.ProductionOnly(DocView.ReadEmbedded("CHANGELOG.md")) : DocView.ReadEmbedded("CHANGELOG.md");

    void ApplyAppearance()
    {
        ThemeManager.Apply(_vm.Prefs);
        ThemeManager.ApplyChrome(this);
        var scale = UiSizes.Factor(_vm.Prefs.Size);
        Root.LayoutTransform = scale == 1.0 ? Transform.Identity : new ScaleTransform(scale, scale);
        (MinWidth, MinHeight) = (820 * scale, 560 * scale);
        // A larger interface needs a wider window, or the dashboard cards clip: widen it when the size is raised (not at start-up, where the saved size wins).
        if (_scaleApplied && scale > _scale && WindowState == WindowState.Normal)
            Width = Math.Max(Width, Math.Min(1000 * scale, SystemParameters.WorkArea.Width));
        (_scale, _scaleApplied) = (scale, true);
        foreach (var (accent, swatch) in _gpuSwatches)
        {
            swatch.IsChecked = _vm.Prefs.GpuAccent == accent;
            swatch.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(ThemePalette.AccentColour(accent)));
        }
        if (_gpuAuto is not null) _gpuAuto.Content = _vm.Prefs.GpuAccent is null ? "Auto (in use)" : "Auto";
        foreach (var (accent, swatch) in _swatches)
        {
            swatch.IsChecked = accent == _vm.Prefs.Accent;
            swatch.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(ThemePalette.AccentColour(accent, _vm.Prefs.CustomAccent)));
        }
        if (_customHex is not null && !_customHex.IsKeyboardFocusWithin)
        {
            _syncingCustom = true;
            _customHex.Text = _vm.Prefs.CustomAccent;
            _syncingCustom = false;
        }
    }

    static TextBlock Caption(string text)
    {
        var t = LightsUi.Text(text, muted: true, size: 12);
        t.TextWrapping = TextWrapping.Wrap;
        t.Margin = new Thickness(0, 2, 0, 0);
        return t;
    }

    void BuildAppearance()
    {
        // Theme
        var themes = new[] { ThemeMode.Dark, ThemeMode.Oled, ThemeMode.Light, ThemeMode.System };
        ThemeHost.Children.Add(LightsUi.Segments(["Dark", "OLED black", "Light", "Match Windows"], Math.Max(0, Array.IndexOf(themes, _vm.Prefs.Theme)),
            i => Remember(_vm.Prefs with { Theme = themes[i] }), "Theme"));
        ThemeHost.Children.Add(Caption("OLED black makes the background pure black, so an OLED screen switches those pixels off. Match Windows follows the light or dark setting of Windows."));

        // Colour scheme
        var tones = Enum.GetValues<SurfaceTone>();
        ToneHost.Children.Add(LightsUi.Segments(["Warm", "Neutral", "Cool"], (int)_vm.Prefs.Tone, i => Remember(_vm.Prefs with { Tone = tones[i] }), "Tone"));
        ToneHost.Children.Add(Caption("The tint of backgrounds, panels and buttons: warm brown-black (the original), plain grey, or a cool blue-grey. It works with every theme."));

        // Accent colour
        var row = new WrapPanel();
        foreach (var accent in Enum.GetValues<AccentChoice>())
        {
            var swatch = new RadioButton { GroupName = "Accent", Tag = accent.ToString(), Style = (Style)FindResource("Swatch") };
            System.Windows.Automation.AutomationProperties.SetName(swatch, accent == AccentChoice.Custom ? "Custom accent" : accent + " accent");
            swatch.Click += (_, _) => Remember(_vm.Prefs with { Accent = accent });
            _swatches.Add((accent, swatch));
            row.Children.Add(swatch);
        }
        AccentHost.Children.Add(row);

        var custom = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        custom.Children.Add(LightsUi.Text("Custom colour", muted: true));
        ((TextBlock)custom.Children[0]).VerticalAlignment = VerticalAlignment.Center;
        ((TextBlock)custom.Children[0]).Margin = new Thickness(0, 0, 10, 0);
        _customHex = new TextBox { Width = 110, MaxLength = 7, Style = (Style)FindResource("TextField") };
        System.Windows.Automation.AutomationProperties.SetName(_customHex, "Custom accent colour, as #rrggbb");
        _customHex.TextChanged += (_, _) =>
        {
            if (_syncingCustom) return;
            var text = _customHex.Text.Trim();
            if (ThemePalette.IsHexColour(text))
            {
                _customNote!.Text = "";
                Remember(_vm.Prefs with { Accent = AccentChoice.Custom, CustomAccent = text.ToLowerInvariant() });
            }
            else _customNote!.Text = "Type a colour like #1e90ff.";
        };
        custom.Children.Add(_customHex);
        AccentHost.Children.Add(custom);
        _customNote = Caption("");
        AccentHost.Children.Add(_customNote);
        AccentHost.Children.Add(Caption("Colours that would be hard to read are lightened or darkened a little so text stays readable."));

        // Highlights, with a preview that uses the same colours as the real thing
        var strengths = Enum.GetValues<HighlightStrength>();
        HighlightHost.Children.Add(LightsUi.Segments(["Subtle", "Normal", "Vivid"], (int)_vm.Prefs.Highlight, i => Remember(_vm.Prefs with { Highlight = strengths[i] }), "Highlight"));
        HighlightHost.Children.Add(Caption("How strongly the accent colour tints the selected menu item and the banners."));
        HighlightHost.Children.Add(Preview());

        // Interface size
        var sizes = Enum.GetValues<UiSize>();
        SizeHost.Children.Add(LightsUi.Segments(["Small", "Normal", "Large"], (int)_vm.Prefs.Size, i => Remember(_vm.Prefs with { Size = sizes[i] }), "Interface size"));
        SizeHost.Children.Add(Caption("Makes everything in the window smaller or larger, text and spacing together."));

        // GPU colour: Auto, or one of the accent colours
        var gpuRow = new WrapPanel();
        foreach (var accent in Enum.GetValues<AccentChoice>().Where(a => a != AccentChoice.Custom))
        {
            var swatch = new RadioButton { GroupName = "GpuColour", Tag = accent.ToString(), Style = (Style)FindResource("Swatch") };
            System.Windows.Automation.AutomationProperties.SetName(swatch, accent + " for the GPU");
            swatch.Click += (_, _) => Remember(_vm.Prefs with { GpuColour = accent.ToString() });
            _gpuSwatches.Add((accent, swatch));
            gpuRow.Children.Add(swatch);
        }
        GpuHost.Children.Add(gpuRow);
        _gpuAuto = LightsUi.Pill("Auto", () => Remember(_vm.Prefs with { GpuColour = "Auto" }), "GPU colour: Auto");
        _gpuAuto.Margin = new Thickness(0, 10, 0, 0);
        _gpuAuto.HorizontalAlignment = HorizontalAlignment.Left;
        GpuHost.Children.Add(_gpuAuto);
        GpuHost.Children.Add(Caption("The colour of the GPU number and its line on the temperature chart. Auto picks a partner to your accent colour and the theme."));
    }

    static FrameworkElement Preview()
    {
        var chip = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
        chip.SetResourceReference(Border.BackgroundProperty, "AccentSoftBrush");
        var chipText = LightsUi.Text("Selected item", size: 13, weight: FontWeights.SemiBold);
        chipText.SetResourceReference(TextBlock.ForegroundProperty, "AccentTextBrush");
        chip.Child = chipText;

        var banner = new Border { CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), Padding = new Thickness(12, 8, 12, 8), VerticalAlignment = VerticalAlignment.Center };
        banner.SetResourceReference(Border.BackgroundProperty, "BannerBrush");
        banner.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
        banner.Child = LightsUi.Text("A banner or a notice", size: 13);

        var bar = new Border { Width = 60, Height = 6, CornerRadius = new CornerRadius(3), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        bar.SetResourceReference(Border.BackgroundProperty, "AccentBrush");

        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        row.Children.Add(chip);
        row.Children.Add(banner);
        row.Children.Add(bar);
        return row;
    }
}
