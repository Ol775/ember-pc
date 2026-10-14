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
    readonly ISecretStore _secrets = new WindowsCredentialStore();
    readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };
    const string GoveeKeyName = "govee-api-key";
    readonly DispatcherTimer _refresh;
    readonly Task _run;
    readonly GameDetection _detection = new([]);
    readonly DispatcherTimer _gameTimer, _screenTimer;
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
    Ember.Lighting.Color[]? _screen;
    double? _gpuLoad, _cpuTemp, _gpuTemp, _caseTemp;
    string? _announcedConnection, _announcedBanner, _announcedCompetitor, _announcedTest;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm;

        LightingManager? lights = null;
        _lights = lights = new LightingManager(new LightingStore(LightingStore.DefaultPath), TimeProvider.System,
            [new OpenRgbSource(() => string.IsNullOrWhiteSpace(lights?.State.OpenRgbPath) ? BundledOpenRgb : lights!.State.OpenRgbPath, () => lights?.State.ShouldStartOpenRgb ?? true),
             new GoveeSource(new GoveeLanClient(), GoveeCloud, id => lights?.SegmentsOf(id) ?? 1),
             new Ember.Lighting.Logitech.LogitechSource(() => Ember.Lighting.Logitech.NativeLogitechLed.TryLoad(), () => lights?.State.LogitechOneColour ?? false),
             .. BuildFeatures.Current.NewIntegrations ? new IDeviceSource[] { new Ember.Lighting.Hue.HueSource(HueConnection),
             new Ember.Lighting.Nanoleaf.NanoleafSource(NanoleafConnection),
             new Ember.Lighting.Razer.RazerSource(() => _razerStore.Enabled, new Ember.Lighting.Razer.RazerChroma(_razerHttp)),
             new Ember.Lighting.Corsair.CorsairSource(Ember.Lighting.Corsair.CorsairApiChain.Open) } : []]);
        Lights.Attach(_lights);
        _lights.Start();

        // ServiceConnection raises events on its background loop thread: queue each one onto the UI thread.
        // InvokeAsync is the non-blocking BeginInvoke, so the loop never waits for the UI.
        var connection = new ServiceConnection(PipeStreamConnector.ConnectAsync, ServiceConnection.DefaultBackoff);
        connection.StateChanged += s => { DiagLog.Changed("Service", "connection", s.ToString()); Dispatcher.InvokeAsync(() => _vm.SetConnection(s)); };
        connection.SnapshotReceived += s => Dispatcher.InvokeAsync(() =>
        {
            _vm.Add(s);
            _gpuLoad = s.Readings.FirstOrDefault(r => r.Hardware == HardwareKind.Gpu && r.Kind == SensorKind.Load && r.Name == "GPU Core")?.Value;
            if (s.Control is { } c) _detection.SetNotGames(c.NotGames);
            var sum = DashboardSummary.From(s);
            _cpuTemp = s.Stale ? null : sum.CpuTemp;
            _gpuTemp = s.Stale ? null : sum.GpuTemp;
            _lights.SetInputs(s.Stale ? null : sum.CpuTemp, s.Stale ? null : sum.GpuTemp, _screen);
            _caseTemp = s.Stale ? null : new[] { sum.CpuTemp, sum.GpuTemp, sum.MotherboardTemp }.Where(v => v is not null).Max();
            if (_vm.Prefs.AiLearning && !s.Stale) _thermal.Add(DateTimeOffset.UtcNow, _cpuTemp, _caseTemp);
        });

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
        if (BuildFeatures.Current.GameDetection) _gameTimer.Start(); // beta feature: off in the alpha build

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
        StartAutomaticUpdateChecks();
        if (_vm.Prefs.StartPage == StartPage.Fans) NavFans.IsChecked = true;
        Curve.Committed += OnCurveCommitted;
        Curve.CopyRequested += OnCurveCopy;

        _run = Task.Run(() => connection.RunAsync(_cts.Token));
        _ai = new AiPanel(_thermal, () => _vm.Prefs, p => Remember(p), ApplySuggestionAsync, ForgetThermal);
        AiHost.Children.Add(_ai);
        _thermalSave = new DispatcherTimer(TimeSpan.FromMinutes(5), DispatcherPriority.Background, (_, _) => SaveThermal(), Dispatcher);
        _thermalSave.Start();
        _tray = new TrayIcon(ShowFromTray, Quit, mode => _ = SendAsync(new SetMode(mode)));
        Closing += OnClosing;
        Closed += OnClosed;
    }

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


    void OnChannelPicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string t } && Enum.TryParse<UpdateChannel>(t, out var c))
            Remember(_vm.Prefs with { Channel = c });
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
        await CheckForUpdatesAsync(announce: false);
        if (_found is { } release) await PromptForUpdateAsync(release);
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
            if (!_lights.WantsScreen) { _screen = null; _lights.SetInputs(_cpuTemp, _gpuTemp, null); return; }
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
        GoveeNote.Text = "Asking Goveeâ€¦";
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

    void SaveThermal() { if (_thermal.LastSample is not null) _thermalStore.TrySave(_thermal); }

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
        if (DashboardPage is null || FansPageView is null || SettingsPageView is null || LightsPageView is null || DevicesPageView is null) return;
        DevicesPageView.Visibility = NavDevices.IsChecked == true ? Visibility.Visible : Visibility.Collapsed; // fires once during InitializeComponent
        LightsPageView.Visibility = NavLights.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        DashboardPage.Visibility = NavDashboard.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
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
        var look = prefs.Theme != _vm.Prefs.Theme || prefs.Accent != _vm.Prefs.Accent || prefs.Tone != _vm.Prefs.Tone || prefs.Highlight != _vm.Prefs.Highlight || prefs.CustomAccent != _vm.Prefs.CustomAccent;
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
    }

    void Quit()
    {
        _quitting = true;
        Close();
    }

    void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
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

    void Refresh()
    {
        _vm.Refresh(DateTimeOffset.UtcNow);
        _chart.Update(_vm.ChartWidth, _vm.ChartHeight, _vm.Unit, _vm.CpuLine, _vm.GpuLine, _vm.CpuText, _vm.GpuText);
        if (DevicesPageView.Visibility == Visibility.Visible) Devices.Update(_vm, _lights);
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
        _thermalSave.Stop();
        SaveThermal();
        _tray.Dispose();
        _audio.Dispose();
        _keys.Dispose();
        _dxgi.Dispose();
        _cts.Cancel();
        try { _lights.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(3)); } catch (Exception) { /* closing anyway */ }
        // RunAsync returns on cancellation and never needs the UI thread, so a short wait cannot deadlock.
        _run.Wait(TimeSpan.FromSeconds(2));
    }
}
