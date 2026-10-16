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

    /// <summary>The production build has no roadmap page and no GPU fan control (still being proven). Every integration and the Power and Games pages are there; Nanoleaf, Razer, Meross, Tapo and game detection are marked Beta.</summary>
    void ApplyBuildChannel()
    {
        if (!BuildFeatures.Current.GameDetection) NavGames.Visibility = Visibility.Collapsed; // game detection is still being proven in production
        if (BuildFeatures.NewLook)
        {
            // The redesign: the Instrument Sans font, a slightly wider sidebar and the new dashboard.
            FontFamily = Fonts.Ui;
            ArrangeSidebar();
            ArrangeSettings();
            Root.ColumnDefinitions[0].Width = new GridLength(224);
            SettingsHeading.FontSize = 24;
            SettingsSub.Visibility = Visibility.Visible;
            ArrangeFansPage();
            OnNavChanged(this, new RoutedEventArgs());   // the dashboard was chosen before the redesign page existed
            NewDash.ModePicked += mode => _ = SendAsync(new SetMode(mode));
            NewDash.ChartResized += (w, h) => { _vm.SetChartSize(w, h); Refresh(); };
            Specs.ReportReady += report => NewDash.SetDrives(report);
            _ = Task.Delay(8000).ContinueWith(_ => Dispatcher.InvokeAsync(() => Specs.LoadAsync())); // reads the drives once, after start-up has settled
        }
        if (!BuildFeatures.IsProduction) return;
        NavRoadmap.Visibility = Visibility.Collapsed;
        HideAutomation();
        RoadmapBox.Tag = "off";
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
        t.Margin = BuildFeatures.NewLook ? new Thickness(0, 4, 0, 10) : new Thickness(0, 2, 0, 0);
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


public partial class MainWindow
{
    /// <summary>The redesign's sidebar: a bigger flame, wider items and the connection status in a small card at the bottom.</summary>
    void ArrangeSidebar()
    {
        LogoRow.Margin = new Thickness(26, 22, 20, 27);
        LogoPath.Stretch = Stretch.Uniform;
        LogoPath.Width = 21;
        LogoPath.Height = 30;
        LogoPath.Margin = new Thickness(0, 0, 13, 0);

        foreach (var element in new UIElement[] { ConnectionRow, BuildLabelText, RestartAdminButton }) SidebarDock.Children.Remove(element);
        ConnectionRow.Margin = new Thickness(0);
        BuildLabelText.Margin = new Thickness(0, 4, 0, 0);
        BuildLabelText.FontSize = 12.5;
        ConnectionLabel.FontSize = 12.5;
        RestartAdminButton.Margin = new Thickness(0, 10, 0, 0);
        var inner = new StackPanel();
        inner.Children.Add(ConnectionRow);
        inner.Children.Add(BuildLabelText);
        inner.Children.Add(RestartAdminButton);
        var card = new Border { CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), Padding = new Thickness(14, 12, 14, 12), Margin = new Thickness(14, 0, 14, 24), Child = inner };
        card.SetResourceReference(Border.BackgroundProperty, "PanelBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "PanelBorderBrush");
        DockPanel.SetDock(card, Dock.Bottom);
        SidebarDock.Children.Insert(SidebarDock.Children.Count - 1, card);
    }
}

public partial class MainWindow
{
    /// <summary>
    /// The redesign's Fans page: the title with the mode switch on its right, then the curve on the left and on the right the fan
    /// headers (moved here from the Devices page) and the AI assist. The classic page keeps everything in one column.
    /// </summary>
    void ArrangeFansPage()
    {
        if (FansPageView.Content is not StackPanel page || DevicesPageView.Content is not StackPanel devices) return;
        var at = page.Children.IndexOf(FanCurvePanel);
        if (at < 0) return;
        page.Margin = new Thickness(32, 30, 32, 28);

        // Heading: the title and a sentence on the left, the modes on the right.
        var modeParent = (Panel)ModeTrack.Parent;
        modeParent.Children.Remove(ModeTrack);
        modeParent.Children.Remove(ModeSourceText);
        page.Children.Remove(ModePanelBox);
        ModeTrack.VerticalAlignment = VerticalAlignment.Top;
        ModeTrack.HorizontalAlignment = HorizontalAlignment.Right;
        ModeTrack.Margin = new Thickness(12, 0, 0, 0);
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 16) };
        DockPanel.SetDock(ModeTrack, Dock.Right);
        head.Children.Add(ModeTrack);
        var titles = new StackPanel();
        var title = Ui.Text("Fans", 28, bold: true);
        title.TextWrapping = TextWrapping.NoWrap;
        System.Windows.Automation.AutomationProperties.SetHeadingLevel(title, System.Windows.Automation.AutomationHeadingLevel.Level2);
        titles.Children.Add(title);
        titles.Children.Add(Ui.Muted("Pick a mode, or shape your own curve. Ember never lets a fan stop while the CPU is hot.", 14).WithMargin(0, 4, 0, 0));
        ModeSourceText.Margin = new Thickness(0, 4, 0, 0);
        ModeSourceText.FontSize = 12.5;
        titles.Children.Add(ModeSourceText);
        head.Children.Add(titles);
        var heading = page.Children.IndexOf(FansHeading);
        page.Children.Remove(FansHeading);
        page.Children.Insert(Math.Max(0, heading), head);
        at = page.Children.IndexOf(FanCurvePanel);

        devices.Children.Remove(FanHeadersPanel);
        foreach (var box in new[] { FanCurvePanel, AiPanelBox, GamesPanelBox }) page.Children.Remove(box);

        // AI assist: the accent-tinted card of the mockups with the existing controls inside it.
        var aiContent = (UIElement)AiPanelBox.Content;
        AiPanelBox.Content = null;
        var ai = new StackPanel();
        ai.Children.Add(Ui.Label("AI assist", "AccentTextBrush"));
        ((FrameworkElement)aiContent).Margin = new Thickness(0, 8, 0, 0);
        ai.Children.Add(aiContent);
        var aiCard = Ui.Card(ai, new Thickness(20, 16, 20, 16), soft: true, name: "AI assist");
        aiCard.Margin = new Thickness(0, 0, 0, 16);

        var right = new StackPanel { Margin = new Thickness(8, 0, 0, 0) };
        FanHeadersPanel.Margin = new Thickness(0, 0, 0, 16);
        FanHeadersList.ItemTemplate = (DataTemplate)FindResource("FanHeaderCompact");
        right.Children.Add(FanHeadersPanel);
        right.Children.Add(aiCard);
        right.Children.Add(GamesPanelBox);
        GamesPanelBox.Margin = new Thickness(0, 0, 0, 16);
        FanCurvePanel.Margin = new Thickness(0, 0, 8, 16);
        FanCurvePanel.VerticalAlignment = VerticalAlignment.Stretch;

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.7, GridUnitType.Star), MinWidth = 340 });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 300 });
        Grid.SetColumn(right, 1);
        body.Children.Add(FanCurvePanel);
        body.Children.Add(right);
        page.Children.Insert(at, body);
    }
}
