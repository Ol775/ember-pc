using System.Windows;
using System.Windows.Controls;
using Ember.Core;

namespace Ember.App;

/// <summary>Settings, Legal and About.</summary>
public partial class MainWindow
{
    const string RepoUrl = "https://github.com/Ol775/ember-pc";

    static void OpenLink(string url)
    {
        // Only addresses of this project on GitHub are ever opened from these pages.
        if (!url.StartsWith(RepoUrl, StringComparison.Ordinal)) return;
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception) { /* no browser to open */ }
    }

    static Button LinkButton(string label, string url)
    {
        var b = new Button { Content = label, Style = LightsUi.Sty("ActionButton"), Margin = new Thickness(0, 0, 8, 8) };
        b.Click += (_, _) => OpenLink(url);
        return b;
    }

    /// <summary>A rounded card in the colours of a banner, for the short version of a long page.</summary>
    static Border Card(UIElement content)
    {
        var card = new Border { CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), Padding = new Thickness(16, 12, 16, 12), Margin = new Thickness(0, 0, 0, 14), Child = content };
        card.SetResourceReference(Border.BackgroundProperty, "BannerBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "PanelBorderBrush");
        return card;
    }

    static StackPanel Bullets(params string[] items)
    {
        var list = new StackPanel();
        foreach (var item in items)
        {
            var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
            var dot = LightsUi.Text("•", muted: true);
            dot.Margin = new Thickness(2, 0, 8, 0);
            dot.VerticalAlignment = VerticalAlignment.Top;
            DockPanel.SetDock(dot, Dock.Left);
            row.Children.Add(dot);
            var text = LightsUi.Text(item);
            text.TextWrapping = TextWrapping.Wrap;
            row.Children.Add(text);
            list.Children.Add(row);
        }
        return list;
    }

    /// <summary>Settings, Legal: the short version first, then the terms, the privacy notice, the third-party notices and the licence.</summary>
    void BuildLegal()
    {
        var summary = new StackPanel();
        summary.Children.Add(LightsUi.Text("In short", size: 15, weight: FontWeights.SemiBold));
        summary.Children.Add(Bullets(
            "Ember is free and open source under the MIT Licence.",
            "It controls real fans and lights: you are responsible for how you use it, and it comes without a warranty.",
            "It has no accounts and no telemetry. The only network use is what you switch on: update checks, Govee cloud, Hue and Nanoleaf on your own network.",
            "Everything below is the full text, kept inside the app so it stays with the version you run."));
        LegalHost.Children.Add(Card(summary));

        var accepted = Math.Max(_vm.Prefs.AcceptedTerms, TermsAcceptance.ByInstaller());
        var status = LightsUi.Text(accepted >= Legal.TermsVersion
            ? $"You have accepted version {Legal.TermsVersion} of the Terms of Use, the current one."
            : $"The current Terms of Use are version {Legal.TermsVersion}; Ember asks for your agreement when it starts.", muted: true, size: 12);
        status.Margin = new Thickness(0, 0, 0, 10);
        LegalHost.Children.Add(status);

        var docs = new (string Label, string Resource, string File, bool Plain)[]
        {
            ("Terms of use", "TERMS.md", "TERMS.md", false), ("Privacy", "PRIVACY.md", "PRIVACY.md", false),
            ("Third-party notices", "THIRD-PARTY.md", "THIRD-PARTY.md", false), ("Licence", "LICENSE.txt", "LICENSE", true),
        };
        var body = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
        var tools = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
        Action<bool> setAll = _ => { };
        string current = docs[0].File;

        var expand = new Button { Content = "Expand all", Style = LightsUi.Sty("ActionButton"), Margin = new Thickness(0, 0, 8, 8) };
        var collapse = new Button { Content = "Collapse all", Style = LightsUi.Sty("ActionButton"), Margin = new Thickness(0, 0, 8, 8) };
        expand.Click += (_, _) => setAll(true);
        collapse.Click += (_, _) => setAll(false);
        var github = new Button { Content = "Read on GitHub", Style = LightsUi.Sty("ActionButton"), Margin = new Thickness(0, 0, 8, 8) };
        github.Click += (_, _) => OpenLink(RepoUrl + "/blob/main/" + current);
        tools.Children.Add(expand);
        tools.Children.Add(collapse);
        tools.Children.Add(github);

        void Show(int i)
        {
            var (_, resource, file, plain) = docs[i];
            current = file;
            var text = DocView.ReadEmbedded(resource);
            expand.Visibility = collapse.Visibility = plain ? Visibility.Collapsed : Visibility.Visible;
            if (!plain)
            {
                // The introduction stays visible; each numbered section folds, the first one open.
                setAll = DocView.FillFolding(body, text, (index, _) => index == 0, _ => "");
                return;
            }
            setAll = _ => { };
            body.Children.Clear();
            var t = new TextBlock { Text = text ?? "Nothing to show.", TextWrapping = TextWrapping.Wrap };
            t.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            body.Children.Add(t);
        }
        LegalHost.Children.Add(LightsUi.Segments(docs.Select(d => d.Label).ToList(), 0, Show, "LegalDoc"));
        LegalHost.Children.Add(tools);
        LegalHost.Children.Add(body);
        Show(0);
    }

    /// <summary>Settings, About: what Ember is, which build this is, where to find it, and what it stands on.</summary>
    void BuildAbout()
    {
        var title = new StackPanel { Orientation = Orientation.Horizontal };
        title.Children.Add(LightsUi.Text("Ember", size: 26, weight: FontWeights.SemiBold));
        var chip = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        chip.SetResourceReference(Border.BackgroundProperty, "AccentSoftBrush");
        var chipText = LightsUi.Text(_vm.BuildLabel, size: 12, weight: FontWeights.SemiBold);
        chipText.SetResourceReference(TextBlock.ForegroundProperty, "AccentTextBrush");
        chip.Child = chipText;
        title.Children.Add(chip);
        AboutHost.Children.Add(title);

        var version = LightsUi.Text("Version " + AppInfo.Version, muted: true);
        version.Margin = new Thickness(0, 4, 0, 10);
        AboutHost.Children.Add(version);

        var about = LightsUi.Text("Ember watches your PC's temperatures and fans, controls the fans on supported motherboards and graphics cards, and runs the lighting of your RGB devices from one place. It runs on your PC only: no account, no telemetry.");
        about.TextWrapping = TextWrapping.Wrap;
        AboutHost.Children.Add(about);

        var links = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
        links.Children.Add(LinkButton("Project page", RepoUrl));
        links.Children.Add(LinkButton("All releases", RepoUrl + "/releases"));
        links.Children.Add(LinkButton("Report a problem", RepoUrl + "/issues"));
        var copy = new Button { Content = "Copy version details", Style = LightsUi.Sty("ActionButton"), Margin = new Thickness(0, 0, 8, 8) };
        copy.Click += (_, _) =>
        {
            var text = $"Ember {AppInfo.Version} ({_vm.BuildLabel}, {_vm.Prefs.Channel} channel)\n{_vm.ConnectionText}\nWindows {Environment.OSVersion.Version}, .NET {Environment.Version}, " +
                       (_vm.IsElevated ? "administrator" : "standard user");
            try { Clipboard.SetText(text); AboutNote.Text = "Copied. Paste it into a problem report."; }
            catch (Exception) { AboutNote.Text = "Windows would not let Ember use the clipboard."; }
            Announce(AboutNote);
        };
        links.Children.Add(copy);
        AboutHost.Children.Add(links);
        AboutNote = LightsUi.Text("", muted: true, size: 12);
        AboutHost.Children.Add(AboutNote);

        var built = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
        built.Children.Add(LightsUi.Text("Built on", size: 15, weight: FontWeights.SemiBold));
        var list = Bullets(
            "LibreHardwareMonitor for sensors and fan control (MPL-2.0)",
            "PawnIO, the signed driver that lets Ember reach the motherboard chips",
            "OpenRGB for lighting hardware, run as a separate program (GPL-2.0)",
            "Logitech G HUB, Govee, Philips Hue, Nanoleaf and Razer Synapse for their own devices");
        list.Margin = new Thickness(0, 6, 0, 0);
        built.Children.Add(list);
        var more = LightsUi.Text("Full notices and licences are under Legal. Ember is an independent project by Ol775, not affiliated with or endorsed by any of these companies.", muted: true, size: 12);
        more.TextWrapping = TextWrapping.Wrap;
        more.Margin = new Thickness(0, 8, 0, 0);
        built.Children.Add(more);
        AboutHost.Children.Add(built);
    }

    TextBlock AboutNote = null!;
}
