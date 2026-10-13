using Ember.Lighting.OpenRgb;

namespace Ember.Lighting;

public sealed record DeviceInfo(string Id, string Name, string Kind, string Source, int LedCount, bool Realtime, int Columns = 0);

/// <summary>
/// Owns the lighting state of the app: the scenes, the devices found by the sources, and the render loop (about 30 pictures
/// a second). All methods are safe to call from the UI thread; <see cref="Changed"/> is raised from a background thread.
/// </summary>
public sealed class LightingManager : IAsyncDisposable
{
    static readonly TimeSpan Frame = TimeSpan.FromMilliseconds(33), RefreshEvery = TimeSpan.FromSeconds(8), ChangedEvery = TimeSpan.FromMilliseconds(100);

    readonly LightingStore _store;
    readonly TimeProvider _clock;
    readonly IReadOnlyList<IDeviceSource> _sources;
    readonly LightingEngine _engine;
    readonly object _gate = new();
    readonly DateTimeOffset _t0;
    CancellationTokenSource? _cts;
    Task? _loop;

    LightingState _state;
    List<IDeviceSink> _sinks = [];
    List<DeviceInfo> _devices = [];
    Dictionary<string, Color[]> _preview = [];
    double? _cpu, _gpu;
    AudioLevels? _audio;
    IReadOnlyList<Color>? _screen;
    DateTimeOffset _lastRefresh = DateTimeOffset.MinValue, _lastChanged = DateTimeOffset.MinValue;
    bool _refreshNow = true;
    bool _saveFailed;

    public LightingManager(LightingStore store, TimeProvider clock, IReadOnlyList<IDeviceSource> sources)
    {
        _store = store;
        _clock = clock;
        _sources = sources;
        _engine = new LightingEngine(clock);
        _state = store.Load();
        _t0 = clock.GetUtcNow();
    }

    public event Action? Changed;

    public LightingState State { get { lock (_gate) return _state; } }
    public IReadOnlyList<DeviceInfo> Devices { get { lock (_gate) return _devices.ToList(); } }
    public IReadOnlyDictionary<string, Color[]> Preview { get { lock (_gate) return _preview; } }
    public bool SaveFailed { get { lock (_gate) return _saveFailed; } }

    /// <summary>One line per source, for the page.</summary>
    public IReadOnlyList<string> SourceStatus => _sources.Select(s => $"{s.Name}: {s.Status}").ToList();

    public IReadOnlyDictionary<string, string> Problems =>
        _engine.Failures.ToDictionary(kv => kv.Key, kv => kv.Value.Message);

