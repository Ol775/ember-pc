using System.Diagnostics;
using System.Globalization;
using Ember.Core;

namespace Ember.StreamDeck;

/// <summary>
/// The plugin: keeps the keys that are on screen, redraws them once a second from what Ember reports (service pipe for
/// fan mode and readings, app pipe for lights), and turns key presses into requests to Ember.
/// </summary>
sealed class Deck(StreamDeckClient sd, string pluginUuid)
{
    sealed class Key(string action, Dictionary<string, string> settings)
    {
        public string Action { get; } = action;
        public Dictionary<string, string> Settings { get; set; } = settings;
        public string? LastSvg { get; set; }
        /// <summary>The running hold timer (long press, or Brightness repeat) while the key is down.</summary>
        public CancellationTokenSource? Hold { get; set; }
        public volatile bool LongFired;
    }

    readonly Dictionary<string, Key> _keys = [];
    readonly object _gate = new();
    volatile Snapshot? _snapshot;
    volatile AppStatus? _lights;
    volatile bool _appOnline;

    public async Task RunAsync(CancellationToken ct)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        await sd.SendAsync(StreamDeckMessages.RegisterPlugin(pluginUuid), stop.Token).ConfigureAwait(false);

        var connection = new ServiceConnection(PipeStreamConnector.ConnectAsync, ServiceConnection.DefaultBackoff);
        connection.SnapshotReceived += s => _snapshot = s;
        connection.StateChanged += state => { if (state != ConnectionState.Connected) _snapshot = null; };
        _ = connection.RunAsync(stop.Token);
        var ticker = TickAsync(stop.Token);

