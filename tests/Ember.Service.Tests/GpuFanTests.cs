using Ember.Core;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ember.Service.Tests;

/// <summary>
/// A graphics card fan only holds a steady speed from about 60% (measured on an RTX 3060: at 40 to 50% it stops and starts), so
/// Ember drives it at 60% or more and, when the curve asks for less, gives it back to the card's own control.
/// </summary>
public sealed class GpuFanTests : IDisposable
{
    const string Gpu = "/gpu-nvidia/0/control/1";
    static readonly FanControlIdentity Id = new("MS-7C56", "0.9.6");

    readonly string _dir = Path.Combine(Path.GetTempPath(), "ember-gpu-" + Guid.NewGuid().ToString("N"));
    readonly FakeFanController _fans = new(new FanHeader(Gpu, "GPU Fan", true, true, false));
    readonly ManualClock _clock = new();

    public GpuFanTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, true);

    FanControlLoop Loop()
    {
        var store = new SettingsStore(Path.Combine(_dir, "settings.json"));
        store.Save(EmberSettings.Default with
        {
            Experimental = true,
            GpuControlEnabled = true,
            Headers = [new HeaderRecord(Gpu, true, "Case", "MS-7C56", "0.9.6", 50)],
        });
        return new FanControlLoop(new SnapshotHub(), _fans, store, () => [], _clock, NullLogger<FanControlLoop>.Instance, Id);
    }

    static Snapshot Snap(double cpu, double? gpu) => new(DateTimeOffset.UtcNow,
        [
            new("cpu", "Core (Tctl/Tdie)", HardwareKind.Cpu, SensorKind.Temperature, cpu),
            .. gpu is { } g ? new[] { new SensorReading("gpu", "GPU Core", HardwareKind.Gpu, SensorKind.Temperature, g) } : [],
        ], false);

    IEnumerable<double> Written() => _fans.Calls.Where(c => c.Op == "set" && c.Id == Gpu).Select(c => c.Value!.Value);

    [Fact]
    public void A_cool_card_asking_for_little_is_left_to_its_own_control()
    {
        var loop = Loop();
        loop.OnSnapshot(Snap(40, 45));
        Assert.Contains(_fans.Calls, c => c is ("auto", Gpu, _));
        Assert.Empty(Written());
    }

    [Fact]
    public void The_card_is_handed_back_once_not_every_step()
    {
        var loop = Loop();
        for (var i = 0; i < 4; i++) loop.OnSnapshot(Snap(40, 45));
        Assert.Single(_fans.Calls, c => c is ("auto", Gpu, _));
    }

    [Fact]
    public void A_hot_card_is_always_driven_and_never_below_60_percent()
    {
        var loop = Loop();
        loop.OnSnapshot(Snap(40, 45));      // handed to the card first
        loop.OnSnapshot(Snap(40, 80));
        Assert.NotEmpty(Written());
        Assert.All(Written(), v => Assert.True(v >= 60, $"{v}% is below what the card's fan can hold"));
        Assert.Equal(1, _fans.Calls.Count(c => c is ("auto", Gpu, _)));
    }

    [Fact]
    public void A_card_whose_temperature_is_unknown_is_driven_not_trusted_to_itself()
    {
        var loop = Loop();
        loop.OnSnapshot(Snap(50, null));
        Assert.DoesNotContain(_fans.Calls, c => c is ("auto", Gpu, _));
        Assert.NotEmpty(Written());
        Assert.All(Written(), v => Assert.True(v >= 60));
    }

    [Fact]
    public void A_louder_mode_takes_the_fan_back_from_the_card_and_a_quieter_one_gives_it_back_again()
    {
        var loop = Loop();
        loop.OnSnapshot(Snap(40, 45));
        Assert.Empty(Written());

        loop.Handle(new SetMode(FanMode.Performance));
        loop.OnSnapshot(Snap(60, 65));
        Assert.NotEmpty(Written());
        Assert.All(Written(), v => Assert.True(v >= 60));

        var autoCalls = _fans.Calls.Count(c => c is ("auto", Gpu, _));
        loop.Handle(new SetMode(FanMode.SuperQuiet));
        loop.OnSnapshot(Snap(40, 45));
        Assert.Equal(autoCalls + 1, _fans.Calls.Count(c => c is ("auto", Gpu, _)));
    }

    [Fact]
    public void A_fan_reading_zero_rpm_on_a_graphics_card_is_not_kicked_to_full_speed()
    {
        var loop = Loop();
        // It was seen turning once, then sits at 0 RPM while the curve is low: the card's own zero-RPM mode, not a stall.
        loop.OnSnapshot(new Snapshot(DateTimeOffset.UtcNow,
            [new("cpu", "Core (Tctl/Tdie)", HardwareKind.Cpu, SensorKind.Temperature, 40), new("gpu", "GPU Core", HardwareKind.Gpu, SensorKind.Temperature, 45),
             new("/gpu-nvidia/0/fan/1", "GPU Fan", HardwareKind.Gpu, SensorKind.FanRpm, 1400)], false));
        for (var i = 0; i < 6; i++)
        {
            _clock.Advance(10);
            loop.OnSnapshot(new Snapshot(DateTimeOffset.UtcNow,
                [new("cpu", "Core (Tctl/Tdie)", HardwareKind.Cpu, SensorKind.Temperature, 40), new("gpu", "GPU Core", HardwareKind.Gpu, SensorKind.Temperature, 45),
                 new("/gpu-nvidia/0/fan/1", "GPU Fan", HardwareKind.Gpu, SensorKind.FanRpm, 0)], false));
        }
        Assert.DoesNotContain(Written(), v => v >= 100);
    }

    [Fact]
    public void Switching_gpu_control_off_while_the_card_is_driven_gives_it_back_for_real()
    {
        var loop = Loop();
        loop.OnSnapshot(Snap(40, 80));
        Assert.NotEmpty(Written());
        loop.Handle(new SetGpuControl(false));
        Assert.Contains(_fans.Calls, c => c is ("release", Gpu, _));
    }
}
