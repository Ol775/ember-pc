using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Ember.Lighting;
using Ember.Lighting.OpenRgb;
using Color = Ember.Lighting.Color;

namespace Ember.App.Tests;

/// <summary>The Lights page against made-up devices that only collect their colours: no hardware is involved.</summary>
public class LightsPageTests
{
    sealed class FakeSink(string id, string name, string kind, int leds, int columns = 0) : IDeviceSink
    {
        public string Id => id;
        public string Name => name;
        public string Kind => kind;
        public int LedCount => leds;
        public int Columns => columns;
        public TimeSpan MinInterval => TimeSpan.Zero;
        public bool Keepalive => false;
        public int Sends;
        public Task SendAsync(IReadOnlyList<Color> colours, CancellationToken ct) { Interlocked.Increment(ref Sends); return Task.CompletedTask; }
    }

    sealed class FakeSource(params IDeviceSink[] sinks) : IDeviceSource
    {
        public string Name => "Test devices";
        public string Status => "Made up for a test";
        public Task<IReadOnlyList<IDeviceSink>> RefreshAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<IDeviceSink>>(sinks);
        public void ReportFailure(string sinkId) { }
    }

    static (LightingManager Manager, FakeSink[] Sinks) Manager(string dir)
    {
        var sinks = new[]
        {
            new FakeSink("kb", "Keyboard", "Keyboard", 90, 15), new FakeSink("ram", "RAM", "Memory", 8), new FakeSink("strip", "LED strip", "Strip", 30), new FakeSink("fans", "Fans", "Fan", 12),
        };
        var layers = new List<Layer>
        {
            new("aurora", "Aurora wave", GradientEffect.Default, 1, BlendMode.Normal),
            new("keys", "Reactive keys", new ReactiveKeysEffect(new Color(1, 1, 1), new Color(1, 0.3, 0.05)), 1, BlendMode.Add, true, ["kb"]),
            new("temp", "CPU temperature", new TemperatureGlowEffect(TempSensor.Cpu, new Color(0.1, 0.4, 1), new Color(1, 0.1, 0.05)), 1, BlendMode.Screen, false),
        };
        var scene = new Scene("Aurora", layers, []);
        var store = new LightingStore(Path.Combine(dir, "lighting.json"));
        store.TrySave(LightingState.Default with { On = true, Scenes = [scene, new Scene("Static amber", [new Layer("a", "Amber", new StaticEffect(new Color(1, 0.7, 0.2)))], [])], CurrentScene = "Aurora" });
        var manager = new LightingManager(store, TimeProvider.System, [new FakeSource(sinks)]);
        return (manager, sinks);
    }

    static void Save(FrameworkElement element, string name)
    {
        if (Environment.GetEnvironmentVariable("EMBER_UI_SHOTS") is not { Length: > 0 } dir) return;
        Directory.CreateDirectory(dir);
        element.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)element.ActualWidth, (int)element.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        var backdrop = new DrawingVisual();
        using (var dc = backdrop.RenderOpen()) dc.DrawRectangle((Brush)Application.Current.FindResource("WindowBrush"), null, new Rect(0, 0, element.ActualWidth, element.ActualHeight));
        bitmap.Render(backdrop);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(dir, name + ".png"));
        encoder.Save(file);
    }


    static T? Find<T>(DependencyObject root, Func<T, bool> match) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T t && match(t)) return t;
            if (Find(child, match) is { } deeper) return deeper;
        }
        return null;
    }

    static void Click(FrameworkElement root, string automationName)
    {
        var button = Find<System.Windows.Controls.Primitives.ButtonBase>(root, b => System.Windows.Automation.AutomationProperties.GetName(b) == automationName)
            ?? throw new InvalidOperationException("No button named " + automationName);
        button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
    }

    [UiFact]
    public void The_lights_page_shows_the_gradient_editor_and_works_with_solo_and_targets()
    {
        _ = UiHost.Window;   // starts the simulation (the application and its styles)
        var dir = Path.Combine(UiHost.DataDir, "lights-test");
        Directory.CreateDirectory(dir);
        var (manager, sinks) = Manager(dir);
        LightsPage? page = null;
        Window? host = null;
        try
        {
            UiHost.Run(() =>
            {
                page = new LightsPage();
                page.Attach(manager);
                host = new Window { Content = page, Width = 1280, Height = 1500, Left = -3000, Top = 0, ShowInTaskbar = false, ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual };
                host.SetResourceReference(Window.BackgroundProperty, "WindowBrush");
                host.Show();
            });
            manager.Start();
            manager.RefreshDevices();
            for (var i = 0; i < 100 && manager.Devices.Count < 4; i++) Thread.Sleep(50);
            Assert.Equal(4, manager.Devices.Count);
            manager.Arrange();
            Thread.Sleep(600);
            UiHost.Settle();
            UiHost.Run(() => Save(host!, "Lights-aurora"));

            // Every device on the canvas got its colours; the Aurora layer paints them all.
            Assert.All(sinks, s => Assert.True(s.Sends > 0, s.Id + " was not sent anything"));


            // The editor of a gradient: pick the layer in the list.
            UiHost.Run(() => Click(page!, "Edit layer Aurora wave"));
            UiHost.Settle();
            UiHost.Run(() => Save(host!, "Lights-gradient"));

            // The Solo button plays the chosen layer alone, and a second press brings the rest back.
            UiHost.Run(() => Click(page!, "Play only the chosen layer on the lights, to see what it does on its own"));
            Assert.Equal("aurora", manager.SoloLayer);
            UiHost.Run(() => Click(page!, "Play only the chosen layer on the lights, to see what it does on its own"));
            Assert.Null(manager.SoloLayer);

            // Applies to: add RAM to the devices the reactive-keys layer is drawn on.
            UiHost.Run(() => Click(page!, "Edit layer Reactive keys"));
            UiHost.Settle();
            UiHost.Run(() => Click(page!, "Draw Reactive keys on RAM"));
            Assert.Equal(["kb", "ram"], manager.State.Current.Layers.First(l => l.Id == "keys").Targets!.OrderBy(x => x));
            UiHost.Run(() => Click(page!, "Stop drawing Reactive keys on RAM"));
            UiHost.Run(() => Click(page!, "Stop drawing Reactive keys on Keyboard"));   // the last device cannot be removed
            Assert.Equal(["kb"], manager.State.Current.Layers.First(l => l.Id == "keys").Targets!);

            // Solo: only the chosen layer is drawn (the reactive-keys layer paints only the keyboard, so the strip goes dark).
            manager.SoloLayer = "keys";
            Thread.Sleep(500);
            var strip = manager.Preview["strip"];
            Assert.All(strip, c => Assert.Equal(Color.Black, c));
            manager.SoloLayer = null;
            Thread.Sleep(500);
            Assert.Contains(manager.Preview["strip"], c => c != Color.Black);

            // Targets: the reactive-keys layer is drawn on the keyboard only, whatever else is on the canvas.
            var layer = manager.State.Current.Layers.First(l => l.Id == "keys");
            Assert.True(layer.Covers("kb"));
            Assert.False(layer.Covers("strip"));
            UiHost.ThrowIfErrors();
        }
        finally
        {
            UiHost.Run(() => host?.Close());
            manager.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(5));
        }
    }
}
