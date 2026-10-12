namespace Ember.Core.Tests;

public class DashboardStateTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static SensorReading R(string name, HardwareKind h, SensorKind k, double v) =>
        new($"{h}/{name}", name, h, k, v);

    private static Snapshot Snap(DateTimeOffset t, double cpu = 50, double gpu = 60, bool stale = false,
        DriverStatus driver = DriverStatus.Ok) =>
        new(t, new[]
        {
            R("CPU Package", HardwareKind.Cpu, SensorKind.Temperature, cpu),
            R("GPU Core", HardwareKind.Gpu, SensorKind.Temperature, gpu),
            R("System", HardwareKind.Motherboard, SensorKind.Temperature, 40),
            R("Fan 1", HardwareKind.Motherboard, SensorKind.FanRpm, 1000),
            R("Fan 2", HardwareKind.Motherboard, SensorKind.FanRpm, 500),
        }, stale, driver);

    private static DashboardState Connected(params Snapshot[] snaps)
    {
        var s = new DashboardState();
        s.SetConnection(ConnectionState.Connected);
        foreach (var x in snaps) s.Add(x);
        return s;
    }

    private static DashboardView View(DashboardState s, TemperatureUnit u = TemperatureUnit.Celsius) =>
        s.Render(T0, 600, 80, u);

    [Theory]
    [InlineData(ConnectionState.Connected, "Service running")]
    [InlineData(ConnectionState.Unavailable, "Service not running")]
    [InlineData(ConnectionState.Connecting, "Connecting…")]
    public void Connection_text(ConnectionState state, string expected)
    {
        var s = new DashboardState();
        s.SetConnection(state);
        Assert.Equal(expected, View(s).ConnectionText);
    }

    [Fact]
    public void Initial_state_is_connecting() => Assert.Equal("Connecting…", View(new DashboardState()).ConnectionText);

    [Fact]
    public void Connected_snapshot_fills_cards_and_fans()
    {
        var v = View(Connected(Snap(T0)));
        Assert.Equal("50°C", v.CpuText);
        Assert.Equal("60°C", v.GpuText);
        Assert.Equal("40°C", v.MotherboardText);
        Assert.Equal("750 RPM", v.FanAverageText);
        Assert.Equal(new[] { "Fan 1", "Fan 2" }, v.Fans.Select(f => f.Name));
        Assert.Equal(new[] { "1000 RPM", "500 RPM" }, v.Fans.Select(f => f.RpmText));
        Assert.Equal(new[] { 1.0, 0.5 }, v.Fans.Select(f => f.Fraction));
    }

    [Fact]
    public void Fahrenheit_unit_is_applied() => Assert.Equal("122°F", View(Connected(Snap(T0)), TemperatureUnit.Fahrenheit).CpuText);

    [Fact]
    public void Missing_values_render_as_dash_not_zero()
    {
        var v = View(Connected(new Snapshot(T0, Array.Empty<SensorReading>(), false)));
        Assert.Equal("—", v.CpuText);
        Assert.Equal("—", v.GpuText);
        Assert.Equal("—", v.MotherboardText);
        Assert.Equal("—", v.FanAverageText);
        Assert.Empty(v.Fans);
    }

    [Fact]
    public void Connected_without_snapshot_shows_dashes() => Assert.Equal("—", View(Connected()).CpuText);

    [Theory]
    [InlineData(ConnectionState.Unavailable)]
    [InlineData(ConnectionState.Connecting)]
    public void Not_connected_shows_dashes_and_no_fans_or_banner(ConnectionState state)
    {
        var s = Connected(Snap(T0, driver: DriverStatus.Missing, stale: true));
        s.SetConnection(state);
        var v = View(s);
        Assert.Equal("—", v.CpuText);
        Assert.Equal("—", v.FanAverageText);
        Assert.Empty(v.Fans);
        Assert.Null(v.DriverBannerText);
        Assert.False(v.ShowStaleNote);
    }

    [Fact]
    public void Reconnect_does_not_resurrect_old_snapshot()
    {
        var s = Connected(Snap(T0));
        s.SetConnection(ConnectionState.Unavailable);
        s.SetConnection(ConnectionState.Connected);
        Assert.Equal("—", View(s).CpuText);
    }

    [Fact]
    public void Driver_missing_shows_exact_banner() =>
        Assert.Equal("Motherboard sensors and fan headers need the PawnIO driver. Run scripts/install-pawnio.ps1 as administrator, then restart the Ember service.",
            View(Connected(Snap(T0, driver: DriverStatus.Missing))).DriverBannerText);

    [Theory]
    [InlineData(DriverStatus.Ok)]
    [InlineData(DriverStatus.Unknown)]
    public void Driver_ok_or_unknown_shows_no_banner(DriverStatus d) =>
        Assert.Null(View(Connected(Snap(T0, driver: d))).DriverBannerText);

    [Fact]
    public void Banner_follows_latest_snapshot() =>
        Assert.Null(View(Connected(Snap(T0, driver: DriverStatus.Missing), Snap(T0, driver: DriverStatus.Ok))).DriverBannerText);

    [Fact]
    public void Stale_snapshot_shows_note_and_skips_history()
    {
        var v = View(Connected(Snap(T0.AddSeconds(-1), stale: true)));
        Assert.True(v.ShowStaleNote);
        Assert.Empty(v.CpuLine);
        Assert.Empty(v.GpuLine);
        Assert.False(View(Connected(Snap(T0))).ShowStaleNote);
    }

    [Fact]
    public void Old_snapshot_shows_stale_note_even_if_service_says_fresh()
    {
        var s = Connected(Snap(T0.AddSeconds(-6)));
        Assert.True(View(s).ShowStaleNote);
        Assert.False(s.Render(T0.AddSeconds(-4), 600, 80, TemperatureUnit.Celsius).ShowStaleNote);
    }

    [Fact]
    public void History_lines_use_summary_values_on_20_to_100_scale_over_10_minutes()
    {
        var s = Connected(Snap(T0.AddMinutes(-10), cpu: 100, gpu: 20), Snap(T0, cpu: 60, gpu: 150));
        var v = View(s);
        Assert.Equal(new[] { (0d, 0d), (600d, 40d) }, v.CpuLine);
        Assert.Equal(new[] { (0d, 80d), (600d, 0d) }, v.GpuLine);
    }

    [Fact]
    public void History_drops_points_older_than_window() =>
        Assert.Single(View(Connected(Snap(T0.AddMinutes(-11)), Snap(T0))).CpuLine);

    [Fact]
    public void History_survives_disconnect()
    {
        var s = Connected(Snap(T0));
        s.SetConnection(ConnectionState.Unavailable);
        Assert.Single(View(s).CpuLine);
    }

    [Fact]
    public void Snapshot_without_gpu_adds_no_gpu_point()
    {
        var snap = new Snapshot(T0, new[] { R("CPU Package", HardwareKind.Cpu, SensorKind.Temperature, 50) }, false);
        var v = View(Connected(snap));
        Assert.Single(v.CpuLine);
        Assert.Empty(v.GpuLine);
    }

    [Fact]
    public void All_zero_fans_give_zero_fraction() =>
        Assert.Equal(0, View(Connected(new Snapshot(T0,
            new[] { R("Fan 1", HardwareKind.Motherboard, SensorKind.FanRpm, 0) }, false))).Fans[0].Fraction);
}

public class DashboardControlTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private static Snapshot Snap() => new(T0, Array.Empty<SensorReading>(), false, DriverStatus.Ok,
        new FanControlStatus(FanMode.Normal, ModeSource.User, false, null, Array.Empty<HeaderStatus>(), Array.Empty<string>(), null));

    [Fact]
    public void Control_status_is_exposed_when_connected_and_cleared_when_not()
    {
        var s = new DashboardState();
        s.SetConnection(ConnectionState.Connected);
        s.Add(Snap());
        Assert.NotNull(s.Render(T0, 600, 80, TemperatureUnit.Celsius).Control);
        s.SetConnection(ConnectionState.Unavailable);
        Assert.Null(s.Render(T0, 600, 80, TemperatureUnit.Celsius).Control);
    }
}
