using System.Net.Http;
using System.IO;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Threading;
using Ember.Core;
using Ember.Lighting;
using Ember.Lighting.Govee;
using Ember.Lighting.Hue;
using Ember.Lighting.Nanoleaf;
using Ember.Lighting.OpenRgb;

namespace Ember.App;

public partial class MainWindow : Window
{
    readonly MainViewModel _vm = new();
    readonly CancellationTokenSource _cts = new();
    readonly LightingManager _lights;
    AppControl? _appControl;
    readonly ISecretStore _secrets = new WindowsCredentialStore();
    readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };
    const string GoveeKeyName = "govee-api-key";
    readonly DispatcherTimer _refresh;
    readonly Task _run;
    readonly GameDetection _detection = new([]);
    readonly DispatcherTimer _gameTimer, _screenTimer;
    DispatcherTimer? _plugTimer;
    readonly Ember.Lighting.Tapo.TapoDirectory _tapo;
    readonly AudioCapture _audio;
    readonly KeyWatcher _keys;
    readonly DxgiGrabber _dxgi = new();
    HistoryChart _chart = null!;
    readonly TrayIcon _tray;
    readonly ThermalStore _thermalStore = new(ThermalStore.DefaultPath);
    readonly ThermalProfile _thermal = new ThermalStore(ThermalStore.DefaultPath).Load();
    readonly AiPanel _ai;
    readonly DispatcherTimer _thermalSave;
    bool _quitting;
    DashboardSummary? _summary;
    PowerSummary? _powerNow;
    Ember.Lighting.Color[]? _screen;
    double? _gpuLoad, _cpuTemp, _gpuTemp, _caseTemp;
    string? _announcedConnection, _announcedBanner, _announcedCompetitor, _announcedTest;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm;

        LightingManager? lights = null;
        // Tapo devices keep their own session cookie per device, so they get a client that does not pool cookies.
        _tapo = new Ember.Lighting.Tapo.TapoDirectory(TapoSession, new HttpClient(new HttpClientHandler { UseCookies = false }) { Timeout = TimeSpan.FromSeconds(8) });
        IReadOnlyList<IDeviceSource> sources = AppData.Simulated ? [] :
            [new OpenRgbSource(() => string.IsNullOrWhiteSpace(lights?.State.OpenRgbPath) ? BundledOpenRgb : lights!.State.OpenRgbPath, () => lights?.State.ShouldStartOpenRgb ?? true),
             new GoveeSource(new GoveeLanClient(), GoveeCloud, id => lights?.SegmentsOf(id) ?? 1),
             new Ember.Lighting.Logitech.LogitechSource(() => Ember.Lighting.Logitech.NativeLogitechLed.TryLoad(), () => lights?.State.LogitechOneColour ?? false),
             new Ember.Lighting.Hue.HueSource(HueConnection),
             new Ember.Lighting.Nanoleaf.NanoleafSource(NanoleafConnection),
             new Ember.Lighting.Razer.RazerSource(() => _razerStore.Enabled, new Ember.Lighting.Razer.RazerChroma(_razerHttp)),
             new Ember.Lighting.Corsair.CorsairSource(Ember.Lighting.Corsair.CorsairApiChain.Open),
             new Ember.Lighting.Meross.MerossSource(MerossSession, new Ember.Lighting.Meross.MerossCloudClient(_http), new Ember.Lighting.Meross.MerossLocalClient(_http)),
             new Ember.Lighting.Tapo.TapoSource(_tapo)];
        _lights = lights = new LightingManager(new LightingStore(LightingStore.DefaultPath), TimeProvider.System, sources);
        Lights.Attach(_lights);
        _lights.Start();
        // Power page: linked Meross and Tapo plugs are read (never switched) every few seconds.
        {
            var plugs = new Ember.Lighting.Meross.PlugMonitor(MerossSession, new Ember.Lighting.Meross.MerossCloudClient(_http), new Ember.Lighting.Meross.MerossLocalClient(_http), () => Power.Links);
            var providers = AppData.Simulated ? [] : new Ember.Lighting.Meross.IPlugProvider[] { plugs, new Ember.Lighting.Tapo.TapoPlugMonitor(_tapo, () => Power.Links) };
            Power.Attach(providers, new Ember.Lighting.Meross.PlugLinkStore(Ember.Lighting.Meross.PlugLinkStore.DefaultPath));
            Power.AttachCost(PowerLedger.Load(PowerLedger.DefaultPath), () => _vm.Prefs.PricePerKwh, price => Remember(_vm.Prefs with { PricePerKwh = price }), () => _vm.Prefs.StandingCharge, charge => Remember(_vm.Prefs with { StandingCharge = charge }));
            _plugTimer = new DispatcherTimer(TimeSpan.FromSeconds(3), DispatcherPriority.Background, async (_, _) =>
            {
                // In the tray the plugs are read less often: nobody is watching the live figure.
                var every = TimeSpan.FromSeconds(Hidden ? 15 : 3);
                if (_plugTimer!.Interval != every) _plugTimer.Interval = every;
                try { await Task.WhenAll(providers.Select(p => p.PollAsync(_cts.Token))); }
                catch (Exception ex) when (ex is OperationCanceledException or HttpRequestException) { }
                catch (Exception ex) { DiagLog.Warn("Power", "Reading the plugs failed", ex); }
            }, Dispatcher);
            _plugTimer.Start();
        }
        // Beta: lets the Stream Deck plugin reach the lights and the window.
        if (!BuildFeatures.IsProduction && !AppData.Simulated) { _appControl = new AppControl(_lights, () => Dispatcher.InvokeAsync(ShowFromTray)); _appControl.Start(); }

        // ServiceConnection raises events on its background loop thread: queue each one onto the UI thread.
        // InvokeAsync is the non-blocking BeginInvoke, so the loop never waits for the UI.
        var connection = new ServiceConnection(PipeStreamConnector.ConnectAsync, ServiceConnection.DefaultBackoff);
        connection.StateChanged += s => { DiagLog.Changed("Service", "connection", s.ToString()); Dispatcher.InvokeAsync(() => _vm.SetConnection(s)); };
        connection.SnapshotReceived += s => Dispatcher.InvokeAsync(() => HandleSnapshot(s));
        HandleSnapshot = s =>
        {
            _vm.Add(s);
            _gpuLoad = s.Readings.FirstOrDefault(r => r.Hardware == HardwareKind.Gpu && r.Kind == SensorKind.Load && r.Name == "GPU Core")?.Value;
            if (s.Control is { } c) _detection.SetNotGames(c.NotGames);
            var sum = DashboardSummary.From(s);
            _summary = s.Stale ? null : sum;
            _cpuTemp = s.Stale ? null : sum.CpuTemp;
            _gpuTemp = s.Stale ? null : sum.GpuTemp;
            var power = PowerSummary.From(s);
            _powerNow = s.Stale ? null : power;
            if (!s.Stale) Power.Feed(DateTimeOffset.UtcNow, power);
            RunAlerts(sum, s.Stale);
            _lights.SetInputs(s.Stale ? null : sum.CpuTemp, s.Stale ? null : sum.GpuTemp, _screen);
            _caseTemp = s.Stale ? null : new[] { sum.CpuTemp, sum.GpuTemp, sum.MotherboardTemp }.Where(v => v is not null).Max();
            if (_vm.Prefs.AiLearning && !s.Stale) _thermal.Add(DateTimeOffset.UtcNow, _cpuTemp, _caseTemp);
        };

        // Throttle: redraw at most twice a second, whatever the snapshot rate.
        _refresh = new DispatcherTimer(TimeSpan.FromMilliseconds(500), DispatcherPriority.Background,
            (_, _) => Refresh(), Dispatcher);
        _refresh.Start();

        // Game detection: once a second look at the foreground program and report start, end and heartbeat to the service.
        _gameTimer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => DetectGame(), Dispatcher);
        // Screen sync: look at the screen ten times a second, but only while an enabled Screen sync layer wants it.
        _audio = new AudioCapture(levels => _lights.SetAudio(levels));
        _keys = new KeyWatcher(vk => _lights.KeyPressed(vk));
        _screenTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Background, (_, _) => GrabScreen(), Dispatcher);
        _screenTimer.Start();
        if (BuildFeatures.Current.GameDetection)
        {
            _gameTimer.Start(); // beta feature: off in the alpha build
            _library = _libraryStore.Load();                  // what the last scan found, known at once
            _detection.SetKnownGames(_library.KnownExes);
            _profiles = _profileStore.Load();
            Games.Attach(ScanLibraryAsync, _profiles, p => { _profiles = p; _profileStore.TrySave(p); });
            _history = PlayHistory.Load(PlayHistory.DefaultPath);
            Games.AttachHistory(_history);
            _playTimer = new DispatcherTimer(TimeSpan.FromSeconds(30), DispatcherPriority.Background, (_, _) => TrackPlay(), Dispatcher);
            _playTimer.Start();
            if (!AppData.Simulated) _ = ScanLibraryAsync();   // and look again now
        }

        StartupSwitch.IsChecked = StartupRegistration.IsEnabled();
        TraySwitch.IsChecked = _vm.Prefs.KeepInTray;
        MinimisedSwitch.IsChecked = _vm.Prefs.StartMinimised;
        HueHost.Text = _hueStore.Load()?.Host ?? "";
        NanoleafHost.Text = _nanoStore.Load()?.Host ?? "";
        RazerSwitch.IsChecked = _razerStore.Enabled;
        Devices.Overrides["Razer"] = (() => _razerStore.Enabled, on =>
        {
            RazerSwitch.IsChecked = on;
            OnRazerToggled(RazerSwitch, new RoutedEventArgs());
        });
        OnSettingsSection(SetGeneral, new RoutedEventArgs());
        _updates = new UpdateChecker(_http);
        _installer = new UpdateInstaller(_http);
        BuildAppearance();
        _chart = new HistoryChart(HistoryCanvas);
        ThemeManager.Changed += () => _chart.Invalidate();
        DocView.FillFolding(ChangelogHost, ChangelogText());
        DocView.FillFolding(RoadmapHost, DocView.ReadEmbedded("ROADMAP.md"), (_, title) => title != "Done", DocView.CountItems);
        ApplyBuildChannel();
        BuildLegal();
        BuildAbout();
        ApplyAppearance();
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += (_, _) => { if (_vm.Prefs.Theme == ThemeMode.System) Dispatcher.InvokeAsync(ApplyAppearance); };
        if (!AppData.Simulated) StartAutomaticUpdateChecks();
        switch (_vm.Prefs.StartPage)
        {
            case StartPage.Fans: NavFans.IsChecked = true; break;
            case StartPage.Lights: NavLights.IsChecked = true; break;
            case StartPage.Devices: NavDevices.IsChecked = true; break;
        }
        RestoreWindowPlacement();
        RememberWindowSwitch.IsChecked = _vm.Prefs.RememberWindow;
        Curve.Committed += OnCurveCommitted;
        Curve.CopyRequested += OnCurveCopy;

        _run = AppData.Simulated ? Task.CompletedTask : Task.Run(() => connection.RunAsync(_cts.Token));
        _ai = new AiPanel(_thermal, () => _vm.Prefs, p => Remember(p), ApplySuggestionAsync, ForgetThermal);
        AiHost.Children.Add(_ai);
        _thermalSave = new DispatcherTimer(TimeSpan.FromMinutes(5), DispatcherPriority.Background, (_, _) => SaveThermal(), Dispatcher);
        _thermalSave.Start();
        _tray = new TrayIcon(ShowFromTray, Quit, mode => _ = SendAsync(new SetMode(mode)));
        if (!BuildFeatures.IsProduction) BuildAutomation();
        Closing += OnClosing;
        Closed += OnClosed;
    }

    /// <summary>Takes a reading as if the service had sent it. A simulation feeds it by hand.</summary>
    internal Action<Snapshot> HandleSnapshot = _ => { };

    /// <summary>Shows a made-up game library and play history, for the window tests.</summary>
    internal void SimulateGames(GameLibrary library, PlayHistory history)
    {
        _library = library;
        _history = history;
        Games.AttachHistory(history);
    }

    internal void SimulateConnected() => _vm.SetConnection(ConnectionState.Connected);

    bool _termsChecked;

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        if (_termsChecked) return;
        _termsChecked = true;
        // The installer asks for the agreement itself; this covers a copy that was not installed that way, and changed terms.
        if (Legal.IsAccepted(_vm.Prefs.AcceptedTerms, TermsAcceptance.ByInstaller())) return;
        if (TermsWindow.Ask(this)) Remember(_vm.Prefs with { AcceptedTerms = Legal.TermsVersion });
        else Application.Current.Shutdown();
    }


    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ThemeManager.ApplyChrome(this);
    }

    // ---- appearance and updates ----

    readonly UpdateChecker _updates;
    string? _releaseUrl;
    ReleaseInfo? _found;
    readonly UpdateInstaller _installer;


    async void OnChannelPicked(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string t } || !Enum.TryParse<UpdateChannel>(t, out var channel) || channel == _vm.Prefs.Channel) return;
        Remember(_vm.Prefs with { Channel = channel });
        await SwitchChannelAsync(channel);
    }

    /// <summary>
    /// Choosing Stable on a beta build moves this installation to the newest stable release (even though its number is lower),
    /// and choosing Beta on a stable build moves it to the newest beta that is newer than this build. Settings are kept.
    /// Otherwise nothing is installed: the choice only decides which releases the update check offers.
    /// </summary>
    async Task SwitchChannelAsync(UpdateChannel channel)
    {
        var name = channel == UpdateChannel.Stable ? "Stable" : "Beta";
        UpdateNote.Text = $"Looking for the newest {name} version...";
        var result = await _updates.CheckAsync(AppInfo.Version, channel, anyVersion: true);
        var onBetaBuild = !BuildFeatures.IsProduction;
        var target = result.Release;
        var needsSwitch = target is not null && (channel == UpdateChannel.Stable
            ? onBetaBuild && target.Version.CompareTo(AppInfo.Version) != 0
            : !onBetaBuild && target.Version.CompareTo(AppInfo.Version) > 0);
        if (!needsSwitch)
        {
            DiagLog.Info("Update", $"Channel set to {name}; no switch of version needed");
            await CheckForUpdatesAsync(announce: true);
            UpdateNote.Text = channel == UpdateChannel.Beta && !onBetaBuild
                ? "You will be offered beta versions as they come out. There is no beta newer than this version yet."
                : UpdateNote.Text;
            return;
        }
        var canInstall = target!.InstallerUrl is not null && target.HashUrl is not null;
        DiagLog.Info("Update", $"Channel set to {name}; offering to switch to {target.Version}");
        var choice = UpdatePromptWindow.Ask(this, target, ReleaseHeadlines(target.Notes), canInstall, name);
        if (choice != UpdateChoice.UpdateNow)
        {
            UpdateNote.Text = $"The {name} channel is chosen, but this installation stays on Ember {AppInfo.Version} until you switch.";
            return;
        }
        _found = target;
        _releaseUrl = target.Url;
        ShowUpdatesSection();
        if (canInstall) OnUpdateNow(this, new RoutedEventArgs());
        else OnOpenRelease(this, new RoutedEventArgs());
    }

    void OnAutoUpdateToggled(object sender, RoutedEventArgs e) =>
        Remember(_vm.Prefs with { AutoCheckUpdates = ((System.Windows.Controls.CheckBox)sender).IsChecked == true });


    // ---- automatic update check and prompt ----

    DispatcherTimer? _updateTimer;
    string? _promptedVersion;
    bool _prompting;

    /// <summary>Looks for an update at start and then every few hours while Ember runs, when the person has not turned that off.</summary>
    void StartAutomaticUpdateChecks()
    {
        _ = Task.Delay(8000).ContinueWith(_ => Dispatcher.InvokeAsync(() => AutomaticUpdateCheckAsync()));
        _updateTimer = new DispatcherTimer(TimeSpan.FromHours(6), DispatcherPriority.Background, (_, _) => _ = AutomaticUpdateCheckAsync(), Dispatcher);
        _updateTimer.Start();
    }

    async Task AutomaticUpdateCheckAsync()
    {
        if (!_vm.Prefs.AutoCheckUpdates || _quitting) return;
        try
        {
            await CheckForUpdatesAsync(announce: false);
            if (_found is { } release) await PromptForUpdateAsync(release);
        }
        catch (Exception ex) { DiagLog.Warn("Update", "Automatic update check failed", ex); } // the next check tries again
    }

    /// <summary>Asks once per version per run (until "Remind me later" is chosen): in the window if it is open, else from the tray.</summary>
    async Task PromptForUpdateAsync(ReleaseInfo release)
    {
        var version = release.Version.ToString();
        if (_prompting || version == _promptedVersion || version == _vm.Prefs.SkippedVersion) return;
        _promptedVersion = version;
        if (!IsVisible || WindowState == WindowState.Minimized)
        {
            DiagLog.Info("Update", $"Ember {version} is available; told the person from the tray");
            _tray.Notify("Ember update available", $"Ember {version} is ready. Click to update.", () => Dispatcher.InvokeAsync(async () => { ShowFromTray(); await ShowUpdatePromptAsync(release); }));
            return;
        }
        await ShowUpdatePromptAsync(release);
    }

    async Task ShowUpdatePromptAsync(ReleaseInfo release)
    {
        if (_prompting) return;
        _prompting = true;
        try
        {
            var canInstall = release is { InstallerUrl: not null, HashUrl: not null };
            DiagLog.Info("Update", $"Asking whether to update to {release.Version}");
            var choice = UpdatePromptWindow.Ask(this, release, ReleaseHeadlines(release.Notes), canInstall);
            DiagLog.Info("Update", "Choice: " + choice);
            switch (choice)
            {
                case UpdateChoice.Skip:
                    Remember(_vm.Prefs with { SkippedVersion = release.Version.ToString() });
                    break;
                case UpdateChoice.Later:
                    _promptedVersion = null; // asked again at the next automatic check
                    break;
                case UpdateChoice.UpdateNow:
                    ShowUpdatesSection();
                    if (canInstall) OnUpdateNow(this, new RoutedEventArgs());
                    else OnOpenRelease(this, new RoutedEventArgs());
                    break;
            }
        }
        finally { _prompting = false; }
        await Task.CompletedTask;
    }

    /// <summary>Opens Settings on the Updates section, where the download and install progress is shown.</summary>
    void ShowUpdatesSection()
    {
        NavSettings.IsChecked = true;
        if (FindRadio(this, "updates") is { } radio) radio.IsChecked = true;
    }

    static System.Windows.Controls.RadioButton? FindRadio(DependencyObject parent, string tag)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>())
        {
            if (child is System.Windows.Controls.RadioButton { Tag: string t } r && t == tag) return r;
            if (FindRadio(child, tag) is { } found) return found;
        }
        return null;
    }
    async void OnCheckUpdates(object sender, RoutedEventArgs e) => await CheckForUpdatesAsync(announce: true);

    async Task CheckForUpdatesAsync(bool announce)
    {
        UpdateNote.Text = "Checking...";
        UpdateDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "MutedBrush");
        UpdateBanner.Visibility = Visibility.Collapsed;
        var result = await _updates.CheckAsync(AppInfo.Version, UpdateChannelInUse);
        _releaseUrl = result.Release?.Url;
        _found = result.Release;
        UpdateNote.Text = result.Message;
        switch (result.Status)
        {
            case UpdateStatus.UpToDate: UpdateDot.Fill = ThemeManager.Good; break;
            case UpdateStatus.Available: UpdateDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "AccentBrush"); break;
            default: UpdateDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "MutedBrush"); break;
        }
        if (result.Release is { } r)
        {
            UpdateBanner.Visibility = Visibility.Visible;
            UpdateBannerTitle.Text = $"Ember {r.Version}" + (r.Prerelease ? " (pre-release)" : "") + " is ready";
            var notes = ReleaseNotesPreview(r.Notes);
            UpdateNotes.Text = notes;
            UpdateNotes.Visibility = notes.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            UpdateNowButton.Visibility = r is { InstallerUrl: not null, HashUrl: not null } ? Visibility.Visible : Visibility.Collapsed;
            OpenReleaseButton.Visibility = Visibility.Visible;
        }
        if (announce) Announce(UpdateNote);
    }


    /// <summary>One short line per change for the update prompt: the bold lead of each bullet ("Corsair: falls back to the older SDK"), at most four.</summary>
    static string ReleaseHeadlines(string? notes)
    {
        if (string.IsNullOrWhiteSpace(notes)) return "";
        var lines = new List<string>();
        foreach (var raw in notes.Replace("\r", "").Split('\n'))
        {
            var line = raw.Trim();
            if (!line.StartsWith("- ") && !line.StartsWith("* ")) continue;
            var m = System.Text.RegularExpressions.Regex.Match(line, @"^[-*]\s+\*\*(.+?)\*\*");
            var text = m.Success ? m.Groups[1].Value : line[2..];
            text = text.Replace("`", "").Trim().TrimEnd('.', ':');
            var stop = text.IndexOf(". ", StringComparison.Ordinal);
            if (stop > 20) text = text[..stop];
            if (text.Length > 110) text = text[..107].TrimEnd() + "…";
            if (text.Length > 0) lines.Add("• " + text);
            if (lines.Count == 4) break;
        }
        return string.Join("\n", lines);
    }
    /// <summary>The release notes as plain lines: headings and bold marks dropped, bullets kept.</summary>
    static string ReleaseNotesPreview(string? notes)
    {
        if (string.IsNullOrWhiteSpace(notes)) return "";
        var lines = notes.Replace("\r", "").Split('\n')
            .Select(l => l.Trim().Replace("**", "").Replace("`", ""))
            .Where(l => l.Length > 0 && !l.StartsWith('#'))
            .Select(l => l.StartsWith("- ") ? "• " + l[2..] : l)
            .Take(6);
        return string.Join("\n", lines);
    }

    async void OnUpdateNow(object sender, RoutedEventArgs e)
    {
        if (_found is not { } release) return;
        UpdateNowButton.IsEnabled = false;
        try
        {
            var progress = new Progress<double>(p => UpdateNote.Text = $"Downloading Ember {release.Version}... {p:P0}");
            UpdateNote.Text = $"Downloading Ember {release.Version}...";
            var folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "EmberUpdate");
            var got = await _installer.DownloadAsync(release, folder, progress);
            UpdateNote.Text = got.Message;
            DiagLog.Info("Update", $"Download of {release.Version}: {(got.Ok ? "ok" : "failed")}, {got.Message}");
            if (!got.Ok || got.Path is null) return;
            UpdateNote.Text = "Starting the installer. Windows will ask for permission, then Ember closes, updates and opens again.";
            var accepted = Math.Max(_vm.Prefs.AcceptedTerms, TermsAcceptance.ByInstaller());
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(got.Path, "/UPDATE /ACCEPTED=" + accepted) { UseShellExecute = true }); }
            catch (System.ComponentModel.Win32Exception ex) { DiagLog.Warn("Update", "Installer not started", ex); UpdateNote.Text = "The installer was not started (permission was refused). Press Update now to try again."; return; }
            Quit();
        }
        finally { UpdateNowButton.IsEnabled = true; }
    }

    void OnOpenRelease(object sender, RoutedEventArgs e)
    {
        // Only a github.com release page found by the checker is ever opened.
        if (_releaseUrl is { } url && Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps && u.Host == "github.com")
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(u.AbsoluteUri) { UseShellExecute = true }); }
            catch (Exception) { UpdateNote.Text = "Could not open the browser."; }
    }
    void GrabScreen()
    {
        try
        {
            if (_lights.WantsAudio) { if (!_audio.Running) _audio.Start(); _audio.CheckSilence(); }
            else if (_audio.Running) _audio.Stop();
            if (_lights.WantsKeys) _keys.Start(); else if (_keys.Running) _keys.Stop();
            if (!_lights.WantsScreen)
            {
                _screen = null;
                _lights.SetInputs(_cpuTemp, _gpuTemp, null);
                // Nothing needs the screen: only check twice a second whether a layer has started to.
                var idle = TimeSpan.FromMilliseconds(_lights.WantsAudio ? 100 : 500);
                if (_screenTimer.Interval != idle) _screenTimer.Interval = idle;
                return;
            }
            // The graphics card hands over the frame when it can (fast); otherwise the older, slower copy through Windows.
            var monitor = _lights.ScreenMonitor;
            var zones = _dxgi.Capture(monitor: monitor) ?? ScreenGrabber.Capture(monitor: monitor);
            var wanted = TimeSpan.FromMilliseconds(_dxgi.Active ? 40 : 100);
            if (_screenTimer.Interval != wanted) _screenTimer.Interval = wanted;
            _screen = zones is null ? null : ScreenZones.Smooth(_screen, zones, 0.6);
            _lights.SetInputs(_cpuTemp, _gpuTemp, _screen);
        }
        catch (Exception) { _screen = null; /* a failed grab must never take the window down */ }
    }

    PlayHistory _history = new();
    DispatcherTimer? _playTimer;
    DateTimeOffset _lastHistorySave = DateTimeOffset.UtcNow;

    /// <summary>Every half minute: if a game from the library is the program in front, it was played. Saved at most every few minutes and when Ember closes.</summary>
    void TrackPlay()
    {
        try
        {
            if (GameLookup.ByExe(_library, GameDetectorWindows.Foreground().Exe) is { } game) _history.Played(game.Name, DateTimeOffset.Now, _playTimer!.Interval.TotalSeconds);
            if (_history.Dirty && DateTimeOffset.UtcNow - _lastHistorySave > TimeSpan.FromMinutes(5)) { _history.TrySave(PlayHistory.DefaultPath); _lastHistorySave = DateTimeOffset.UtcNow; }
        }
        catch (Exception) { /* tracking must never take the window down */ }
    }

    async void DetectGame()
    {
        try
        {
            if (!_vm.FansPage.Experimental) return; // game detection is behind the Experimental switch
            ControlMessage? message = _detection.Update(DateTimeOffset.UtcNow, GameDetectorWindows.Foreground(), _gpuLoad) switch
            {
                GameEvent.Started s => new GameStarted(s.Exe),
                GameEvent.Ended => new GameEnded(),
                GameEvent.Heartbeat => new DetectorHeartbeat(),
                _ => null,
            };
            if (message is not null) await ControlClient.SendAsync(message);
            // A game with its own fan mode (off unless switched on on the Games page): picked as a game-time mode, so it ends with the game.
            if (message is GameStarted started && _profiles.ModeFor(GameLookup.ByExe(_library, started.Exe)?.Name) is { } own)
                await ControlClient.SendAsync(new SetMode(own));
        }
        catch (Exception) { /* detection must never take the window down */ }
    }

    /// <summary>The OpenRGB that ships next to the app, if this build has one.</summary>
    public static string? BundledOpenRgb
    {
        get
        {
            var p = Path.Combine(AppContext.BaseDirectory, "OpenRGB", "OpenRGB.exe");
            return File.Exists(p) ? p : null;
        }
    }

    GoveeCloudClient? GoveeCloud() =>
        string.IsNullOrWhiteSpace(_secrets.Get(GoveeKeyName)) ? null : new GoveeCloudClient(_http, () => _secrets.Get(GoveeKeyName));

    void OnSaveGoveeKey(object sender, RoutedEventArgs e)
    {
        var key = GoveeKey.Password.Trim();
        if (key.Length == 0) { GoveeNote.Text = "Paste your Govee API key first."; Announce(GoveeNote); return; }
        GoveeNote.Text = _secrets.Set(GoveeKeyName, key) ? "Key saved in Windows Credential Manager." : "Windows would not store the key.";
        GoveeKey.Clear();
        _lights.RefreshDevices();
        Announce(GoveeNote);
    }

    async void OnTestGoveeKey(object sender, RoutedEventArgs e)
    {
        var client = GoveeCloud();
        if (client is null) { GoveeNote.Text = "Save a key first."; Announce(GoveeNote); return; }
        GoveeNote.Text = "Asking Govee…";
        try
        {
            var devices = await client.GetDevicesAsync();
            GoveeNote.Text = $"The key works. Govee lists {devices.Count} device{(devices.Count == 1 ? "" : "s")}.";
        }
        catch (Exception ex) { GoveeNote.Text = "The key did not work: " + ex.Message; }
        Announce(GoveeNote);
    }

    void OnRemoveGoveeKey(object sender, RoutedEventArgs e)
    {
        GoveeNote.Text = _secrets.Set(GoveeKeyName, null) ? "Key removed." : "Windows would not remove the key.";
        _lights.RefreshDevices();
        Announce(GoveeNote);
    }

    // ---- Game libraries ----

    GameProfiles _profiles = GameProfiles.Default;
    readonly GameProfileStore _profileStore = new(GameProfileStore.DefaultPath);
    GameLibrary _library = GameLibrary.Empty;
    readonly GameLibraryStore _libraryStore = new(GameLibraryStore.DefaultPath);
    bool _libraryScanning;

    /// <summary>Reads the launcher libraries in the background and tells game detection which programs are games.</summary>
    async Task ScanLibraryAsync()
    {
        if (_libraryScanning) return;
        _libraryScanning = true;
        try
        {
            var found = await Task.Run(GameLibraryScan.ScanAll);
            _library = found;
            _libraryStore.TrySave(found);
            _detection.SetKnownGames(found.KnownExes);
        }
        catch (Exception ex) { DiagLog.Changed("Games", "scan", "failed: " + ex.GetType().Name); }
        finally { _libraryScanning = false; }
    }

    // ---- Tapo (beta builds only) ----

    const string TapoAccountName = "tapo-account";

    Ember.Lighting.Tapo.TapoAccount? TapoSession() => Ember.Lighting.Tapo.TapoAccount.Restore(_secrets.Get(TapoAccountName));

    void OnTapoSave(object sender, RoutedEventArgs e)
    {
        var email = TapoEmail.Text.Trim();
        var password = TapoPassword.Password;
        if (email.Length == 0 || password.Length == 0) { TapoNote.Text = "Type your Tapo email and password first."; Announce(TapoNote); return; }
        if (_secrets.Set(TapoAccountName, new Ember.Lighting.Tapo.TapoAccount(email, password).Serialise()))
        {
            TapoNote.Text = "Saved. Ember looks for Tapo devices on this network now; they appear in a minute. The password stays in Windows Credential Manager.";
            TapoPassword.Clear();
            _lights.RefreshDevices();
        }
        else TapoNote.Text = "Windows would not store the account, so it was not kept.";
        Announce(TapoNote);
    }

    void OnTapoRemove(object sender, RoutedEventArgs e)
    {
        TapoNote.Text = _secrets.Set(TapoAccountName, null) ? "Removed. Ember forgot your Tapo account." : "Could not remove the account.";
        _lights.RefreshDevices();
        Announce(TapoNote);
    }

    // ---- Meross (beta builds only) ----

    const string MerossSessionName = "meross-session";

    Ember.Lighting.Meross.MerossAccount? MerossSession() => Ember.Lighting.Meross.MerossAccount.Restore(_secrets.Get(MerossSessionName));

    async void OnMerossSignIn(object sender, RoutedEventArgs e)
    {
        var email = MerossEmail.Text.Trim();
        var password = MerossPassword.Password;
        if (email.Length == 0 || password.Length == 0) { MerossNote.Text = "Type your Meross email and password first."; Announce(MerossNote); return; }
        MerossNote.Text = "Signing in to Meross…";
        Announce(MerossNote);
        try
        {
            var cloud = new Ember.Lighting.Meross.MerossCloudClient(_http);
            var account = await cloud.SignInAsync(email, password, MerossCode.Text);
            var devices = await cloud.GetDevicesAsync(account);
            var usable = devices.Count(d => d.IsLight);
            var plugCount = devices.Count(d => d.Type.StartsWith("mss", StringComparison.OrdinalIgnoreCase));
            if (!_secrets.Set(MerossSessionName, account.Serialise()))
                MerossNote.Text = "Signed in, but Windows would not store the sign-in, so it was not kept.";
            else
            {
                MerossNote.Text = $"Signed in. Meross lists {usable} bulb{(usable == 1 ? "" : "s")} or light strip{(usable == 1 ? "" : "s")}; Ember looks for them on this network and they appear on the Lights page in a few seconds. {plugCount} smart plug{(plugCount == 1 ? "" : "s")} can be linked on the Power page to read what they draw. Your password was not saved.";
                MerossPassword.Clear();
                MerossCode.Clear();
                _lights.RefreshDevices();
            }
        }
        catch (Exception ex) { MerossNote.Text = "Could not sign in: " + (ex is HttpRequestException or TaskCanceledException ? "no answer from Meross" : ex.Message); }
        Announce(MerossNote);
    }

    void OnMerossSignOut(object sender, RoutedEventArgs e)
    {
        var ok = _secrets.Set(MerossSessionName, null);
        MerossNote.Text = ok ? "Signed out. Ember forgot your Meross sign-in." : "Could not remove the sign-in.";
        MerossCode.Clear();
        _lights.RefreshDevices();
        Announce(MerossNote);
    }

    // ---- Philips Hue ----

    readonly HueSettingsStore _hueStore = new(HueSettingsStore.DefaultPath);
    HueClient? _hueClient;
    string? _hueCacheKey;

    /// <summary>The linked bridge, or null while none is linked. The same client is kept while nothing about the link changes.</summary>
    HueClient? HueConnection()
    {
        var settings = _hueStore.Load();
        var key = _secrets.Get(HueSettings.KeyName);
        if (settings is null || string.IsNullOrEmpty(settings.CertThumbprint) || string.IsNullOrWhiteSpace(key)) { _hueClient = null; _hueCacheKey = null; return null; }
        var cacheKey = settings.Host + "|" + settings.CertThumbprint + "|" + key;
        if (_hueClient is null || _hueCacheKey != cacheKey)
        {
            _hueClient = new HueClient(HuePinning.CreateClient(() => settings.CertThumbprint), settings.Host, key);
            _hueCacheKey = cacheKey;
        }
        return _hueClient;
    }

    async void OnHueFind(object sender, RoutedEventArgs e)
    {
        HueNote.Text = "Asking Signify's discovery service for bridges on your network (it sees your public address, nothing else)…";
        Announce(HueNote);
        try
        {
            var found = await HueDiscovery.FindAsync(_http);
            if (found.Count == 0) HueNote.Text = "No bridge was listed. Type its address (find it in the Hue app under Settings, Bridges) instead.";
            else { HueHost.Text = found[0]; HueNote.Text = found.Count == 1 ? "Found a bridge at " + found[0] + ". Press the button on the bridge, then Link bridge." : "Found " + string.Join(", ", found) + ". The first is filled in; change it if that is not your bridge."; }
        }
        catch (Exception ex) { HueNote.Text = "Could not ask the discovery service (" + ex.Message + "). Type the bridge address instead."; }
        Announce(HueNote);
    }

    async void OnHueLink(object sender, RoutedEventArgs e)
    {
        var host = HueHost.Text.Trim();
        if (!HueSettings.IsValidHost(host)) { HueNote.Text = "Type the bridge address first, for example 192.168.1.20."; Announce(HueNote); return; }
        HueNote.Text = "Linking… (the button on the bridge must have been pressed in the last 30 seconds)";
        Announce(HueNote);
        string? thumbprint = null;
        try
        {
            using var http = HuePinning.CreateClient(() => null, t => thumbprint = t, TimeSpan.FromSeconds(8));
            var key = await HueClient.PairAsync(http, host);
            if (string.IsNullOrEmpty(thumbprint)) { HueNote.Text = "The bridge did not show a certificate, so Ember will not link it."; Announce(HueNote); return; }
            if (!_secrets.Set(HueSettings.KeyName, key) || !_hueStore.TrySave(new HueSettings(host, thumbprint)))
            {
                HueNote.Text = "Linked, but Windows would not store the key or the settings, so it was not kept.";
                _secrets.Set(HueSettings.KeyName, null);
            }
            else HueNote.Text = "Linked to the bridge at " + host + ". Its lights appear on the Lights page in a few seconds.";
            _lights.RefreshDevices();
        }
        catch (HueLinkButtonException ex) { HueNote.Text = ex.Message; }
        catch (Exception ex) { HueNote.Text = "Could not link: " + (ex is HttpRequestException or TaskCanceledException ? "no answer from " + host : ex.Message); }
        Announce(HueNote);
    }

    void OnHueUnlink(object sender, RoutedEventArgs e)
    {
        var ok = _secrets.Set(HueSettings.KeyName, null) & _hueStore.TrySave(null);
        HueNote.Text = ok ? "Unlinked. Ember forgot the bridge and its key. (The bridge still lists Ember; remove it in the Hue app if you like.)" : "Could not remove everything.";
        _lights.RefreshDevices();
        Announce(HueNote);
    }

    // ---- Nanoleaf ----

    readonly NanoleafSettingsStore _nanoStore = new(NanoleafSettingsStore.DefaultPath);
    NanoleafClient? _nanoClient;
    string? _nanoCacheKey;
    readonly HttpClient _nanoHttp = new() { Timeout = TimeSpan.FromSeconds(4) };

    /// <summary>The linked controller, or null while none is linked.</summary>
    NanoleafClient? NanoleafConnection()
    {
        var settings = _nanoStore.Load();
        var token = _secrets.Get(NanoleafSettings.KeyName);
        if (settings is null || string.IsNullOrWhiteSpace(token)) { _nanoClient = null; _nanoCacheKey = null; return null; }
        var cacheKey = settings.Host + "|" + token;
        if (_nanoClient is null || _nanoCacheKey != cacheKey) { _nanoClient = new NanoleafClient(_nanoHttp, settings.Host, token); _nanoCacheKey = cacheKey; }
        return _nanoClient;
    }

    async void OnNanoleafFind(object sender, RoutedEventArgs e)
    {
        NanoleafNote.Text = "Searching your network for Nanoleaf controllers…";
        Announce(NanoleafNote);
        try
        {
            var found = await NanoleafDiscovery.FindAsync(TimeSpan.FromSeconds(3));
            if (found.Count == 0) NanoleafNote.Text = "None answered. Type the controller address (find it in the Nanoleaf app or your router) and try Link.";
            else { NanoleafHost.Text = found[0]; NanoleafNote.Text = "Found " + string.Join(", ", found) + ". Hold the power button on the controller for 5 to 7 seconds, then press Link within 30 seconds."; }
        }
        catch (Exception ex) { NanoleafNote.Text = "The search failed (" + ex.Message + "). Type the controller address instead."; }
        Announce(NanoleafNote);
    }

    async void OnNanoleafLink(object sender, RoutedEventArgs e)
    {
        var host = NanoleafHost.Text.Trim();
        if (!NanoleafSettings.IsValidHost(host)) { NanoleafNote.Text = "Type the controller address first, for example 192.168.1.30."; Announce(NanoleafNote); return; }
        NanoleafNote.Text = "Linking… (the controller must be in pairing mode)";
        Announce(NanoleafNote);
        try
        {
            var token = await NanoleafClient.PairAsync(_nanoHttp, host);
            if (!_secrets.Set(NanoleafSettings.KeyName, token) || !_nanoStore.TrySave(new NanoleafSettings(host)))
            {
                NanoleafNote.Text = "Linked, but Windows would not store the token or the settings, so it was not kept.";
                _secrets.Set(NanoleafSettings.KeyName, null);
            }
            else NanoleafNote.Text = "Linked to the controller at " + host + ". Its panels appear on the Lights page in a few seconds.";
            _lights.RefreshDevices();
        }
        catch (NanoleafPairingException ex) { NanoleafNote.Text = ex.Message; }
        catch (Exception ex) { NanoleafNote.Text = "Could not link: " + (ex is HttpRequestException or TaskCanceledException ? "no answer from " + host : ex.Message); }
        Announce(NanoleafNote);
    }

    void OnNanoleafUnlink(object sender, RoutedEventArgs e)
    {
        var ok = _secrets.Set(NanoleafSettings.KeyName, null) & _nanoStore.TrySave(null);
        NanoleafNote.Text = ok ? "Unlinked. Ember forgot the controller and its token." : "Could not remove everything.";
        _lights.RefreshDevices();
        Announce(NanoleafNote);
    }

    // ---- Razer Chroma ----

    readonly Ember.Lighting.Razer.RazerSettingsStore _razerStore = new(Ember.Lighting.Razer.RazerSettingsStore.DefaultPath);
    readonly HttpClient _razerHttp = new() { Timeout = TimeSpan.FromSeconds(3) };

    void OnRazerToggled(object sender, RoutedEventArgs e)
    {
        var on = ((System.Windows.Controls.CheckBox)sender).IsChecked == true;
        RazerNote.Text = _razerStore.TrySave(on)
            ? (on ? "On. Its devices appear on the Lights page in a few seconds if Razer Synapse is running with Chroma Connect on." : "Off. Ember no longer talks to Razer Synapse.")
            : "Could not save this setting.";
        _lights.RefreshDevices();
        Announce(RazerNote);
    }

    async void OnCurveCommitted(CurveGroup group, IReadOnlyList<CurvePoint> points)
    {
        var reply = await ControlClient.SendAsync(new SetCurve(group, points));
        if (!reply.Ok) Curve.ReportFailure("The service did not accept that curve: " + (reply.Error ?? "unknown reason") + ".");
    }

    // ---- AI assist ----

    /// <summary>Applies a suggestion the owner approved: both manual curves, then Manual mode. The service checks the curves again against the safety rules.</summary>
    async Task<string?> ApplySuggestionAsync(CurveSuggestion s)
    {
        if (!_vm.FansPage.Curves.Equals(ManualCurves.Default)
            && MessageBox.Show(this, "This replaces both of your manual curves with the suggestion. Continue?", "Apply the suggested curves",
                MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return "Nothing was changed.";
        foreach (var (group, points) in new[] { (CurveGroup.Cpu, s.Cpu), (CurveGroup.Case, s.Case) })
        {
            var reply = await ControlClient.SendAsync(new SetCurve(group, points));
            if (!reply.Ok) return "The service did not accept the curve: " + (reply.Error ?? "unknown reason") + ".";
        }
        var mode = await ControlClient.SendAsync(new SetMode(FanMode.Manual));
        return mode.Ok ? null : "The curves were saved but Manual mode could not be switched on: " + (mode.Error ?? "unknown reason") + ".";
    }

    void ForgetThermal()
    {
        _thermal.Clear();
        _thermalStore.Delete();
    }

    void SaveThermal()
    {
        if (_history.Dirty) _history.TrySave(PlayHistory.DefaultPath);
        if (_thermal.LastSample is not null) _thermalStore.TrySave(_thermal);
        Power.SaveLedger();
    }

    async void OnCurveCopy(IReadOnlyList<CurvePoint> points)
    {
        if (!_vm.FansPage.Curves.Equals(ManualCurves.Default)
            && MessageBox.Show(this, "This replaces both of your manual curves with the one shown. Continue?", "Customise this curve",
                MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        foreach (var group in new[] { CurveGroup.Cpu, CurveGroup.Case })
        {
            var reply = await ControlClient.SendAsync(new SetCurve(group, points));
            if (!reply.Ok) { Curve.ReportFailure("The service did not accept that curve: " + (reply.Error ?? "unknown reason") + "."); return; }
        }
        await SendAsync(new SetMode(FanMode.Manual));
    }

    async void OnExperimentalToggled(object sender, RoutedEventArgs e)
    {
        var on = ExperimentalSwitch.IsChecked == true;
        var reply = await ControlClient.SendAsync(new SetExperimental(on));
        SettingsNote.Text = reply.Ok
            ? (on ? "Experimental features are on: the Games panel and the GPU fan option now show on the Fans page." : "Experimental features are off.")
            : "Could not change it: " + (reply.Error ?? "the service did not answer.");
        if (!reply.Ok) ExperimentalSwitch.IsChecked = !on;
        Announce(SettingsNote);
    }

    async void OnGpuControl(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.CheckBox box) await SendAsync(new SetGpuControl(box.IsChecked == true));
    }

    async void OnCloseCompetitors(object sender, RoutedEventArgs e)
    {
        var names = _vm.FansPage.Competitors;
        if (names.Count == 0) return;
        var ask = MessageBox.Show(this,
            $"Close {string.Join(", ", names)} now?\n\nThey will be ended if they do not close on their own, and anything unsaved in them is lost.",
            "Close fan-control apps", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (ask != MessageBoxResult.Yes) return;
        var result = await CompetitorCloser.CloseAsync(names);
        ActionLabel.Text = result.NeedAdmin.Count > 0
            ? $"Could not close {string.Join(", ", result.NeedAdmin)}: restart Ember as administrator and try again."
            : result.Closed.Count > 0 ? "Closed " + string.Join(", ", result.Closed) + "." : "Nothing to close.";
        Announce(ActionLabel);
    }

    void OnRestartAsAdmin(object sender, RoutedEventArgs e)
    {
        if (CompetitorCloser.RestartAsAdministrator()) Application.Current.Shutdown();
        else { ActionLabel.Text = "Ember was not restarted: the administrator prompt was cancelled."; Announce(ActionLabel); }
    }

    async void OnNotAGame(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string exe }) await SendAsync(new SetNotGame(exe, true));
    }

    async void OnUndoNotAGame(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string exe }) await SendAsync(new SetNotGame(exe, false));
    }

    void OnSettingsSection(object sender, RoutedEventArgs e)
    {
        if (SettingsSections is null || sender is not FrameworkElement { Tag: string section }) return;
        foreach (var child in SettingsSections.Children.OfType<FrameworkElement>())
            if (child.Tag is string t) child.Visibility = t == section ? Visibility.Visible : Visibility.Collapsed;
    }

    void OnNavChanged(object sender, RoutedEventArgs e)
    {
        if (DashboardPage is null || FansPageView is null || SettingsPageView is null || LightsPageView is null || DevicesPageView is null || NewDashboardPage is null || PowerPageView is null || GamesPageView is null || SpecsPageView is null) return;
        SpecsPageView.Visibility = NavSpecs.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        if (NavSpecs.IsChecked == true) _ = Specs.LoadAsync();
        GamesPageView.Visibility = NavGames.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        // Opening the page scans again when the last scan is more than five minutes old.
        if (NavGames.IsChecked == true && DateTimeOffset.Now - _library.ScannedAt > TimeSpan.FromMinutes(5)) _ = ScanLibraryAsync();
        PowerPageView.Visibility = NavPower.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        DevicesPageView.Visibility = NavDevices.IsChecked == true ? Visibility.Visible : Visibility.Collapsed; // fires once during InitializeComponent
        LightsPageView.Visibility = NavLights.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        DashboardPage.Visibility = NavDashboard.IsChecked == true && !BuildFeatures.NewLook ? Visibility.Visible : Visibility.Collapsed;
        NewDashboardPage.Visibility = NavDashboard.IsChecked == true && BuildFeatures.NewLook ? Visibility.Visible : Visibility.Collapsed;
        FansPageView.Visibility = NavFans.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        SettingsPageView.Visibility = NavSettings.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    void OnUnitPicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string u } && Enum.TryParse<TemperatureUnit>(u, out var unit))
            Remember(_vm.Prefs with { Unit = unit });
    }

    void OnStartPagePicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string p } && Enum.TryParse<StartPage>(p, out var page))
            Remember(_vm.Prefs with { StartPage = page });
    }

    void Remember(AppPreferences prefs)
    {
        var look = prefs.Theme != _vm.Prefs.Theme || prefs.Accent != _vm.Prefs.Accent || prefs.Tone != _vm.Prefs.Tone || prefs.Highlight != _vm.Prefs.Highlight || prefs.CustomAccent != _vm.Prefs.CustomAccent || prefs.GpuColour != _vm.Prefs.GpuColour || prefs.Size != _vm.Prefs.Size;
        SettingsNote.Text = _vm.Update(prefs) ? "" : "Could not save this setting; it applies until you close Ember.";
        if (look) ApplyAppearance();
        Announce(SettingsNote);
    }

    void OnStartupToggled(object sender, RoutedEventArgs e)
    {
        var want = StartupSwitch.IsChecked == true;
        if (!StartupRegistration.Set(want, _vm.Prefs.StartMinimised))
        {
            StartupSwitch.IsChecked = StartupRegistration.IsEnabled();
            SettingsNote.Text = "Windows would not let Ember change its start-up entry.";
        }
        else SettingsNote.Text = want ? "Ember will open when you sign in." : "Ember will no longer open when you sign in.";
        Announce(SettingsNote);
    }

    void OnTrayToggled(object sender, RoutedEventArgs e) =>
        Remember(_vm.Prefs with { KeepInTray = TraySwitch.IsChecked == true });

    void OnMinimisedToggled(object sender, RoutedEventArgs e)
    {
        var want = MinimisedSwitch.IsChecked == true;
        Remember(_vm.Prefs with { StartMinimised = want });
        if (StartupRegistration.IsEnabled()) StartupRegistration.Set(true, want); // the sign-in entry carries the switch
    }

    // ---- tray ----

    /// <summary>False when the window has to be shown at start: the terms have not been accepted yet.</summary>
    public bool CanStayHidden => Legal.IsAccepted(_vm.Prefs.AcceptedTerms, TermsAcceptance.ByInstaller());

    internal void ShowFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        Refresh();
    }

    void Quit()
    {
        _quitting = true;
        Close();
    }

    void OnRememberWindowToggled(object sender, RoutedEventArgs e)
    {
        var on = RememberWindowSwitch.IsChecked == true;
        Remember(_vm.Prefs with { RememberWindow = on, Window = on ? _vm.Prefs.Window : "" });
        if (on) SaveWindowPlacement();
    }

    /// <summary>Puts the window where it was when it was last closed (only with "Open the window where I left it"), moved onto the screen if that place is gone.</summary>
    void RestoreWindowPlacement()
    {
        if (!_vm.Prefs.RememberWindow || !WindowPlacement.TryParse(_vm.Prefs.Window, out var saved) || saved is null) return;
        var fit = saved.Fit(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight, MinWidth, MinHeight);
        WindowStartupLocation = WindowStartupLocation.Manual;
        (Left, Top, Width, Height) = (fit.Left, fit.Top, fit.Width, fit.Height);
        if (fit.Maximised) WindowState = WindowState.Maximized;
    }

    void SaveWindowPlacement()
    {
        if (!_vm.Prefs.RememberWindow || (WindowState == WindowState.Minimized && !IsVisible)) return;
        var b = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        if (b.Width <= 0 || b.Height <= 0 || double.IsInfinity(b.Width) || double.IsNaN(b.Left)) return;
        var text = new WindowPlacement(b.Left, b.Top, b.Width, b.Height, WindowState == WindowState.Maximized).Format();
        if (text != _vm.Prefs.Window) _vm.Update(_vm.Prefs with { Window = text });
    }

    void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        SaveWindowPlacement();
        // With "Keep running in the tray" on, the close button only hides the window. Quit in the tray menu really closes it.
        if (_quitting || !_vm.Prefs.KeepInTray) return;
        e.Cancel = true;
        Hide();
    }

    void OnOpenSettingsFolder(object sender, RoutedEventArgs e)
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Ember");
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", "\"" + dir + "\"") { UseShellExecute = true }); }
        catch (Exception) { SettingsNote.Text = "Could not open " + dir + "."; Announce(SettingsNote); }
    }
    async void OnModePicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: FanMode mode }) await SendAsync(new SetMode(mode));
    }

    /// <summary>The switch of a fan header (redesign): on lets Ember control that fan, off hands it back to the board.</summary>
    async void OnHeaderSwitch(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string id, DataContext: HeaderRow row }) return;
        if (row.CanDisable) await SendAsync(new ConfirmHeader(id, false));
        else if (row.CanEnable) await SendAsync(new ConfirmHeader(id, true));
        else if (sender is System.Windows.Controls.Primitives.ToggleButton t) t.IsChecked = row.CanDisable;   // nothing can change: show the real state again
    }

    async void OnTestHeader(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string id }) await SendAsync(new RunHeaderTest(id));
    }

    async void OnEnableHeader(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string id }) await SendAsync(new ConfirmHeader(id, true));
    }

    async void OnDisableHeader(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string id }) await SendAsync(new ConfirmHeader(id, false));
    }

    async Task SendAsync(ControlMessage message)
    {
        var reply = await ControlClient.SendAsync(message);
        ActionLabel.Text = reply.Ok ? "" : reply.Error ?? "That did not work.";
        Announce(ActionLabel);
    }

    void OnChartSizeChanged(object sender, SizeChangedEventArgs e)
    {
        _vm.SetChartSize(e.NewSize.Width, e.NewSize.Height);
        Refresh();
    }

    DashboardInputs DashboardInputsNow()
    {
        var state = _lights.State;
        return new DashboardInputs(_summary, _powerNow, Power.TotalWatts, Power.TotalFromPlug, state.Current.Name, state.On, _lights.Devices.Count(d => !d.Disabled),
            _detection.ActiveGame, _vm.FansPage.Experimental, FansViewState.Label(_vm.FansPage.Mode));
    }

    bool Hidden => !IsVisible || WindowState == WindowState.Minimized;
    bool _trimmed;

    [System.Runtime.InteropServices.DllImport("kernel32.dll")] static extern bool SetProcessWorkingSetSize(IntPtr process, nint min, nint max);

    /// <summary>Once per trip to the tray: collect garbage and hand unused pages back to Windows (they come back on demand when the window opens).</summary>
    static void TrimMemory()
    {
        try
        {
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            SetProcessWorkingSetSize(System.Diagnostics.Process.GetCurrentProcess().Handle, -1, -1);
        }
        catch (Exception ex) { DiagLog.Warn("App", "Trimming memory failed", ex); }
    }

    internal void Refresh()
    {
        // In the tray nobody sees the pages: keep the tray tip current, redraw rarely, and draw everything the moment the window shows.
        var every = TimeSpan.FromMilliseconds(Hidden ? 2000 : 500);
        if (_refresh.Interval != every) _refresh.Interval = every;
        if (Hidden)
        {
            _tray.SetMode(_vm.FansPage.Mode, _cpuTemp is { } c ? $"CPU {c:0} °C" : null);
            if (!_trimmed) { _trimmed = true; TrimMemory(); }
            return;
        }
        _trimmed = false;
        _vm.Refresh(DateTimeOffset.UtcNow);
        _chart.Update(_vm.ChartWidth, _vm.ChartHeight, _vm.Unit, _vm.CpuLine, _vm.GpuLine, _vm.CpuText, _vm.GpuText);
        if (BuildFeatures.NewLook && NewDashboardPage.Visibility == Visibility.Visible) NewDash.Update(_vm, DashboardInputsNow());
        if (DevicesPageView.Visibility == Visibility.Visible) Devices.Update(_vm, _lights);
        if (PowerPageView.Visibility == Visibility.Visible) Power.Update();
        if (GamesPageView.Visibility == Visibility.Visible) Games.Update(_library, _libraryScanning, _detection.ActiveGame, _vm.FansPage.Experimental);
        // WPF only announces a live region when the app raises LiveRegionChanged itself.
        Curve.SetState(_vm.FansPage.Mode, _vm.FansPage.Curves, _cpuTemp, _caseTemp);
        _ai.Refresh();
        _tray.SetMode(_vm.FansPage.Mode, _cpuTemp is { } t ? $"CPU {t:0} °C" : null);
        if (_vm.ConnectionText != _announcedConnection)
        {
            _announcedConnection = _vm.ConnectionText;
            Announce(ConnectionLabel);
        }
        if (_vm.DriverBannerText != _announcedBanner)
        {
            _announcedBanner = _vm.DriverBannerText;
            if (_announcedBanner is not null) Announce(BannerLabel);
        }
        if (_vm.FansPage.CompetitorWarning != _announcedCompetitor)
        {
            _announcedCompetitor = _vm.FansPage.CompetitorWarning;
            if (_announcedCompetitor is not null) Announce(CompetitorLabel);
        }
        if (_vm.FansPage.TestMessage != _announcedTest)
        {
            _announcedTest = _vm.FansPage.TestMessage;
            if (_announcedTest is not null) Announce(TestLabel);
        }
    }

    static void Announce(UIElement element)
    {
        if (!AutomationPeer.ListenerExists(AutomationEvents.LiveRegionChanged)) return;
        UIElementAutomationPeer.CreatePeerForElement(element)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }

    void OnClosed(object? sender, EventArgs e)
    {
        _refresh.Stop();
        _gameTimer.Stop();
        _screenTimer.Stop();
        _scheduleTimer?.Stop();
        _thermalSave.Stop();
        SaveThermal();
        _tray.Dispose();
        _audio.Dispose();
        _keys.Dispose();
        _dxgi.Dispose();
        _cts.Cancel();
        try { _appControl?.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(2)); } catch (Exception) { /* closing anyway */ }
        try { _lights.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(3)); } catch (Exception) { /* closing anyway */ }
        // RunAsync returns on cancellation and never needs the UI thread, so a short wait cannot deadlock.
        _run.Wait(TimeSpan.FromSeconds(2));
    }
}
