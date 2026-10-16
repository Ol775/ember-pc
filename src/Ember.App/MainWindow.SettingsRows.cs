using System.Windows;
using System.Windows.Controls;

namespace Ember.App;

/// <summary>
/// The redesign's Settings pages: every option is a row (its name and a sentence on the left, its control on the right) inside one
/// rounded card, as in the mockups. The controls themselves are the ones the classic page builds; they are moved, not copied, so
/// every handler and saved value keeps working.
/// </summary>
public partial class MainWindow
{
    /// <summary>Moves a control out of its old parent so it can be placed in a row.</summary>
    static T Detach<T>(T element) where T : FrameworkElement
    {
        if (element.Parent is Panel panel) panel.Children.Remove(element);
        else if (element.Parent is ContentControl content) content.Content = null;
        return element;
    }

    /// <summary>One option: name and description on the left, the control on the right, a hairline above all but the first.</summary>
    static FrameworkElement SettingRow(string title, string? description, FrameworkElement control, bool first = false)
    {
        var dock = new DockPanel();
        control.VerticalAlignment = VerticalAlignment.Center;
        control.Margin = new Thickness(16, 0, 0, 0);
        DockPanel.SetDock(control, Dock.Right);
        dock.Children.Add(control);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(Ui.Text(title, 14, bold: true));
        if (!string.IsNullOrEmpty(description)) text.Children.Add(Ui.Muted(description, 12.5).WithMargin(0, 3, 0, 0));
        dock.Children.Add(text);
        var line = new Border { BorderThickness = new Thickness(0, first ? 0 : 1, 0, 0), Padding = new Thickness(0, first ? 14 : 14, 0, 14), Child = dock };
        line.SetResourceReference(Border.BorderBrushProperty, "PanelBorderBrush");
        return line;
    }

    static Border SettingsCard(string tag, params FrameworkElement[] rows)
    {
        var stack = new StackPanel();
        foreach (var row in rows) stack.Children.Add(row);
        var card = Ui.Card(stack, new Thickness(24, 0, 24, 0));
        card.Tag = tag;
        card.Margin = new Thickness(0, 0, 0, 16);
        return card;
    }

    /// <summary>A switch with no label of its own, for a row that carries the name.</summary>
    static System.Windows.Controls.CheckBox Bare(System.Windows.Controls.CheckBox box)
    {
        Detach(box).Content = null;
        box.Margin = new Thickness(0);
        return box;
    }

    static FrameworkElement Track(Panel host)
    {
        var track = Detach((FrameworkElement)host.Children[0]);
        track.Margin = new Thickness(0);
        return track;
    }

    /// <summary>The title and its sentence run across the top of both columns; the section list sits under them, the options to its right.</summary>
    void ArrangeSettingsPage()
    {
        if (SettingsPageView.Content is not Grid page || page.Children.Count < 2) return;
        page.Margin = new Thickness(32, 30, 32, 28);
        page.ColumnDefinitions[0].Width = new GridLength(200);
        page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        SettingsSections.Children.Remove(SettingsHeading);
        SettingsSections.Children.Remove(SettingsSub);
        SettingsHeading.FontSize = 28;
        SettingsHeading.FontWeight = FontWeights.Bold;
        SettingsHeading.Margin = new Thickness(0);
        SettingsSub.FontSize = 14;
        SettingsSub.Margin = new Thickness(0, 4, 0, 0);
        var head = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
        head.Children.Add(SettingsHeading);
        head.Children.Add(SettingsSub);
        Grid.SetColumnSpan(head, 2);
        foreach (UIElement child in page.Children) Grid.SetRow(child, 1);
        page.Children.Add(head);
        Grid.SetRow(head, 0);
        ((FrameworkElement)page.Children[0]).Margin = new Thickness(0, 0, 16, 0);
        SettingsSections.MaxWidth = double.PositiveInfinity;
    }

