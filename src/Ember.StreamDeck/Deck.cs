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
                lock (_gate) _keys.Remove(e.Context);
                break;
            case "didReceiveSettings" when e.Context is not null:
                lock (_gate) if (_keys.TryGetValue(e.Context, out var k)) { k.Settings = e.SettingsAsText(); k.LastSvg = null; }
                await RenderAsync(ct).ConfigureAwait(false);
                break;
            case "keyDown" when e.Context is not null:
                await PressAsync(e.Context, e.SettingsAsText(), ct).ConfigureAwait(false);
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
                if (n++ % 2 == 0) await RefreshLightsAsync(ct).ConfigureAwait(false);
                await RenderAsync(ct).ConfigureAwait(false);
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
        var snap = _snapshot;
        return new EmberState(_appOnline, snap is null ? Readouts.None : Readouts.From(snap), snap?.Control?.Mode, _lights);
    }

    async Task RenderAsync(CancellationToken ct)
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

    async Task PressAsync(string context, Dictionary<string, string> settings, CancellationToken ct)
    {
        string? action;
        lock (_gate) action = _keys.TryGetValue(context, out var k) ? k.Action : null;
        if (action is null) return;
        string Setting(string name) => settings.TryGetValue(name, out var v) ? v : "";

        AppReply? reply = null;
        switch (action)
        {
            case StreamDeckActions.Scene:
                if (Setting("scene").Length == 0) { await AlertAsync(context, ct).ConfigureAwait(false); return; }
                reply = await AppClient.SendAsync(new SelectSceneRequest(Setting("scene")), ct: ct).ConfigureAwait(false);
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
                reply = _snapshot?.Control?.Mode is { } current
                    ? Plain(await ControlClient.SendAsync(new SetMode(StreamDeckLogic.Next(current)), ct).ConfigureAwait(false))
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
            if (Process.GetProcessesByName("Ember.App").Length > 0) return false;
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