        await foreach (var text in sd.ReceiveAsync(stop.Token).ConfigureAwait(false))
        {
            if (!StreamDeckMessages.TryParseEvent(text, out var e) || e is null) continue;
            try { await HandleAsync(e, stop.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
            catch (Exception) { /* one bad event must not stop the plugin */ }
        }
        stop.Cancel();
        try { await ticker.ConfigureAwait(false); } catch (OperationCanceledException) { }
    }

    async Task HandleAsync(SdEvent e, CancellationToken ct)
    {
        switch (e.Event)
        {
            case "willAppear" when e.Context is not null && e.Action is not null:
                lock (_gate) _keys[e.Context] = new Key(e.Action, e.SettingsAsText());
                await RenderAsync(ct).ConfigureAwait(false);
                break;
            case "willDisappear" when e.Context is not null:
                lock (_gate) { if (_keys.Remove(e.Context, out var gone)) gone.Hold?.Cancel(); }
                break;
            case "didReceiveSettings" when e.Context is not null:
                lock (_gate) if (_keys.TryGetValue(e.Context, out var k)) { k.Settings = e.SettingsAsText(); k.LastSvg = null; }
                await RenderAsync(ct).ConfigureAwait(false);
                break;
            case "keyDown" when e.Context is not null:
                await KeyDownAsync(e.Context, e.SettingsAsText(), ct).ConfigureAwait(false);
                break;
            case "keyUp" when e.Context is not null:
                await KeyUpAsync(e.Context, e.SettingsAsText(), ct).ConfigureAwait(false);
                break;
            case "propertyInspectorDidAppear" when e.Context is not null && e.Action is not null:
                await SendChoicesAsync(e.Action, e.Context, ct).ConfigureAwait(false);
                break;
        }
    }

    async Task TickAsync(CancellationToken ct)
    {
        var n = 0;
        while (!ct.IsCancellationRequested)
        {
            bool any;
            lock (_gate) any = _keys.Count > 0;
            if (any)
            {
                // One failed refresh must not end the loop: the keys would freeze until Stream Deck closes the socket.
                try
                {
                    if (n++ % 2 == 0) await RefreshLightsAsync(ct).ConfigureAwait(false);
                    await RenderAsync(ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException) { }
            }
            await Task.Delay(TimeSpan.FromSeconds(1), ct).ConfigureAwait(false);
        }
    }

    async Task RefreshLightsAsync(CancellationToken ct)
    {
        var reply = await AppClient.SendAsync(new AppStatusRequest(), ct: ct).ConfigureAwait(false);
        Apply(reply);
    }

    void Apply(AppReply reply)
    {
        _appOnline = reply.Status is not null;
        _lights = reply.Status;
    }

    EmberState State()
    {
        // A service that hangs keeps its pipe open but stops sending: old numbers and an old mode are shown as nothing.
        var now = DateTimeOffset.UtcNow;
        var snap = _snapshot;
        var fresh = StreamDeckLogic.IsFresh(snap, now);
        return new EmberState(_appOnline, fresh ? Readouts.From(snap!, now) : Readouts.None, fresh ? snap!.Control?.Mode : null, _lights);
    }

    readonly SemaphoreSlim _renderLock = new(1, 1);

    /// <summary>Redraws the keys whose picture changed. One render at a time, so an older picture can never be sent after a newer one.</summary>
    async Task RenderAsync(CancellationToken ct)
    {
        await _renderLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var state = State();
            List<(string Context, string Svg)> changed = [];
            lock (_gate)
            {
                foreach (var (context, key) in _keys)
                {
                    var svg = StreamDeckFaces.Svg(StreamDeckFaces.For(key.Action, key.Settings, state));
                    if (svg == key.LastSvg) continue;
                    key.LastSvg = svg;
                    changed.Add((context, svg));
                }
            }
            foreach (var (context, svg) in changed)
                await sd.SendAsync(StreamDeckMessages.SetImage(context, svg), ct).ConfigureAwait(false);
        }
        finally { _renderLock.Release(); }
    }

    /// <summary>
    /// Press and release. Brightness acts at once and repeats while held. Cycle fan mode and Lighting scene act on release
    /// (tap: next mode, or the chosen scene) unless held for half a second, which acts straight away (hold: previous mode, or the next scene).
    /// Every other key acts when pressed.
    /// </summary>
    async Task KeyDownAsync(string context, Dictionary<string, string> settings, CancellationToken ct)
    {
        Key? key;
        lock (_gate) _keys.TryGetValue(context, out key);
        if (key is null) return;
        key.Hold?.Cancel();
        key.LongFired = false;
        switch (key.Action)
        {
            case StreamDeckActions.Brightness:
                await PressAsync(context, settings, false, ct).ConfigureAwait(false);
                key.Hold = Spawn(ct, async hold =>
                {
                    await Task.Delay(StreamDeckLogic.HoldDelay, hold).ConfigureAwait(false);
                    while (!hold.IsCancellationRequested)
                    {
                        // The request itself uses the plugin token: letting go of the key must not cancel it half way (that flashed a false warning).
                        await PressAsync(context, settings, false, ct).ConfigureAwait(false);
                        await Task.Delay(StreamDeckLogic.RepeatInterval, hold).ConfigureAwait(false);
                    }
                });
                break;
            case StreamDeckActions.CycleMode:
            case StreamDeckActions.Scene:
                key.Hold = Spawn(ct, async hold =>
                {
                    await Task.Delay(StreamDeckLogic.HoldDelay, hold).ConfigureAwait(false);
                    key.LongFired = true;
                    await PressAsync(context, settings, true, ct).ConfigureAwait(false);
                });
                break;
            default:
                await PressAsync(context, settings, false, ct).ConfigureAwait(false);
                break;
        }
    }

    async Task KeyUpAsync(string context, Dictionary<string, string> settings, CancellationToken ct)
    {
        Key? key;
        lock (_gate) _keys.TryGetValue(context, out key);
        if (key is null) return;
        key.Hold?.Cancel();
        key.Hold = null;
        if (key.Action is StreamDeckActions.CycleMode or StreamDeckActions.Scene && !key.LongFired)
            await PressAsync(context, settings, false, ct).ConfigureAwait(false);
    }

    static CancellationTokenSource Spawn(CancellationToken ct, Func<CancellationToken, Task> body)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _ = Task.Run(async () =>
        {
            try { await body(cts.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { /* released */ }
            catch (Exception) { /* a failed repeat must not take the plugin down */ }
        });
        return cts;
    }

    async Task PressAsync(string context, Dictionary<string, string> settings, bool longPress, CancellationToken ct)
    {
        string? action;
        lock (_gate) action = _keys.TryGetValue(context, out var k) ? k.Action : null;
        if (action is null) return;
        string Setting(string name) => settings.TryGetValue(name, out var v) ? v : "";

        AppReply? reply = null;
        switch (action)
        {
            case StreamDeckActions.Scene:
                var scene = longPress
                    ? _lights is { } l ? StreamDeckLogic.NextScene(l.CurrentScene, l.Scenes) : null
                    : Setting("scene");
                if (string.IsNullOrEmpty(scene)) { await AlertAsync(context, ct).ConfigureAwait(false); return; }
                reply = await AppClient.SendAsync(new SelectSceneRequest(scene), ct: ct).ConfigureAwait(false);
                break;
            case StreamDeckActions.Power:
                reply = await AppClient.SendAsync(new ToggleOnRequest(), ct: ct).ConfigureAwait(false);
                break;
            case StreamDeckActions.Brightness:
                var step = double.TryParse(Setting("step"), NumberStyles.Float, CultureInfo.InvariantCulture, out var s) ? Math.Clamp(s, -1, 1) : 0.1;
                reply = await AppClient.SendAsync(new NudgeBrightnessRequest(step), ct: ct).ConfigureAwait(false);
                break;
            case StreamDeckActions.FanMode:
                reply = Enum.TryParse<FanMode>(Setting("mode"), out var mode) && StreamDeckLogic.CycleOrder.Contains(mode)
                    ? Plain(await ControlClient.SendAsync(new SetMode(mode), ct).ConfigureAwait(false))
                    : new AppReply(false, "No mode chosen.", null);
                break;
            case StreamDeckActions.CycleMode:
                var fresh = StreamDeckLogic.IsFresh(_snapshot, DateTimeOffset.UtcNow);
                reply = fresh && _snapshot?.Control?.Mode is { } current
                    ? Plain(await ControlClient.SendAsync(new SetMode(longPress ? StreamDeckLogic.Previous(current) : StreamDeckLogic.Next(current)), ct).ConfigureAwait(false))
                    : new AppReply(false, "Ember service is not running.", null);
                break;
            case StreamDeckActions.Open:
                reply = await AppClient.SendAsync(new ShowWindowRequest(), ct: ct).ConfigureAwait(false);
                if (!reply.Ok && reply.Error == "Ember is not running." && TryStartEmber()) reply = new AppReply(true, null, null);
                break;
            default:
                return;
        }
        if (!reply.Ok) { await AlertAsync(context, ct).ConfigureAwait(false); return; }
        if (reply.Status is not null) Apply(reply);
        await RenderAsync(ct).ConfigureAwait(false);
    }

    static AppReply Plain(ControlReply r) => new(r.Ok, r.Error, null);

    Task AlertAsync(string context, CancellationToken ct) => sd.SendAsync(StreamDeckMessages.ShowAlert(context), ct);

    /// <summary>Starts Ember only when no copy is running. The path comes from ember-path.txt, written by Ember when it installs the plugin.</summary>
    static bool TryStartEmber()
    {
        try
        {
            var running = Process.GetProcessesByName("Ember.App");
            foreach (var p in running) p.Dispose();
            if (running.Length > 0) return false;
            var file = Path.Combine(AppContext.BaseDirectory, "ember-path.txt");
            if (!File.Exists(file)) return false;
            var exe = File.ReadAllText(file).Trim();
            if (!Path.GetFileName(exe).Equals("Ember.App.exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(exe)) return false;
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(exe)! })?.Dispose();
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    async Task SendChoicesAsync(string action, string context, CancellationToken ct)
    {
        var reply = await AppClient.SendAsync(new AppStatusRequest(), ct: ct).ConfigureAwait(false);
        Apply(reply);
        var scenes = reply.Status?.Scenes ?? [];
        var modes = StreamDeckLogic.CycleOrder.Select(m => new { value = m.ToString(), label = StreamDeckLogic.ModeLabel(m) }).ToArray();
        await sd.SendAsync(StreamDeckMessages.SendToPropertyInspector(action, context, new { scenes, modes }), ct).ConfigureAwait(false);
    }
}