    void ArrangeSettings()
    {
        var sections = SettingsSections;
        ArrangeSettingsPage();
        T[] Boxes<T>(string tag) where T : UIElement => sections.Children.OfType<T>().Where(e => e is FrameworkElement { Tag: string t } && t == tag).ToArray();

        // Appearance: one card of rows.
        var appearanceBoxes = Boxes<GroupBox>("appearance");
        if (appearanceBoxes.Length > 0)
        {
            var at = sections.Children.IndexOf(appearanceBoxes[0]);
            var hostRows = new List<FrameworkElement>();

            var theme = SettingRow("Theme", "Follow Windows, or pick one. OLED uses true black.", Track(ThemeHost), first: true);
            var tone = SettingRow("Colour scheme", "The tint of backgrounds, panels and buttons.", Track(ToneHost));
            var accentSwatches = Detach((FrameworkElement)AccentHost.Children[0]);
            var accent = SettingRow("Accent colour", "Used for gauges, highlights and the active page.", accentSwatches);
            // The custom colour box and its note sit under the swatches.
            var customBox = (StackPanel)Detach((FrameworkElement)AccentHost.Children[0]);
            customBox.Children.RemoveAt(0);   // its "Custom colour" label is the row title now
            customBox.Margin = new Thickness(0);
            var customControl = new StackPanel();
            customControl.Children.Add(customBox);
            customControl.Children.Add(Detach(_customNote!));
            var gpuSwatches = Detach((FrameworkElement)GpuHost.Children[0]);
            var gpuAuto = Detach(_gpuAuto!);
            gpuAuto.Margin = new Thickness(0, 0, 8, 0);
            var gpuControl = new StackPanel { Orientation = Orientation.Horizontal };
            gpuControl.Children.Add(gpuAuto);
            gpuControl.Children.Add(gpuSwatches);
            var gpu = SettingRow("Graphics card colour", "The colour of GPU readings and lines.", gpuControl);
            var highlights = SettingRow("Highlights", "How strongly the accent tints the selected menu item and the banners.", Track(HighlightHost));
            var size = SettingRow("Size", "Scales text and controls.", Track(SizeHost));
            var unit = SettingRow("Temperature unit", "The fan curves are always drawn in °C.", Detach(UnitTrack));

            var rows = new List<FrameworkElement> { theme, tone, accent, SettingRow("Custom colour", "Type a colour like #1e90ff to use it as the accent.", customControl), gpu, highlights, size, unit }.ToList();
            var card = SettingsCard("appearance", rows.ToArray());
            foreach (var box in appearanceBoxes) sections.Children.Remove(box);
            sections.Children.Insert(Math.Min(at, sections.Children.Count), card);
        }

        // General: the page Ember opens on and the switches that decide how it starts and closes.
        var generalBoxes = Boxes<GroupBox>("general");
        if (generalBoxes.Length > 0)
        {
            var at = sections.Children.IndexOf(generalBoxes[0]);
            var startTrack = Detach(StartPageTrack);
            var startup = Bare(StartupSwitch);
            var tray = Bare(TraySwitch);
            var minimised = Bare(MinimisedSwitch);
            var remember = Bare(RememberWindowSwitch);
            var card = SettingsCard("general",
                SettingRow("Start page", "The page Ember opens on.", startTrack, first: true),
                SettingRow("Open the window where I left it", "Remembers the size and position, including after a monitor change.", remember),
                SettingRow("Keep running in the tray", "Closing the window keeps Ember controlling fans and lights.", tray),
                SettingRow("Start with Windows", "Opens Ember when you sign in.", startup),
                SettingRow("Start minimised, in the tray", "The window stays closed until you open it from the tray.", minimised));
            foreach (var box in generalBoxes) sections.Children.Remove(box);
            sections.Children.Insert(Math.Min(at, sections.Children.Count), card);
            var note = Ui.Muted("This only opens the window. Fan control runs in the Ember service, which starts with Windows whether or not the window is open.", 12.5);
            note.Tag = "general";
            note.Margin = new Thickness(4, -4, 0, 16);
            sections.Children.Insert(sections.Children.IndexOf(card) + 1, note);
        }

        OnSettingsSection(SetGeneral, new RoutedEventArgs());
    }
}
