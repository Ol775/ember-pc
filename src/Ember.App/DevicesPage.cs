using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Ember.Core;
using Ember.Lighting;
using static Ember.App.LightsUi;

namespace Ember.App;

/// <summary>
/// Devices and integrations: one card per source (sensors and fans, OpenRGB, Govee) and a table of every device Ember
/// knows about. It only reads state; nothing here changes hardware. Rebuilt only when what it shows changes.
/// </summary>
public sealed class DevicesPage : UserControl
{
    readonly StackPanel _root = new() { Margin = new Thickness(24) };
    readonly UniformGrid _cards = new() { Columns = 2, Margin = new Thickness(-6, 0, -6, 10) };
    readonly StackPanel _rows = new();
    string? _key;

    public DevicesPage()
    {
        Content = _root;
        Focusable = false;
        var title = new TextBlock { Text = "Devices and integrations", FontSize = 22, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 16) };
        AutomationProperties.SetHeadingLevel(title, AutomationHeadingLevel.Level2);
        _root.Children.Add(title);
        _root.Children.Add(_cards);
        _root.Children.Add(Panel("All devices", _rows, new Thickness(0)));
        ThemeManager.Changed += () => _key = null;
    }

    public void Update(MainViewModel vm, LightingManager lights)
    {
        var headers = vm.FansPage.Headers;
        var devices = lights.Devices;
        var status = lights.SourceStatus;
        var key = vm.FansPage.ServiceAvailable + "|" + string.Join(";", headers.Select(h => h.Id + h.Name + h.StateText)) + "#"
            + string.Join(";", devices.Select(d => d.Id + d.Name + d.Source)) + "#" + string.Join(";", status)
            + "#" + string.Join(";", lights.Problems.Select(p => p.Key + p.Value));
        if (key == _key) return;
        _key = key;

        var controllable = headers.Count(h => h.CanDisable);
        _cards.Children.Clear();
        _cards.Children.Add(Card("Sensors and fans", vm.FansPage.ServiceAvailable ? "Connected" : "Service not running", vm.FansPage.ServiceAvailable,
            "LibreHardwareMonitor, runs in the Ember service",
            $"{headers.Count} fan headers shown, {controllable} controlled by Ember"));
        _cards.Children.Add(Source("OpenRGB", "SDK server on localhost, started with Ember", devices, status));
        _cards.Children.Add(Source("Govee", "LAN first; cloud only with your API key", devices, status));
        _cards.Children.Add(Source("Logitech", "Keyboards and mice, through G HUB", devices, status));

        _rows.Children.Clear();
        _rows.Children.Add(Row("Name", "Source", "Type", "Status", header: true));
        foreach (var d in devices.OrderBy(d => d.Source).ThenBy(d => d.Name))
        {
            var problem = lights.Problems.TryGetValue(d.Id, out var p) ? p : null;
            _rows.Children.Add(Row(d.Name, d.Source, "Lighting", problem ?? (d.Realtime ? "Online" : "Online, slow (cloud)"), warn: problem is not null || !d.Realtime));
        }
        if (_rows.Children.Count == 1) _rows.Children.Add(Text("Nothing found yet", muted: true));
    }

    static FrameworkElement Source(string name, string blurb, IReadOnlyList<DeviceInfo> devices, IReadOnlyList<string> status)
    {
        var mine = devices.Where(d => d.Source.Contains(name, StringComparison.OrdinalIgnoreCase)).ToList();
        var line = status.FirstOrDefault(s => s.StartsWith(name, StringComparison.OrdinalIgnoreCase));
        var detail = line is null ? "Not available" : line[(name.Length + 1)..].Trim(' ', ':');
        return Card(name, mine.Count > 0 ? "Connected" : "No devices", mine.Count > 0, blurb,
            mine.Count > 0 ? $"{mine.Count} {(mine.Count == 1 ? "device" : "devices")}: {string.Join(", ", mine.Take(4).Select(d => d.Name))}" : detail);
    }

    static FrameworkElement Card(string title, string pill, bool good, string blurb, string detail)
    {
        var head = new DockPanel();
        var chip = new Border
        {
            CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 2, 10, 2),
            Background = good ? ThemeManager.GoodSoft : (Brush)Application.Current.FindResource("AccentSoftBrush"),
            Child = new TextBlock { Text = pill, FontSize = 12, Foreground = good ? ThemeManager.Good : (Brush)Application.Current.FindResource("AccentTextBrush") },
        };
        DockPanel.SetDock(chip, Dock.Right);
        head.Children.Add(chip);
        head.Children.Add(Text(title, weight: FontWeights.SemiBold, size: 15));
        var body = new StackPanel();
        body.Children.Add(head);
        body.Children.Add(WithMargin(Text(blurb, muted: true, size: 13), 0, 8, 0, 0));
        body.Children.Add(WithMargin(Text(detail, size: 13), 0, 6, 0, 0));
        var box = Panel(title, body, new Thickness(6));
        box.Header = null; // the card shows its own title
        return box;
    }

    static FrameworkElement Row(string a, string b, string c, string d, bool header = false, bool warn = false)
    {
        var g = new Grid { Margin = new Thickness(0, header ? 0 : 6, 0, 0) };
        foreach (var w in new[] { 2.0, 1.2, 1.0, 1.4 }) g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(w, GridUnitType.Star) });
        var cells = new[] { a, b, c, d };
        for (var i = 0; i < 4; i++)
        {
            var t = Text(cells[i], muted: header || i is 1 or 2, size: header ? 12 : 14);
            if (i == 3 && !header)
                t.Foreground = warn ? (Brush)Application.Current.FindResource("AccentTextBrush") : ThemeManager.Good;
            Grid.SetColumn(t, i);
            g.Children.Add(t);
        }
        return g;
    }

    static T WithMargin<T>(T e, double l, double t, double r, double b) where T : FrameworkElement { e.Margin = new Thickness(l, t, r, b); return e; }
}
