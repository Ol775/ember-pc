using Ember.Lighting.Logitech;

namespace Ember.Lighting.Tests;

public class LogitechGKeysProbeTests
{
    sealed class Led(bool keysAccepted) : ILogitechLed
    {
        public bool Init() => true;
        public bool SetTargetDevice(LogitechTarget target) => true;
        public bool SetLighting(int r, int g, int b) => true;
        public bool SetLightingFromBitmap(byte[] bgra) => true;
        public bool SetLightingForKeyName(int keyName, int r, int g, int b) => keysAccepted;
        public void Shutdown() { }
        public void Dispose() { }
    }

    [Fact]
    public async Task The_G_keys_device_is_only_offered_on_a_keyboard_that_takes_G_key_names()
    {
        var withKeys = await new LogitechSource(() => new Led(true)).RefreshAsync(default);
        Assert.Contains(withKeys, s => s.Id == "logitech:gkeys");
        Assert.Contains(withKeys, s => s.Id == "logitech:keyboard");

        var without = await new LogitechSource(() => new Led(false)).RefreshAsync(default);
        Assert.DoesNotContain(without, s => s.Id == "logitech:gkeys");
        Assert.Contains(without, s => s.Id == "logitech:keyboard");
    }
}

public class LogitechDuplicateTests
{
    sealed class Sink(string id, string name, string kind) : IDeviceSink
    {
        public string Id => id; public string Name => name; public string Kind => kind;
        public int LedCount => 1; public TimeSpan MinInterval => TimeSpan.Zero; public bool Keepalive => false;
        public Task SendAsync(IReadOnlyList<Color> leds, CancellationToken ct) => Task.CompletedTask;
    }

    [Fact]
    public void A_keyboard_driven_through_G_HUB_is_not_listed_again_from_OpenRGB()
    {
        var list = new List<IDeviceSink>
        {
            new Sink("logitech:keyboard", "Logitech keyboard", "Keyboard"),
            new Sink("openrgb:G915 X LS|HID", "G915 X LS", "Keyboard"),
            new Sink("openrgb:Other|HID", "Corsair K70", "Keyboard"),
            new Sink("openrgb:MSI|HID", "MSI B550", "Motherboard"),
        };
        var kept = LightingManager.WithoutDuplicates(list).Select(s => s.Id).ToList();
        Assert.DoesNotContain("openrgb:G915 X LS|HID", kept);
        Assert.Contains("openrgb:Other|HID", kept);
        Assert.Contains("openrgb:MSI|HID", kept);
    }

    [Fact]
    public void Without_G_HUB_the_OpenRGB_keyboard_stays()
    {
        var list = new List<IDeviceSink> { new Sink("openrgb:G915 X LS|HID", "G915 X LS", "Keyboard") };
        Assert.Single(LightingManager.WithoutDuplicates(list));
    }
}
