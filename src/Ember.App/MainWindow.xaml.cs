using System.Net.Http;
using System.IO;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Threading;
using Ember.Core;
using Ember.Lighting;
using Ember.Lighting.Govee;
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
             new Ember.Lighting.Logitech.LogitechSource(() => Ember.Lighting.Logitech.NativeLogitechLed.TryLoad())]);
        Lights.Attach(_lights);
        _lights.Start();

        // ServiceConnection raises events on its background loop thread: queue each one onto the UI thread.
        // InvokeAsync is the non-blocking BeginInvoke, so the loop never waits for the UI.
        var connection = new ServiceConnection(PipeStreamConnector.ConnectAsync, ServiceConnection.DefaultBackoff);
        connection.StateChanged += s => Dispatcher.InvokeAsync(() => _vm.SetConnection(s));
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
        OnSettingsSection(SetGeneral, new RoutedEventArgs());
        _updates = new UpdateChecker(_http);
        BuildAccentRow();
        DocView.Fill(ChangelogHost, DocView.ReadEmbedded("CHANGELOG.md"));
        DocView.Fill(RoadmapHost, DocView.ReadEmbedded("ROADMAP.md"));
        BuildLegal();
        ApplyAppearance();
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += (_, _) => { if (_vm.Prefs.Theme == ThemeMode.System) Dispatcher.InvokeAsync(ApplyAppearance); };
        if (_vm.Prefs.AutoCheckUpdates) _ = Task.Delay(8000).ContinueWith(_ => Dispatcher.InvokeAsync(() => CheckForUpdatesAsync(announce: false)));
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

    /// <summary>Settings, Legal: the terms, the privacy notice, the third-party notices and the licence, all kept inside the app.</summary>
    void BuildLegal()
    {
        var docs = new (string Label, string Resource, bool Plain)[]
        {
            ("Terms of use", "TERMS.md", false), ("Privacy", "PRIVACY.md", false),
            ("Third-party notices", "THIRD-PARTY.md", false), ("Licence", "LICENSE.txt", true),
        };
        var body = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        void Show(int i)
        {
            var (_, resource, plain) = docs[i];
            var text = DocView.ReadEmbedded(resource);
            if (!plain) { DocView.Fill(body, text); return; }
            body.Children.Clear();
            var t = new TextBlock { Text = text ?? "Nothing to show.", TextWrapping = TextWrapping.Wrap };
            t.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            body.Children.Add(t);
        }
        LegalHost.Children.Add(LightsUi.Segments(docs.Select(d => d.Label).ToList(), 0, Show, "LegalDoc"));
        LegalHost.Children.Add(body);
        Show(0);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ThemeManager.ApplyChrome(this);
    }

    // ---- appearance and updates ----

    readonly UpdateChecker _updates;
    string? _releaseUrl;

    void ApplyAppearance()
    {
        ThemeManager.Apply(_vm.Prefs);
        ThemeManager.ApplyChrome(this);
        foreach (var r in AccentRow.Children.OfType<System.Windows.Controls.RadioButton>())
            r.IsChecked = r.Tag is string t && Enum.TryParse<AccentChoice>(t, out var a) && a == _vm.Prefs.Accent;
    }

    void BuildAccentRow()
    {
        foreach (var accent in Enum.GetValues<AccentChoice>())
        {
            var swatch = new System.Windows.Controls.RadioButton
            {
                GroupName = "Accent", Tag = accent.ToString(), Style = (Style)FindResource("Swatch"),
                Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(ThemePalette.For(true, accent)["AccentBrush"])),
            };
            System.Windows.Automation.AutomationProperties.SetName(swatch, accent + " accent");
            swatch.Click += (_, _) => Remember(_vm.Prefs with { Accent = accent });
            AccentRow.Children.Add(swatch);
        }
    }

    void OnThemePicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string t } && Enum.TryParse<ThemeMode>(t, out var mode))
            Remember(_vm.Prefs with { Theme = mode });
    }

    void OnChannelPicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string t } && Enum.TryParse<UpdateChannel>(t, out var c))
            Remember(_vm.Prefs with { Channel = c });
    }

    void OnAutoUpdateToggled(object sender, RoutedEventArgs e) =>
        Remember(_vm.Prefs with { AutoCheckUpdates = ((System.Windows.Controls.CheckBox)sender).IsChecked == true });

    async void OnCheckUpdates(object sender, RoutedEventArgs e) => await CheckForUpdatesAsync(announce: true);

    async Task CheckForUpdatesAsync(bool announce)
    {
        UpdateNote.Text = "Checking...";
        OpenReleaseButton.Visibility = Visibility.Collapsed;
        var result = await _updates.CheckAsync(AppInfo.Version, _vm.Prefs.Channel);
        _releaseUrl = result.Release?.Url;
        UpdateNote.Text = result.Message;
        OpenReleaseButton.Visibility = _releaseUrl is null ? Visibility.Collapsed : Visibility.Visible;
        if (announce) Announce(UpdateNote);
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
            var zones = _dxgi.Capture() ?? ScreenGrabber.Capture();
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
        var look = prefs.Theme != _vm.Prefs.Theme || prefs.Accent != _vm.Prefs.Accent;
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

    void ShowFromTray()
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