    public void Start()
    {
        if (_loop is not null) return;
        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => RunAsync(_cts.Token));
    }

    public void SetInputs(double? cpuTemp, double? gpuTemp, IReadOnlyList<Color>? screenZones)
    {
        lock (_gate) { _cpu = cpuTemp; _gpu = gpuTemp; _screen = screenZones; }
    }

    public void SetAudio(AudioLevels? audio)
    {
        lock (_gate) _audio = audio;
    }

    /// <summary>True when an enabled layer follows the music, so the app listens to the sound only when it is needed.</summary>
    public bool WantsAudio
    {
        get { lock (_gate) return _state.On && _state.Current.Layers.Any(l => l.Enabled && l.Effect is AudioPulseEffect or AudioSpectrumEffect); }
    }

    /// <summary>True when an enabled layer wants the screen picture, so the app only captures the screen when it is needed.</summary>
    public bool WantsScreen
    {
        get { lock (_gate) return _state.On && _state.Current.Layers.Any(l => l.Enabled && l.Effect is ScreenSyncEffect); }
    }

    public void RefreshDevices() { lock (_gate) _refreshNow = true; }

    // ---- commands ----

    public void SetOn(bool on) => Mutate(s => s with { On = on });

    public void SetBrightness(double brightness) =>
        EditScene(sc => sc with { Brightness = Math.Clamp(brightness, 0, 1) });

    public void SelectScene(string name) => Mutate(s => s.Scenes.Any(x => x.Name == name) ? s with { CurrentScene = name } : s);

    /// <summary>Saves a copy of the current scene under a new name (or replaces the scene of that name) and selects it.</summary>
    public bool SaveSceneAs(string name)
    {
        name = name.Trim();
        if (name.Length is 0 or > 60) return false;
        var ok = true;
        Mutate(s =>
        {
            var copy = s.Current with { Name = name };
            var scenes = s.Scenes.Where(x => x.Name != name).Append(copy).ToList();
            if (scenes.Count > LightingState.MaxScenes) { ok = false; return s; }
            return s with { Scenes = scenes, CurrentScene = name };
        });
        return ok;
    }

    public void DeleteScene(string name) => Mutate(s =>
    {
        if (s.Scenes.Count <= 1) return s;
        var scenes = s.Scenes.Where(x => x.Name != name).ToList();
        return s with { Scenes = scenes, CurrentScene = s.CurrentScene == name ? scenes[0].Name : s.CurrentScene };
    });

    /// <summary>Changes the current scene. The result is checked; an invalid one is ignored.</summary>
    public void EditScene(Func<Scene, Scene> edit) => Mutate(s =>
    {
        var current = s.Current;
        var changed = edit(current);
        return s with { Scenes = s.Scenes.Select(x => x.Name == current.Name ? changed : x).ToList() };
    });

    /// <summary>How many segments a lamp is driven as (1 = one colour for the whole lamp).</summary>
    public void SetSegments(string deviceId, int count) => Mutate(s =>
    {
        var map = (s.Segments ?? new Dictionary<string, int>()).ToDictionary(kv => kv.Key, kv => kv.Value);
        if (count <= 1) map.Remove(deviceId); else map[deviceId] = Math.Min(count, 255);
        return s with { Segments = map.Count == 0 ? null : map };
    });

    public int SegmentsOf(string deviceId) => State.Segments is { } m && m.TryGetValue(deviceId, out var n) ? n : 1;

    public void SetOpenRgb(string? path, bool? start) => Mutate(s => s with { OpenRgbPath = string.IsNullOrWhiteSpace(path) ? null : path.Trim(), StartOpenRgb = start });

    // Writing the file waits until the changes stop (a slider produces dozens a second) and is done once on shutdown.
    ITimer? _saveTimer;
    bool _dirty;

    void ScheduleSave()
    {
        _dirty = true;
        _saveTimer ??= _clock.CreateTimer(_ => Flush(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _saveTimer.Change(TimeSpan.FromMilliseconds(400), Timeout.InfiniteTimeSpan);
    }

    void Flush()
    {
        LightingState state;
        lock (_gate)
        {
            if (!_dirty) return;
            _dirty = false;
            state = _state;
        }
        var ok = _store.TrySave(state);
        lock (_gate) _saveFailed = !ok;
    }

    void Mutate(Func<LightingState, LightingState> change)
    {
        lock (_gate)
        {
            var next = change(_state);
            if (ReferenceEquals(next, _state) || !LightingState.IsValid(next)) return;
            _state = next;
            ScheduleSave();
        }
        Changed?.Invoke();
    }

    // ---- loop ----

    async Task RunAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(Frame);
        try
        {
            do
            {
                try { await StepAsync(ct).ConfigureAwait(false); }
                catch (Exception ex) when (ex is not OperationCanceledException) { /* a bad frame must never end the loop */ }
            }
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false));
        }
        catch (OperationCanceledException) { }
    }

    async Task StepAsync(CancellationToken ct)
    {
        var now = _clock.GetUtcNow();
        bool refresh;
        lock (_gate)
        {
            refresh = _refreshNow || now - _lastRefresh >= RefreshEvery || _engine.Failures.Count > 0 && now - _lastRefresh >= TimeSpan.FromSeconds(2);
            if (refresh) { _refreshNow = false; _lastRefresh = now; }
        }
        if (refresh) await RefreshAsync(ct).ConfigureAwait(false);

        LightingState state;
        IReadOnlyList<IDeviceSink> sinks;
        LightingInputs inputs;
        lock (_gate)
        {
            state = _state;
            sinks = _sinks.ToList();
            inputs = new LightingInputs((now - _t0).TotalSeconds, _cpu, _gpu, _screen, _audio);
        }
        var frame = await _engine.StepAsync(state.Current, state.On, inputs, sinks, ct, state.Scenes).ConfigureAwait(false);
        foreach (var id in _engine.Failures.Keys)
            foreach (var source in _sources) source.ReportFailure(id);

        var raise = false;
        lock (_gate)
        {
            _preview = frame;
            if (now - _lastChanged >= ChangedEvery) { _lastChanged = now; raise = true; }
        }
        if (raise) Changed?.Invoke();
    }

    async Task RefreshAsync(CancellationToken ct)
    {
        var sinks = new List<IDeviceSink>();
        foreach (var source in _sources)
        {
            try { sinks.AddRange(await source.RefreshAsync(ct).ConfigureAwait(false)); }
            catch (Exception ex) when (ex is not OperationCanceledException) { /* the source reports its own status */ }
        }
        _engine.Reset();
        lock (_gate)
        {
            _sinks = sinks;
            _devices = sinks.Select(s => new DeviceInfo(s.Id, s.Name, s.Kind, SourceOf(s), s.LedCount, s.Realtime, s.Columns)).ToList();
            var withPlacements = Placed(_state, sinks);
            if (!ReferenceEquals(withPlacements, _state))
            {
                _state = withPlacements;
                ScheduleSave();
            }
        }
        Changed?.Invoke();
    }

    string SourceOf(IDeviceSink sink) => sink.Id.StartsWith("govee", StringComparison.Ordinal) ? "Govee" : sink.Id.StartsWith("logitech", StringComparison.Ordinal) ? "Logitech" : "OpenRGB";

    /// <summary>Gives every new device a row on the canvas of the current scene, so it can be seen and moved.</summary>
    public static LightingState Placed(LightingState state, IReadOnlyList<IDeviceSink> sinks)
    {
        var scene = state.Current;
        var have = scene.Placements.Select(p => p.DeviceId).ToHashSet();
        var missing = sinks.Where(s => !have.Contains(s.Id)).ToList();
        if (missing.Count == 0 || scene.Placements.Count + missing.Count > LightingState.MaxPlacements) return state;
        var placements = scene.Placements.ToList();
        foreach (var sink in missing)
        {
            var row = placements.Count;
            placements.Add(new DevicePlacement(sink.Id, 0.08, Math.Min(0.80, 0.05 + 0.19 * row), 0.84, sink.Columns > 1 ? 0.30 : 0.14, Enabled: true));
        }
        var changed = scene with { Placements = placements };
        return state with { Scenes = state.Scenes.Select(x => x.Name == scene.Name ? changed : x).ToList() };
    }

    public async ValueTask DisposeAsync()
    {
        _cts?.Cancel();
        if (_loop is not null) { try { await _loop.ConfigureAwait(false); } catch (OperationCanceledException) { } }
        // Leave the lights the way the user last set them: only an explicit "off" darkens them.
        foreach (var s in _sources) if (s is IAsyncDisposable d) await d.DisposeAsync().ConfigureAwait(false);
        _saveTimer?.Dispose();
        Flush();
        _cts?.Dispose();
    }
}
