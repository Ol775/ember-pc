using Ember.Lighting.OpenRgb;

namespace Ember.Lighting;

public sealed record DeviceInfo(string Id, string Name, string Kind, string Source, int LedCount, bool Realtime, int Columns = 0, bool Disabled = false);

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
    readonly List<KeyPulse> _pulses = [];
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
        get { lock (_gate) return _state.On && _state.Current.Layers.Any(l => l.Enabled && l.Effect is AudioPulseEffect or AudioSpectrumEffect or AudioBarsEffect); }
    }

    /// <summary>True when an enabled layer reacts to key presses, so the app watches the keyboard only when it is needed.</summary>
    public bool WantsKeys
    {
        get { lock (_gate) return _state.On && _state.Current.Layers.Any(l => l.Enabled && l.Effect is ReactiveKeysEffect); }
    }

    /// <summary>A key went down. Only where it sits on the keyboard and when are kept, for about two seconds; which key it was is not.</summary>
    public void KeyPressed(int virtualKey)
    {
        if (KeyboardLayout.Position(virtualKey) is not { } at) return;
        var seconds = (_clock.GetUtcNow() - _t0).TotalSeconds;
        lock (_gate)
        {
            _pulses.RemoveAll(p => seconds - p.Seconds > 4);
            if (_pulses.Count >= 48) _pulses.RemoveAt(0);
            _pulses.Add(new KeyPulse(at.X, at.Y, seconds));
        }
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

    /// <summary>Changes the scene of that name (a device's own lighting is a scene), whether or not it is the current one.</summary>
    public void EditSceneNamed(string name, Func<Scene, Scene> edit) => Mutate(s =>
        s.Scenes.Any(x => x.Name == name) ? s with { Scenes = s.Scenes.Select(x => x.Name == name ? edit(x) : x).ToList() } : s);

    /// <summary>
    /// Gives one device lighting of its own: a copy of the current scene's layers, saved as a scene called "&lt;device&gt; lights",
    /// that only that device shows. Returns the scene's name (the copy is reused if the device already has one), or null when
    /// there is no room for another scene.
    /// </summary>
    public string? GiveOwnLighting(string deviceId, string deviceName)
    {
        string? result = null;
        Mutate(s =>
        {
            var current = s.Current;
            var placement = current.Placements.FirstOrDefault(p => p.DeviceId == deviceId);
            if (placement is null) return s;
            if (placement.Profile is { } have && have != current.Name && s.Scenes.Any(x => x.Name == have)) { result = have; return s; }
            if (s.Scenes.Count >= LightingState.MaxScenes) return s;
            var baseName = deviceName.Trim().Length == 0 ? "Device" : deviceName.Trim();
            baseName = (baseName.Length > 50 ? baseName[..50] : baseName) + " lights";
            var name = baseName;
            for (var n = 2; s.Scenes.Any(x => x.Name == name); n++) name = baseName + " " + n;
            var copy = current with { Name = name };
            var changed = current with { Placements = current.Placements.Select(p => p.DeviceId == deviceId ? p with { Profile = name } : p).ToList() };
            result = name;
            return s with { Scenes = s.Scenes.Select(x => x.Name == current.Name ? changed : x).Append(copy).ToList() };
        });
        return result;
    }

    /// <summary>Puts a device back on the current scene's lighting. Its own scene is kept, in case it is wanted again.</summary>
    public void FollowScene(string deviceId) => EditScene(sc => sc with { Placements = sc.Placements.Select(p => p.DeviceId == deviceId ? p with { Profile = null } : p).ToList() });

    /// <summary>How many segments a lamp is driven as (1 = one colour for the whole lamp).</summary>
    public void SetSegments(string deviceId, int count) => Mutate(s =>
    {
        var map = (s.Segments ?? new Dictionary<string, int>()).ToDictionary(kv => kv.Key, kv => kv.Value);
        if (count <= 1) map.Remove(deviceId); else map[deviceId] = Math.Min(count, 255);
        return s with { Segments = map.Count == 0 ? null : map };
    });

    /// <summary>Asks the sources how many separately lit parts the device has and applies it. False when none could tell.</summary>
    public async Task<int?> DetectSegmentsAsync(string deviceId, CancellationToken ct = default)
    {
        foreach (var source in _sources.OfType<Govee.ISegmentDetector>())
        {
            var count = await source.DetectSegmentsAsync(deviceId, ct).ConfigureAwait(false);
            if (count is not { } n) continue;
            SetSegments(deviceId, n);
            RefreshDevices();
            return n;
        }
        return null;
    }

    public int SegmentsOf(string deviceId) => State.Segments is { } m && m.TryGetValue(deviceId, out var n) ? n : 1;

    /// <summary>
    /// Switches a device off for Ember altogether: it is left out of every scene and Ember stops sending to it, so it keeps
    /// whatever colour it last had until its own software or a switch-on takes over. Applies at once.
    /// </summary>
    public void SetDeviceDisabled(string deviceId, bool disabled)
    {
        Mutate(s =>
        {
            var list = (s.DisabledDevices ?? []).Where(d => d != deviceId).ToList();
            if (disabled) list.Add(deviceId);
            return s with { DisabledDevices = list.Count == 0 ? null : list };
        });
        RefreshDevices();
    }

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
            var seconds = (now - _t0).TotalSeconds;
            _pulses.RemoveAll(p => seconds - p.Seconds > 4);
            inputs = new LightingInputs(seconds, _cpu, _gpu, _screen, _audio, _pulses.Count == 0 ? null : _pulses.ToList());
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
            var off = _state.DisabledDevices is { } list ? list.ToHashSet() : [];
            _devices = sinks.Select(s => new DeviceInfo(s.Id, s.Name, s.Kind, SourceOf(s), s.LedCount, s.Realtime, s.Columns, off.Contains(s.Id))).ToList();
            sinks = sinks.Where(s => !off.Contains(s.Id)).ToList();
            _sinks = sinks;
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
        var known = sinks.ToDictionary(s => s.Id);
        var placements = scene.Placements.ToList();
        // A canvas the owner has not moved things on is laid out again with the newcomers, so every device keeps a row of its
        // own however many turn up. One that has been arranged by hand is left alone and the newcomers take free space.
        var untouched = placements.Count > 0 && placements.All(p => known.ContainsKey(p.DeviceId)) && MatchesAutoLayout(placements, known);
        if (untouched || placements.Count == 0)
        {
            var all = placements.Select(p => known[p.DeviceId]).Concat(missing).ToList();
            var rows = AutoLayout(all.Select(s => s.Columns > 1).ToList());
            var old = placements.ToDictionary(p => p.DeviceId);
            placements = all.Select((s, i) => old.TryGetValue(s.Id, out var p)
                ? p with { X = 0.08, Y = rows[i].Y, Width = 0.84, Height = rows[i].H }
                : new DevicePlacement(s.Id, 0.08, rows[i].Y, 0.84, rows[i].H, Enabled: true)).ToList();
        }
        else
        {
            foreach (var sink in missing)
            {
                var h = sink.Columns > 1 ? 0.20 : 0.10;
                placements.Add(new DevicePlacement(sink.Id, 0.08, FreeRow(placements, h), 0.84, h, Enabled: true));
            }
        }
        var changed = scene with { Placements = placements };
        return state with { Scenes = state.Scenes.Select(x => x.Name == scene.Name ? changed : x).ToList() };
    }

    /// <summary>Lays every device of the current scene out again, one row each, however it was arranged before.</summary>
    public void Arrange()
    {
        Dictionary<string, IDeviceSink> known;
        lock (_gate) known = _sinks.ToDictionary(s => s.Id);
        EditScene(sc =>
        {
            var present = sc.Placements.Where(p => known.ContainsKey(p.DeviceId)).ToList();
            var rows = AutoLayout(present.Select(p => known[p.DeviceId].Columns > 1).ToList());
            var byId = present.Select((p, i) => (p.DeviceId, Row: rows[i])).ToDictionary(x => x.DeviceId, x => x.Row);
            return sc with { Placements = sc.Placements.Select(p => byId.TryGetValue(p.DeviceId, out var r) ? p with { X = 0.08, Width = 0.84, Y = r.Y, Height = r.H } : p).ToList() };
        });
    }

    const double Margin = 0.04, Gap = 0.02;

    /// <summary>One row per device from top to bottom, tall for keyboards and grids. Rows shrink together so they all fit.</summary>
    static (double Y, double H)[] AutoLayout(IReadOnlyList<bool> grids)
    {
        var weights = grids.Select(g => g ? 2.0 : 1.0).ToList();
        var unit = Math.Min(0.15, (1 - 2 * Margin) / weights.Sum());
        var rows = new (double, double)[grids.Count];
        var y = Margin;
        for (var i = 0; i < rows.Length; i++)
        {
            rows[i] = (Math.Round(y, 4), Math.Round(Math.Max(0.02, weights[i] * unit - Gap), 4));
            y += weights[i] * unit;
        }
        return rows;
    }

    static bool MatchesAutoLayout(IReadOnlyList<DevicePlacement> placements, Dictionary<string, IDeviceSink> known)
    {
        var rows = AutoLayout(placements.Select(p => known[p.DeviceId].Columns > 1).ToList());
        for (var i = 0; i < rows.Length; i++)
        {
            var p = placements[i];
            if (Math.Abs(p.X - 0.08) > 0.001 || Math.Abs(p.Width - 0.84) > 0.001 || Math.Abs(p.Y - rows[i].Y) > 0.001 || Math.Abs(p.Height - rows[i].H) > 0.001) return false;
        }
        return true;
    }

    /// <summary>The first free spot of the given height (a fraction of the canvas), or the bottom edge when nothing is free.</summary>
    static double FreeRow(IReadOnlyList<DevicePlacement> placed, double height)
    {
        for (var y = Margin; y + height <= 1 - Margin + 1e-9; y += 0.01)
            if (placed.All(p => y + height <= p.Y || y >= p.Y + p.Height)) return Math.Round(y, 4);
        return Math.Round(1 - Margin - height, 4);
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
