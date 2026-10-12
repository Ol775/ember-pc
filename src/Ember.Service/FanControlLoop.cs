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
    const double GpuFloor = 30;
    // Extra care for the CPU: ramp early above these temperatures (on top of the curve, the 90 °C rule and the 100% on a lost sensor).
    static readonly (double TempC, double Percent)[] CpuGuard = [(70, 60), (75, 85), (80, 100)];
    /// <summary>Bounds settings.json: any local user can reach the control pipe, so the list must not grow without limit.</summary>
    public const int MaxNotGames = 200;

    readonly SnapshotHub _hub;
    readonly IFanController _fans;
    readonly SettingsStore _store;
    readonly Func<IReadOnlyList<string>> _processNames;
    readonly TimeProvider _clock;
    readonly ILogger<FanControlLoop> _log;
    readonly FanControlIdentity _identity;

    readonly object _gate = new();
    readonly SafetyMonitor _safety = new();
    readonly Dictionary<Group, CurveEngine> _engines = new() { [Group.CpuPump] = new(2, 1.0), [Group.Case] = new() };
    readonly Dictionary<Group, double> _lastPercent = new();
    readonly HashSet<TempSource> _seen = new();
    readonly HashSet<string> _written = new();
    readonly HashSet<string> _failed = new();
    readonly Dictionary<string, string> _names = new(); // header id -> name, for the ones Ember has written
    readonly HashSet<string> _everSpun = new(); // control ids whose fan has been seen turning since the service started
    double? _lastCpuTemp;
    bool _stopped;
    readonly BuildFeatures _features;
    bool _disposed;
    string? _testingHeader;
    EmberSettings _settings;
    Dictionary<Group, FanCurve> _manualCurves;
    ModeResolver _resolver;
    IReadOnlyList<string> _competitors = [];
    DateTimeOffset _lastScan = DateTimeOffset.MinValue;
    bool _wasSafetyOverride;
    string? _testMessage;
    FanControlStatus _status;

    public FanControlLoop(SnapshotHub hub, IFanController fans, SettingsStore store,
        Func<IReadOnlyList<string>> processNames, TimeProvider clock, ILogger<FanControlLoop> log,
        FanControlIdentity identity, BuildFeatures? features = null)
    {
        _hub = hub;
        _fans = fans;
        _store = store;
        _processNames = processNames;
        _clock = clock;
        _log = log;
        _identity = identity;
        _features = features ?? BuildFeatures.All;
        _settings = store.Load();
        _manualCurves = BuildManual(_settings.Curves);
        _resolver = new ModeResolver(_settings.NormalMode, _settings.GameMode);
        _status = BuildStatus(false);
    }

    /// <summary>The latest status for the read pipe.</summary>
    public FanControlStatus Status
    {
        get { lock (_gate) return _status; }
    }

    /// <summary>True when a running header test must stop: a safety override, a competing app, or a hot CPU (75 °C or more).</summary>
    internal bool TestMustStop
    {
        get { lock (_gate) return _wasSafetyOverride || _competitors.Count > 0 || _lastCpuTemp >= 75; }
    }

    /// <summary>Runs the guided header test; null means tests are unavailable.</summary>
    internal HeaderTestRunner? Runner { get; set; }

    readonly CancellationTokenSource _stop = new();

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
            _stop.Cancel();
            StopAndRelease();
        }
    }

    public override void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _stop.Cancel();
        StopAndRelease();
        _stop.Dispose();
        base.Dispose();
    }

    /// <summary>The final release. After it no snapshot or message may write again, even one already waiting on the lock.</summary>
    void StopAndRelease()
    {
        lock (_gate)
        {
            _stopped = true;
            ReleaseAllLocked();
        }
    }

    public void OnSnapshot(Snapshot s)
    {
        lock (_gate)
        {
            if (_stopped) return;
            var now = _clock.GetUtcNow();
            ScanCompetitors(now);
            _resolver.Expire(now);

            var summary = DashboardSummary.From(s);
            _lastCpuTemp = s.Stale ? null : summary.CpuTemp;
            foreach (var r in s.Readings)
                if (r.Kind == SensorKind.FanRpm && r.Value > 0) _everSpun.Add(r.Id.Replace("/fan/", "/control/", StringComparison.Ordinal));
            var temps = new Dictionary<TempSource, double?>();
            Observe(temps, TempSource.Cpu, summary.CpuTemp, s.Stale);
            Observe(temps, TempSource.Gpu, summary.GpuTemp, s.Stale);
            Observe(temps, TempSource.Motherboard, summary.MotherboardTemp, s.Stale);

            var safety = _safety.Evaluate(now, temps);
            if (_wasSafetyOverride && !safety.AllFansMax) foreach (var e in _engines.Values) e.Reset();
            _wasSafetyOverride = safety.AllFansMax;

            if (_competitors.Count > 0) ReleaseAllLocked();
            else
            {
                try { DriveHeaders(temps, safety); }
                catch (Exception ex)
                {
                    // Cannot even read the headers: do not leave fans at a stale low value, hand them back.
                    _log.LogError(ex, "Fan control step failed; releasing all headers");
                    ReleaseAllLocked();
                }
            }

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
        var percents = new Dictionary<Group, double>();
        var driven = new HashSet<string>();

        foreach (var record in _settings.Headers)
        {
            if (!record.Enabled || !IsTested(record) || !headers.TryGetValue(record.Id, out var header) || !header.HasControl) continue;
            if (GroupOf(header) is not { } group) continue;

            if (!percents.TryGetValue(group, out var percent))
            {
                percent = Compute(group, temps, safety, CurveFor(group));
                percents[group] = percent;
                _lastPercent[group] = percent;
            }
            var floor = IsGpu(header.Id) ? GpuFloor : header.Name == "CPU Fan" ? ReleaseRules.CpuFloor : ReleaseRules.SystemFloor;
            _names[header.Id] = header.Name;
            try
            {
                _fans.SetPercent(header.Id, Math.Max(floor, percent));
                _written.Add(header.Id);
                driven.Add(header.Id);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Writing {Header} failed", header.Id);
                FailSafe(header.Id);
            }
        }

        // A header Ember wrote earlier but no longer drives (disabled, or a failed release last time): hand it back.
        foreach (var id in _written.Except(driven).ToList()) TryRelease(id);
    }

    /// <summary>The curve in force for a group: the preset of the effective mode, or the user's own curve in Manual mode.</summary>
    FanCurve CurveFor(Group group) =>
        _resolver.Effective == FanMode.Manual ? _manualCurves[group] : ModePresets.Get(_resolver.Effective);

    /// <summary>One FanCurve instance per group, kept until the user edits it: the engine treats a new instance as a changed curve.</summary>
    static Dictionary<Group, FanCurve> BuildManual(ManualCurves curves) => new()
    {
        [Group.CpuPump] = new FanCurve(curves.Cpu),
        [Group.Case] = new FanCurve(curves.Case),
    };

    /// <summary>A write failed: the fan may be at a stale low value. Make it loud (100%), or hand it back if even that fails.</summary>
    void FailSafe(string id)
    {
        _failed.Add(id);
        try
        {
            _fans.SetPercent(id, 100);
            _written.Add(id);
        }
        catch (Exception)
        {
            TryRelease(id);
        }
    }

    /// <summary>Releases one header and forgets it only if that worked, so a failed release is retried on the next step.</summary>
    bool TryRelease(string id)
    {
        // The library restore alone leaves the fan at its last speed on this board, so write the safe speed first.
        var original = _settings.Headers.FirstOrDefault(r => r.Id == id)?.OriginalPercent;
        try { _fans.SetPercent(id, ReleaseRules.ValueFor(_names.GetValueOrDefault(id, ""), original)); }
        catch (Exception ex) { _log.LogError(ex, "Writing the release speed to {Header} failed; trying the library restore anyway", id); }
        try
        {
            _fans.Release(id);
            _written.Remove(id);
            return true;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Releasing {Header} failed; will retry", id);
            try { _fans.SetPercent(id, 100); } catch (Exception) { /* retried next step */ }
            return false;
        }
    }

    double Compute(Group group, Dictionary<TempSource, double?> temps, SafetyResult safety, FanCurve curve)
    {
        if (safety.AllFansMax) return 100;
        var wanted = (group == Group.CpuPump ? new[] { TempSource.Cpu } : AllSources).Where(_seen.Contains).ToList();
        // A source this group depends on has gone blind (seen before, now missing for over 5 s): do not trust the
        // others to stand in for it, because the blind one may be the hot one.
        if (wanted.Count == 0 || wanted.Any(safety.MissingSources.Contains)) return 100;
        var live = wanted.Select(w => temps.GetValueOrDefault(w)).Where(v => v is not null).Select(v => v!.Value).ToList();
        // Inside the 5 s grace period a source can be briefly unreadable: hold the last value rather than guess.
        if (live.Count == 0) return _lastPercent.GetValueOrDefault(group, 100);
        var percent = _engines[group].Step(live.Max(), curve);
        if (group == Group.CpuPump && temps.GetValueOrDefault(TempSource.Cpu) is { } cpu)
            foreach (var (t, p) in CpuGuard) if (cpu >= t) percent = Math.Max(percent, p);
        return percent;
    }

    bool GpuAllowed => _features.GpuFan && _settings.GpuControlEnabled;
    string GpuRefusal => _features.GpuFan ? "turn on GPU fan control first" : "GPU fan control is a beta feature and is not in this build";
    static ControlReply NotInBuild(string what) => new(false, what + " is a beta feature and is not in this build");

    static bool IsGpu(string id) => id.StartsWith("/gpu-", StringComparison.Ordinal);

    Group? GroupOf(FanHeader h)
    {
        if (IsGpu(h.Id)) return GpuAllowed ? Group.Case : null; // optional (beta), off by default
        if (!h.Id.StartsWith("/lpc/", StringComparison.Ordinal)) return null;
        if (h.Name == "CPU Fan") return Group.CpuPump; // the pump is left to the board
        if (h.Name.StartsWith("System Fan", StringComparison.Ordinal)) return Group.Case;
        return null;
    }

    bool IsTested(HeaderRecord r) => r.PassedBoard == _identity.Board && r.PassedLhmVersion == _identity.LhmVersion;

    void ReleaseAllLocked()
    {
        foreach (var id in _written.ToList()) TryRelease(id);
    }

    void ReleaseWhere(Func<string, bool> match)
    {
        foreach (var id in _written.Where(match).ToList()) TryRelease(id);
    }

    FanControlStatus BuildStatus(bool safetyOverride)
    {
        IReadOnlyList<FanHeader> headers;
        try { headers = _fans.Enumerate(); }
        catch (Exception) { headers = []; }
        var rows = headers
            // A header with no fan on it (never seen turning, never tested) is not shown at all.
            .Where(h => _everSpun.Contains(h.Id) || _settings.Headers.Any(r => r.Id == h.Id))
            .Select(h =>
        {
            var record = _settings.Headers.FirstOrDefault(r => r.Id == h.Id);
            var state = _failed.Contains(h.Id) ? HeaderState.Failed
                : IsGpu(h.Id) && !GpuAllowed ? HeaderState.Unknown
                : !h.HasControl ? HeaderState.ReadOnly
                : GroupOf(h) is null ? HeaderState.Unknown // not a CPU, pump or system fan header Ember knows how to drive
                : record is { Enabled: true } && IsTested(record) ? HeaderState.Enabled
                : HeaderState.Controllable;
            return new HeaderStatus(h.Id, h.Name, state, record is not null && IsTested(record));
        }).ToList();
        return new FanControlStatus(_resolver.Effective, _resolver.Source, safetyOverride, _resolver.GameExe, rows,
            _competitors, _testMessage, _settings.NotGames, GpuAllowed, _settings.Curves);
    }

    /// <summary>Handles one validated control message. Called from the control pipe.</summary>
    public ControlReply Handle(ControlMessage message)
    {
        lock (_gate)
        {
            if (_stopped) return new ControlReply(false, "the service is stopping");
            var reply = HandleLocked(message);
            _status = BuildStatus(_wasSafetyOverride);
            return reply;
        }
    }

    ControlReply HandleLocked(ControlMessage message)
    {
        {
            var now = _clock.GetUtcNow();
            switch (message)
            {
                case SetMode m:
                    if (!_resolver.PickMode(m.Mode)) return new ControlReply(false, "mode not available");
                    if (_resolver.Source == ModeSource.User) Save(_settings with { NormalMode = m.Mode });
                    return Ok();
                case SetGameMode m:
                    if (!_features.GameDetection) return NotInBuild("Game mode");
                    _resolver.SetGameMode(m.Mode);
                    Save(_settings with { GameMode = m.Mode });
                    return Ok();
                case GameStarted g:
                    if (!_features.GameDetection) return NotInBuild("Game detection");
                    if (_settings.NotGames.Contains(g.Exe.ToLowerInvariant())) return Ok();
                    _resolver.GameStarted(g.Exe, now);
                    return Ok();
                case GameEnded:
                    if (!_features.GameDetection) return NotInBuild("Game detection");
                    _resolver.GameEnded();
                    return Ok();
                case DetectorHeartbeat:
                    if (!_features.GameDetection) return NotInBuild("Game detection");
                    _resolver.Heartbeat(now);
                    return Ok();
                case SetNotGame n:
                    if (!_features.GameDetection) return NotInBuild("Game detection");
                    var list = _settings.NotGames.Where(e => e != n.Exe.ToLowerInvariant()).ToList();
                    if (n.NotGame && list.Count >= MaxNotGames && !_settings.NotGames.Contains(n.Exe.ToLowerInvariant()))
                        return new ControlReply(false, "the not-a-game list is full");
                    if (n.NotGame) list.Add(n.Exe.ToLowerInvariant());
                    Save(_settings with { NotGames = list });
                    if (n.NotGame && string.Equals(_resolver.GameExe, n.Exe, StringComparison.OrdinalIgnoreCase)) _resolver.GameEnded();
                    return Ok();
                case SetGpuControl g:
                    if (!_features.GpuFan) return NotInBuild("GPU fan control");
                    Save(_settings with { GpuControlEnabled = g.Enabled });
                    if (!g.Enabled) ReleaseWhere(IsGpu);
                    return Ok();
                case SetCurve sc:
                    if (ManualCurveRules.Check(sc.Points) is { } why) return new ControlReply(false, why);
                    Save(_settings with { Curves = _settings.Curves.With(sc.Group, sc.Points.ToList()) });
                    _manualCurves = BuildManual(_settings.Curves);
                    return Ok();
                case ConfirmHeader c:
                    return Confirm(c);
                case RunHeaderTest t:
                    if (IsGpu(t.HeaderId) && !GpuAllowed) return new ControlReply(false, GpuRefusal);
                    if (_competitors.Count > 0) return new ControlReply(false, "close " + string.Join(", ", _competitors) + " first");
                    return StartTest(t.HeaderId);
                default:
                    return new ControlReply(false, "invalid");
            }
        }
    }

    ControlReply StartTest(string headerId)
    {
        if (Runner is not { } runner) return new ControlReply(false, "test not available");
        if (_settings.Headers.Any(r => r.Id == headerId && r.Enabled)) return new ControlReply(false, "disable the header before testing it");
        if (runner.IsBusy || _testingHeader is not null) return new ControlReply(false, "a test is already running");
        if (_fans.Enumerate().FirstOrDefault(h => h.Id == headerId) is { } known && !IsGpu(known.Id) && GroupOf(known) is null)
            return new ControlReply(false, "this header is not part of a fan group");
        _testMessage = "Testing…";
        _testingHeader = headerId; // set now, not inside the task, so a second request cannot slip in before it starts
        _ = Task.Run(async () =>
        {
            try
            {
                var r = await runner.RunAsync(headerId, _stop.Token).ConfigureAwait(false);
                RecordTestResult(headerId, r.Passed, r.Message, r.OriginalPercent);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _log.LogError(ex, "Header test failed");
                RecordTestResult(headerId, false, "The test stopped because of an error.");
            }
            finally
            {
                lock (_gate) _testingHeader = null;
            }
        });
        return Ok();
    }

    ControlReply Confirm(ConfirmHeader c)
    {
        var existing = _settings.Headers.FirstOrDefault(r => r.Id == c.HeaderId);
        if (!c.Enabled)
        {
            if (existing is null) return Ok();
            Save(_settings with { Headers = _settings.Headers.Select(r => r.Id == c.HeaderId ? r with { Enabled = false } : r).ToList() });
            if (_written.Contains(c.HeaderId)) TryRelease(c.HeaderId); // a failed release stays in _written and is retried each step
            return Ok();
        }
        if (_testingHeader == c.HeaderId) return new ControlReply(false, "wait for the test to finish");
        if (IsGpu(c.HeaderId) && !GpuAllowed) return new ControlReply(false, GpuRefusal);
        if (_competitors.Count > 0) return new ControlReply(false, "close " + string.Join(", ", _competitors) + " first");
        var header = _fans.Enumerate().FirstOrDefault(h => h.Id == c.HeaderId);
        if (header is null || !header.HasControl) return new ControlReply(false, "header cannot be controlled");
        if (GroupOf(header) is null) return new ControlReply(false, "this header is not part of a fan group");
        if (existing is null || !IsTested(existing)) return new ControlReply(false, "run the header test first");
        Save(_settings with { Headers = _settings.Headers.Select(r => r.Id == c.HeaderId ? r with { Enabled = true } : r).ToList() });
        return Ok();
    }

    /// <summary>Called by the header test runner when a test finishes; records the pass or failure.</summary>
    internal void RecordTestResult(string headerId, bool passed, string message, double? originalPercent = null)
    {
        lock (_gate)
        {
            _testMessage = message;
            if (!passed) { _failed.Add(headerId); _status = BuildStatus(_wasSafetyOverride); return; }
            _failed.Remove(headerId);
            var group = _fans.Enumerate().FirstOrDefault(h => h.Id == headerId) is { } h && GroupOf(h) is { } g
                ? (g == Group.CpuPump ? "CPU/Pump" : "Case") : "Case";
            var record = new HeaderRecord(headerId, false, group, _identity.Board, _identity.LhmVersion, originalPercent);
            var others = _settings.Headers.Where(r => r.Id != headerId);
            var kept = _settings.Headers.FirstOrDefault(r => r.Id == headerId);
            Save(_settings with { Headers = others.Append(kept is null ? record : record with { Enabled = kept.Enabled }).ToList() });
            _status = BuildStatus(_wasSafetyOverride);
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
