using Ember.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Ember.Service;

/// <summary>Which board and library version a header test result belongs to; a different value means "test again".</summary>
public sealed record FanControlIdentity(string Board, string LhmVersion);

/// <summary>
/// One control step per snapshot: safety rules, then the curve of the effective mode, then the per-header floor,
/// then the write. Only headers that are enabled, have a passing test for this board and library version, and have
/// no competing app running are ever written. On stop every header Ember wrote is handed back to the board.
/// </summary>
public sealed class FanControlLoop : BackgroundService
{
    enum Group { CpuPump, Case }

    static readonly TempSource[] AllSources = [TempSource.Cpu, TempSource.Gpu, TempSource.Motherboard];
    static readonly TimeSpan ScanEvery = TimeSpan.FromSeconds(5);
    const double Floor = 20, PumpFloor = 60;

    readonly SnapshotHub _hub;
    readonly IFanController _fans;
    readonly SettingsStore _store;
    readonly Func<IReadOnlyList<string>> _processNames;
    readonly TimeProvider _clock;
    readonly ILogger<FanControlLoop> _log;
    readonly FanControlIdentity _identity;

    readonly object _gate = new();
    readonly SafetyMonitor _safety = new();
    readonly Dictionary<Group, CurveEngine> _engines = new() { [Group.CpuPump] = new(), [Group.Case] = new() };
    readonly Dictionary<Group, double> _lastPercent = new();
    readonly HashSet<TempSource> _seen = new();
    readonly HashSet<string> _written = new();
    readonly HashSet<string> _failed = new();
    EmberSettings _settings;
    ModeResolver _resolver;
    IReadOnlyList<string> _competitors = [];
    DateTimeOffset _lastScan = DateTimeOffset.MinValue;
    bool _wasSafetyOverride;
    string? _testMessage;
    FanControlStatus _status;

    public FanControlLoop(SnapshotHub hub, IFanController fans, SettingsStore store,
        Func<IReadOnlyList<string>> processNames, TimeProvider clock, ILogger<FanControlLoop> log,
        FanControlIdentity identity)
    {
        _hub = hub;
        _fans = fans;
        _store = store;
        _processNames = processNames;
        _clock = clock;
        _log = log;
        _identity = identity;
        _settings = store.Load();
        _resolver = new ModeResolver(_settings.NormalMode, _settings.GameMode);
        _status = BuildStatus(false);
    }

    /// <summary>The latest status for the read pipe.</summary>
    public FanControlStatus Status
    {
        get { lock (_gate) return _status; }
    }

