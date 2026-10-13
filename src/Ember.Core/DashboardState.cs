using System.Globalization;

namespace Ember.Core;

public sealed record FanBar(string Name, string RpmText, double Fraction)
{
    // WPF uses ToString() as the accessible name of each list item.
    public override string ToString() => $"{Name}, {RpmText}";
}

public sealed record DashboardView(
    string ConnectionText, string CpuText, string GpuText, string MotherboardText, string FanAverageText,
    IReadOnlyList<FanBar> Fans, string? DriverBannerText, bool ShowStaleNote,
    IReadOnlyList<(double X, double Y)> CpuLine, IReadOnlyList<(double X, double Y)> GpuLine,
    FanControlStatus? Control = null,
    string CpuSub = "", string GpuSub = "", string MotherboardSub = "", string FansSub = "");

/// <summary>
/// Turns connection state and snapshots into what the dashboard shows. Not thread-safe:
/// the app calls it only on the UI thread.
/// </summary>
public sealed class DashboardState
{
    public const string DriverBannerText =
        "Motherboard sensors and fan headers need the PawnIO driver. Run scripts/install-pawnio.ps1 as administrator, then restart the Ember service.";

    public static readonly TimeSpan ChartWindow = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(5);
    public const double ChartMinCelsius = 20, ChartMaxCelsius = 100;

    const string CpuId = "cpu", GpuId = "gpu";
    const string Dash = "—";

    // Snapshots arrive once per second; keep a little more than the 10-minute window.
    readonly HistoryBuffer _history = new(720);
    ConnectionState _state = ConnectionState.Connecting;
    Snapshot? _latest;
    DashboardSummary? _summary;

    public void SetConnection(ConnectionState state)
    {
        _state = state;
        // A new connection starts blank; old values must not look live.
        if (state != ConnectionState.Connected) { _latest = null; _summary = null; }
    }

    public void Add(Snapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _latest = snapshot;
        _summary = DashboardSummary.From(snapshot);
        var points = new List<SensorReading>(2);
        if (_summary.CpuTemp is { } cpu) points.Add(new(CpuId, CpuId, HardwareKind.Cpu, SensorKind.Temperature, cpu));
        if (_summary.GpuTemp is { } gpu) points.Add(new(GpuId, GpuId, HardwareKind.Gpu, SensorKind.Temperature, gpu));
        _history.Add(snapshot with { Readings = points }); // ignores stale snapshots
    }

    public DashboardView Render(DateTimeOffset now, double chartWidth, double chartHeight, TemperatureUnit unit)
    {
        IReadOnlyList<(double X, double Y)> Line(string id) => ChartGeometry.Scale(
            _history.Series(id), now, ChartWindow, chartWidth, chartHeight, ChartMinCelsius, ChartMaxCelsius);

        var connected = _state == ConnectionState.Connected;
        var s = connected ? _summary : null;
        var maxRpm = s is { Fans.Count: > 0 } ? s.Fans.Max(f => f.Rpm) : 0;
        // ponytail: bars are relative to the fastest fan; switch to per-fan max RPM once fans are profiled (phase 2).
        var fans = s?.Fans.Select(f => new FanBar(f.Name, Rpm(f.Rpm), maxRpm > 0 ? Math.Clamp(f.Rpm / maxRpm, 0, 1) : 0)).ToList()
                   ?? new List<FanBar>();

        return new DashboardView(
            _state switch
            {
                ConnectionState.Connected => "Service running",
                ConnectionState.Unavailable => "Service not running",
                _ => "Connecting…",
            },
            Temperature.Format(s?.CpuTemp, unit),
            Temperature.Format(s?.GpuTemp, unit),
            Temperature.Format(s?.MotherboardTemp, unit),
            s?.AverageFanRpm is { } avg ? Rpm(avg) : Dash,
            fans,
            connected && _latest?.Driver == DriverStatus.Missing ? DriverBannerText : null,
            connected && _latest is { } l && (l.Stale || now - l.Timestamp > StaleAfter), // also stale when the service stops sending (hung sensor read)
            Line(CpuId),
            Line(GpuId),
            connected ? _latest?.Control : null,
            Join(s?.CpuLoad is { } cl ? Pct(cl) + " load" : null, s?.CpuClockMhz is { } mhz and > 0 ? (mhz / 1000).ToString("0.0", CultureInfo.InvariantCulture) + " GHz" : null),
            s?.GpuLoad is { } gl ? Pct(gl) + " load" : "",
            Join(s?.VrmTemp is { } v ? "VRM " + Temperature.Format(v, unit) : null, s?.ChipsetTemp is { } c ? "chipset " + Temperature.Format(c, unit) : null),
            fans.Count(f => f.Fraction > 0) is var n and > 0 ? n + (n == 1 ? " fan spinning" : " fans spinning") : "");
    }

    static string Pct(double v) => Math.Round(v, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture) + "%";

    static string Join(params string?[] parts) => string.Join(" · ",parts.Where(p => !string.IsNullOrEmpty(p)));

    static string Rpm(double rpm) =>
        Math.Round(rpm, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture) + " RPM";
}