    /// <summary>The header test runner (Task 11) plugs in here; null means tests are unavailable.</summary>
    internal Func<string, bool>? StartHeaderTest { get; set; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Action<Snapshot> handler = s =>
        {
            try { OnSnapshot(s); }
            catch (Exception ex) { _log.LogError(ex, "Fan control step failed"); }
        };
        _hub.Published += handler;
        try { await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        finally
        {
            _hub.Published -= handler;
            ReleaseAll();
        }
    }

    public override void Dispose()
    {
        ReleaseAll();
        base.Dispose();
    }

    public void OnSnapshot(Snapshot s)
    {
        lock (_gate)
        {
            var now = _clock.GetUtcNow();
            ScanCompetitors(now);
            _resolver.Expire(now);

            var summary = DashboardSummary.From(s);
            var temps = new Dictionary<TempSource, double?>();
            Observe(temps, TempSource.Cpu, summary.CpuTemp, s.Stale);
            Observe(temps, TempSource.Gpu, summary.GpuTemp, s.Stale);
            Observe(temps, TempSource.Motherboard, summary.MotherboardTemp, s.Stale);

            var safety = _safety.Evaluate(now, temps);
            if (_wasSafetyOverride && !safety.AllFansMax) foreach (var e in _engines.Values) e.Reset();
            _wasSafetyOverride = safety.AllFansMax;

            if (_competitors.Count > 0) ReleaseAllLocked();
            else DriveHeaders(temps, safety);

            _status = BuildStatus(safety.AllFansMax);
        }
    }

    void Observe(Dictionary<TempSource, double?> temps, TempSource source, double? value, bool stale)
    {
        if (value is not null) _seen.Add(source);
        if (_seen.Contains(source)) temps[source] = stale ? null : value;
    }

    void ScanCompetitors(DateTimeOffset now)
    {
        if (now - _lastScan < ScanEvery) return;
        _lastScan = now;
        try { _competitors = CompetitorList.Default.Match(_processNames()); }
        catch (Exception ex) { _log.LogWarning(ex, "Process scan failed; keeping previous result"); }
    }

    void DriveHeaders(Dictionary<TempSource, double?> temps, SafetyResult safety)
    {
        var headers = _fans.Enumerate().ToDictionary(h => h.Id);
        var curve = ModePresets.Get(_resolver.Effective);
        var percents = new Dictionary<Group, double>();

        foreach (var record in _settings.Headers)
        {
            if (!record.Enabled || !IsTested(record) || !headers.TryGetValue(record.Id, out var header) || !header.HasControl) continue;
            if (GroupOf(header) is not { } group) continue;

            if (!percents.TryGetValue(group, out var percent))
            {
                percent = Compute(group, temps, safety, curve);
                percents[group] = percent;
                _lastPercent[group] = percent;
            }
            var floor = header.Name == "Pump Fan" ? PumpFloor : Floor;
            try
            {
                _fans.SetPercent(header.Id, Math.Max(floor, percent));
                _written.Add(header.Id);
            }
            catch (Exception ex) { _log.LogError(ex, "Writing {Header} failed", header.Id); }
        }
    }

    double Compute(Group group, Dictionary<TempSource, double?> temps, SafetyResult safety, FanCurve curve)
    {
        if (safety.AllFansMax) return 100;
        var wanted = (group == Group.CpuPump ? new[] { TempSource.Cpu } : AllSources).Where(_seen.Contains).ToList();
        if (wanted.Count == 0 || wanted.All(safety.MissingSources.Contains)) return 100;
        var live = wanted.Select(w => temps.GetValueOrDefault(w)).Where(v => v is not null).Select(v => v!.Value).ToList();
        // Inside the 5 s grace period a source can be briefly unreadable: hold the last value rather than guess.
        if (live.Count == 0) return _lastPercent.GetValueOrDefault(group, 100);
        return _engines[group].Step(live.Max(), curve);
    }

    static Group? GroupOf(FanHeader h)
    {
        if (!h.Id.StartsWith("/lpc/", StringComparison.Ordinal)) return null; // GPU fan is handled in a later task
        if (h.Name is "CPU Fan" or "Pump Fan") return Group.CpuPump;
        if (h.Name.StartsWith("System Fan", StringComparison.Ordinal)) return Group.Case;
        return null;
    }

    bool IsTested(HeaderRecord r) => r.PassedBoard == _identity.Board && r.PassedLhmVersion == _identity.LhmVersion;

    void ReleaseAll()
    {
        lock (_gate) ReleaseAllLocked();
    }

    void ReleaseAllLocked()
    {
        foreach (var id in _written.ToList())
        {
            try { _fans.ReleaseToAuto(id); }
            catch (Exception ex) { _log.LogError(ex, "Releasing {Header} failed", id); }
        }
        _written.Clear();
    }

    FanControlStatus BuildStatus(bool safetyOverride)
    {
        IReadOnlyList<FanHeader> headers;
        try { headers = _fans.Enumerate(); }
        catch (Exception) { headers = []; }
        var rows = headers.Select(h =>
        {
            var record = _settings.Headers.FirstOrDefault(r => r.Id == h.Id);
            var state = _failed.Contains(h.Id) ? HeaderState.Failed
                : !h.HasControl ? HeaderState.ReadOnly
                : record is { Enabled: true } && IsTested(record) ? HeaderState.Enabled
                : HeaderState.Controllable;
            return new HeaderStatus(h.Id, h.Name, state, record is not null && IsTested(record));
        }).ToList();
        return new FanControlStatus(_resolver.Effective, _resolver.Source, safetyOverride, _resolver.GameExe, rows,
            _competitors, _testMessage);
    }

    /// <summary>Handles one validated control message. Called from the control pipe.</summary>
    public ControlReply Handle(ControlMessage message)
    {
        lock (_gate)
        {
            var now = _clock.GetUtcNow();
            switch (message)
            {
                case SetMode m:
                    if (!_resolver.PickMode(m.Mode)) return new ControlReply(false, "mode not available");
                    if (_resolver.Source == ModeSource.User) Save(_settings with { NormalMode = m.Mode });
                    return Ok();
                case SetGameMode m:
                    _resolver.SetGameMode(m.Mode);
                    Save(_settings with { GameMode = m.Mode });
                    return Ok();
                case GameStarted g:
                    if (_settings.NotGames.Contains(g.Exe.ToLowerInvariant())) return Ok();
                    _resolver.GameStarted(g.Exe, now);
                    return Ok();
                case GameEnded:
                    _resolver.GameEnded();
                    return Ok();
                case DetectorHeartbeat:
                    _resolver.Heartbeat(now);
                    return Ok();
                case SetNotGame n:
                    var list = _settings.NotGames.Where(e => e != n.Exe.ToLowerInvariant()).ToList();
                    if (n.NotGame) list.Add(n.Exe.ToLowerInvariant());
                    Save(_settings with { NotGames = list });
                    if (n.NotGame && string.Equals(_resolver.GameExe, n.Exe, StringComparison.OrdinalIgnoreCase)) _resolver.GameEnded();
                    return Ok();
                case ConfirmHeader c:
                    return Confirm(c);
                case RunHeaderTest t:
                    if (_competitors.Count > 0) return new ControlReply(false, "close " + string.Join(", ", _competitors) + " first");
                    return StartHeaderTest?.Invoke(t.HeaderId) == true ? Ok() : new ControlReply(false, "test not available");
                default:
                    return new ControlReply(false, "invalid");
            }
        }
    }

    ControlReply Confirm(ConfirmHeader c)
    {
        var existing = _settings.Headers.FirstOrDefault(r => r.Id == c.HeaderId);
        if (!c.Enabled)
        {
            if (existing is null) return Ok();
            Save(_settings with { Headers = _settings.Headers.Select(r => r.Id == c.HeaderId ? r with { Enabled = false } : r).ToList() });
            if (_written.Remove(c.HeaderId))
            {
                try { _fans.ReleaseToAuto(c.HeaderId); }
                catch (Exception ex) { _log.LogError(ex, "Releasing {Header} failed", c.HeaderId); }
            }
            return Ok();
        }
        if (_competitors.Count > 0) return new ControlReply(false, "close " + string.Join(", ", _competitors) + " first");
        var header = _fans.Enumerate().FirstOrDefault(h => h.Id == c.HeaderId);
        if (header is null || !header.HasControl) return new ControlReply(false, "header cannot be controlled");
        if (existing is null || !IsTested(existing)) return new ControlReply(false, "run the header test first");
        Save(_settings with { Headers = _settings.Headers.Select(r => r.Id == c.HeaderId ? r with { Enabled = true } : r).ToList() });
        return Ok();
    }

    /// <summary>Called by the header test runner when a test finishes; records the pass or failure.</summary>
    internal void RecordTestResult(string headerId, bool passed, string message)
    {
        lock (_gate)
        {
            _testMessage = message;
            if (!passed) { _failed.Add(headerId); return; }
            _failed.Remove(headerId);
            var group = _fans.Enumerate().FirstOrDefault(h => h.Id == headerId) is { } h && GroupOf(h) is { } g
                ? (g == Group.CpuPump ? "CPU/Pump" : "Case") : "Case";
            var record = new HeaderRecord(headerId, false, group, _identity.Board, _identity.LhmVersion);
            var others = _settings.Headers.Where(r => r.Id != headerId);
            var kept = _settings.Headers.FirstOrDefault(r => r.Id == headerId);
            Save(_settings with { Headers = others.Append(kept is null ? record : record with { Enabled = kept.Enabled }).ToList() });
        }
    }

    void Save(EmberSettings s)
    {
        _settings = s;
        try { _store.Save(s); }
        catch (Exception ex) { _log.LogError(ex, "Saving settings failed"); }
    }

    static ControlReply Ok() => new(true, null);
}
