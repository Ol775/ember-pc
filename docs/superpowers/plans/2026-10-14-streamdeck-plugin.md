# Stream Deck plugin (beta) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A beta-only Stream Deck plugin (action keys and live readout keys) that Ember can install into the Stream Deck app from a card on the Devices page.

**Architecture:** The plugin is a small C# exe (`src/Ember.StreamDeck`) that speaks Stream Deck's local WebSocket protocol and reuses `Ember.Core`: it reads CPU/GPU readings and sets fan modes through the existing service pipes (`ember.v1`, `ember.ctl.v1`), and talks to the running app through a new per-user pipe (`ember.app.v1`) for lights and "show window". The release script packs the plugin into `Ember.streamDeckPlugin`, ships it inside the install folder, and the Devices-page card installs it by opening that file.

**Tech Stack:** .NET 8 (net8.0-windows), `System.Net.WebSockets.ClientWebSocket`, named pipes, xUnit, Stream Deck SDK v2 manifest (HTML property inspector, no Node).

**Spec:** `docs/superpowers/specs/2026-10-14-streamdeck-plugin-design.md`

## Global Constraints

- Beta only: the card, the install option and the bundled plugin file are hidden when `BuildFeatures.IsProduction`; every label says "(beta)".
- Fan safety: the plugin only sends `SetMode` for `SuperQuiet, Quiet, Normal, Performance, Extreme` (the tray's order). No new fan message; no `Manual`/`Ai`; the service checks stay in charge.
- No Node.js, Python or any runtime beyond the .NET 8 Desktop Runtime Ember already needs.
- The plugin never reaches the internet and never starts a second Ember (open key uses the app's single-instance show event, and starts the exe only if no Ember is running).
- Only local, same-user access: `ember.app.v1` allows only the creating user's SID and uses `FirstPipeInstance`.
- `TreatWarningsAsErrors` is on; central package management (`Directory.Packages.props`).
- Commits: `git -c user.name=Ol775 -c user.email=41142034+Ol775@users.noreply.github.com commit`, NO `Claude-Session` trailer, no personal or PC details in files.
- CHANGELOG bullets are user-facing with a bold lead. Add a section to `docs/TESTING.md` for everything only hardware can show.
- Constants: plugin UUID `dev.ember.pc` (builder may change before first publish), actions `dev.ember.pc.scene`, `.power`, `.brightness`, `.fanmode`, `.cyclemode`, `.open`, `.readout`; app pipe name `EmberNames.AppPipeName = "ember.app.v1"`.

## Review Focus

- Ember not running: every key shows "Ember offline" and presses do nothing (except Open Ember, which starts it); no exception, no hang, reconnect within ~2 s of Ember starting.
- Service not running or snapshot `Stale`: readout keys show a dash, never `0` or an old number.
- No GPU / no CPU fan reading, or a scene that was deleted since the key was set: dash / `showAlert`, no crash.
- Garbage or oversized line on `ember.app.v1`, or a second client holding the pipe: decoder rejects it, server keeps serving (mirrors `ControlProtocol` rules).
- Stream Deck app restarts or the plugin is launched twice: the plugin exits cleanly when the WebSocket closes; a second Ember instance is never created.

---

### Task 1: Spike - does Stream Deck accept an unsigned plugin? (throwaway)

**Files:**
- Create (throwaway, delete at end of task): `%TEMP%\ember-sd-spike\dev.ember.spike.sdPlugin\manifest.json`, a 20-line `hello.exe` built from a scratch console project outside the repo.

**Interfaces:** Produces: a yes/no answer written into the spec's "More calls" section, and the verified manifest keys (`SDKVersion`, `CodePath`, `OS`, `Actions[].UUID`, `Actions[].Controllers`) that Task 6 copies.

- [ ] **Step 1:** Create a scratch plugin whose exe connects to the WebSocket given by `-port`, sends `{"event":"registerPlugin","uuid":<-pluginUUID>}`, and on `keyDown` calls `setTitle` with "hi". Pack it as a `.streamDeckPlugin` (zip with the `.sdPlugin` folder at its root).
- [ ] **Step 2:** Open the file (`Start-Process`). Expected: Stream Deck shows its install prompt (the owner must click it) and the action appears in the action list. Record whether any "unverified publisher" refusal blocks it.
- [ ] **Step 3:** Put the action on a key, press it; Expected: key title becomes "hi". Check which `-port/-pluginUUID/-registerEvent/-info` arguments arrive.
- [ ] **Step 4:** If Stream Deck refuses unsigned plugins, STOP and report to the owner. Otherwise note the result in the spec, uninstall the spike (delete `%APPDATA%\Elgato\StreamDeck\Plugins\dev.ember.spike.sdPlugin` after quitting Stream Deck), delete the scratch folder, commit the spec note.

### Task 2: App control protocol and server (Ember.Core)

**Files:**
- Create: `src/Ember.Core/AppProtocol.cs`, `src/Ember.Core/AppPipeServer.cs`
- Modify: `src/Ember.Core/EmberNames.cs` (add `AppPipeName`)
- Test: `tests/Ember.Core.Tests/AppProtocolTests.cs`, `tests/Ember.Core.Tests/AppPipeServerTests.cs`

**Interfaces:**
- Produces:
  - `public abstract record AppRequest;` with `AppStatusRequest`, `SelectSceneRequest(string Name)`, `SetOnRequest(bool On)`, `ToggleOnRequest`, `NudgeBrightnessRequest(double Delta)`, `ShowWindowRequest`.
  - `public sealed record AppReply(bool Ok, string? Error, AppStatus? Status)`; `public sealed record AppStatus(bool On, string CurrentScene, IReadOnlyList<string> Scenes, double Brightness)`.
  - `public static class AppProtocol { const int Version = 1; const int MaxLineBytes = 8192; string Encode(AppRequest); bool TryDecode(string line, out AppRequest?); string EncodeReply(AppReply); bool TryDecodeReply(string line, out AppReply?); }` - JSON lines like `ControlProtocol`, never throws, strict values (`Delta` finite and within -1..1, scene name 1..60 chars, at most 50 scenes in a reply).
  - `public sealed class AppPipeServer(Func<AppRequest, AppReply> handle, string pipeName, ILogger<AppPipeServer>? log = null) : IAsyncDisposable { void Start(); }` - one request line, one reply line per connection; max 4 live connections, 20 messages/s, 60 s idle, bad line gets `AppReply(false, "invalid", null)` and a close; pipe created with `FirstPipeInstance` and an ACL allowing only `WindowsIdentity.GetCurrent().User` (FullControl) - copy the structure of `ControlPipeServer`/`PipeSecurityFactory`, but it lives in Core so the App can host it and tests can run it.
- Consumes: `FanMode`-style conventions from `ControlProtocol`.

- [ ] **Step 1: Write failing tests** in `AppProtocolTests`: `RoundTripsEveryRequest`, `RejectsUnknownOpWrongVersionOversizedAndNullFields`, `RejectsNonFiniteOrHugeBrightnessDelta` (NaN, 5), `ReplyRoundTripKeepsSceneOrder`, `DecodeNeverThrowsOnGarbage` (empty, `{`, `[]`, 9000-byte line). In `AppPipeServerTests` (use a unique pipe name per test): `AnswersStatusRequest`, `ReturnsInvalidForGarbageAndKeepsServing` (send garbage on one connection, a valid request on the next gets an answer), `RefusesBeyondMaxConnections`.
- [ ] **Step 2:** `dotnet test tests/Ember.Core.Tests --filter "FullyQualifiedName~App"`; Expected: FAIL (types missing).
- [ ] **Step 3:** Implement the types above (Dto classes with `JsonRequired` on `v`/`op`, as in `ControlProtocol`); add `public const string AppPipeName = "ember.app.v1";`.
- [ ] **Step 4:** Same command; Expected: PASS. Then run `dotnet test Ember.sln -c Release`; Expected: all green.
- [ ] **Step 5:** Commit "App control pipe protocol and server".

### Task 3: The app answers on the pipe (lights and show window)

**Files:**
- Create: `src/Ember.App/AppControl.cs`
- Modify: `src/Ember.App/MainWindow.xaml.cs` (create and start it next to `_lights.Start()`; dispose on quit), `src/Ember.App/MainWindow.Pages.cs` only if a helper is needed
- Create (testable part, in Core because the App has no test project): `src/Ember.Core/AppRequestHandler.cs`
- Test: `tests/Ember.Core.Tests/AppRequestHandlerTests.cs`

**Interfaces:**
- Produces: `public sealed class AppRequestHandler(IAppLights lights, Action showWindow) { public AppReply Handle(AppRequest r); }` and `public interface IAppLights { bool On {get;} string CurrentScene {get;} IReadOnlyList<string> SceneNames {get;} double Brightness {get;} void SelectScene(string name); void SetOn(bool on); void SetBrightness(double value); }` in `Ember.Core` (so it can be tested); `AppControl` in the App adapts `LightingManager` (`State.On`, `State.CurrentScene`, `State.Scenes`, `SelectScene`, `SetOn`, `SetBrightness`, brightness is the current scene's `Brightness`) to `IAppLights`, marshals calls to `Dispatcher` if needed (LightingManager locks internally; confirm), and passes `() => window.Dispatcher.InvokeAsync(window.ShowFromTray)` as `showWindow`.
- Consumes: Task 2 types.

- [ ] **Step 1: Failing tests** in `AppRequestHandlerTests` with a fake `IAppLights`: `StatusReturnsSceneListAndState`; `SelectSceneChangesSceneAndReturnsStatus`; `SelectUnknownSceneIsRefused` (Ok false, error "That scene is not in Ember."); `ToggleFlipsOn`; `NudgeBrightnessClampsToZeroAndOne` (0.95 + 0.2 -> 1.0, 0.05 - 0.2 -> 0.0); `ShowWindowCallsCallback`.
- [ ] **Step 2:** `dotnet test tests/Ember.Core.Tests --filter "FullyQualifiedName~AppRequestHandler"`; Expected: FAIL.
- [ ] **Step 3:** Implement `AppRequestHandler` (all state changes go through `IAppLights`; brightness stored per scene so the adapter calls `LightingManager.SetBrightness`).
- [ ] **Step 4:** Tests PASS. Wire `AppControl` into `MainWindow` (create `AppPipeServer(handler.Handle, EmberNames.AppPipeName)`, `Start()`, dispose on quit). Build `dotnet build Ember.sln -c Release`; Expected: 0 warnings, 0 errors.
- [ ] **Step 5:** Live check on the PC: run the dev app (`reference-ember-deploy-and-test`: kill the installed app, run `publish\app\Ember.App.exe`), send a status request from a PowerShell `NamedPipeClientStream` to `ember.app.v1`, and a `selectScene`; Expected: a JSON reply with the scene list and the lights change. Commit "The app answers lights requests on ember.app.v1".

### Task 4: Key readouts and mode cycling (pure logic, Ember.Core)

**Files:**
- Create: `src/Ember.Core/StreamDeckLogic.cs`
- Test: `tests/Ember.Core.Tests/StreamDeckLogicTests.cs`

**Interfaces:**
- Produces:
  - `public sealed record Readouts(double? CpuTemp, double? CpuFanRpm, double? GpuTemp, double? GpuFanRpm)`; `public static Readouts Readouts.From(Snapshot s)` (all null when `s.Stale`; CPU temp/GPU temp from `DashboardSummary.From`; GPU fan = highest `SensorKind.FanRpm` reading with `HardwareKind.Gpu`; CPU fan = the `FanRpm` reading whose name contains "CPU" (case-insensitive), highest; zero RPM counts as a real 0, null only when no such reading).
  - `public static class StreamDeckLogic { static readonly FanMode[] CycleOrder = [SuperQuiet, Quiet, Normal, Performance, Extreme]; static FanMode Next(FanMode? current); static string ModeLabel(FanMode m); static string FormatTemp(double? c); static string FormatRpm(double? rpm); }` - `Next(null)` and `Next(Manual/Ai)` return `Normal`; formats return "-" for null, `"63°"` and `"1,240"` otherwise.
- Consumes: `Snapshot`, `DashboardSummary`.

- [ ] **Step 1: Failing tests:** `NextWrapsFromExtremeToSuperQuiet`, `NextFromManualOrAiOrNullGoesToNormal` (a safe middle mode, my call), `ModeLabelsMatchTheTray` (e.g. SuperQuiet -> "Super quiet"; read the labels in `TrayIcon.cs` and assert the same strings), `StaleSnapshotGivesAllNull`, `PicksCpuFanByNameAndGpuFanByHardware`, `ZeroRpmIsShownAsZeroNotDash`, `FormatsNullAsDash`.
- [ ] **Step 2:** `dotnet test tests/Ember.Core.Tests --filter "FullyQualifiedName~StreamDeckLogic"`; Expected: FAIL.
- [ ] **Step 3:** Implement. Before finalising the CPU/GPU fan name rules, dump the live readings once (`ember.v1` one line, PowerShell `NamedPipeClientStream`) and write the real sensor names you see into a comment and into a test fixture.
- [ ] **Step 4:** PASS; commit "Stream Deck readout and mode logic".

### Task 5: The plugin program (WebSocket host, keys)

**Files:**
- Create: `src/Ember.StreamDeck/Ember.StreamDeck.csproj` (Exe, `net8.0-windows`, `AssemblyName` `EmberStreamDeck`, references `Ember.Core`), `Program.cs` (argument parsing, run loop, exit when the socket closes), `StreamDeckClient.cs` (WebSocket framing: connect, `registerPlugin`, receive loop, `Send(object)`), `Keys/*.cs` (one small class per action), `EmberLink.cs` (the three links to Ember)
- Modify: `Ember.sln` (add project; add a `tests` reference if testing the client)
- Test: `tests/Ember.Core.Tests/` already covers logic; add `tests/Ember.Core.Tests/StreamDeckMessagesTests.cs` for the pure message builders only if they live in Core (put `StreamDeckMessages` there)

**Interfaces:**
- Produces: `StreamDeckMessages` (in `Ember.Core`): `string RegisterPlugin(string uuid)`, `string SetTitle(string context, string title)`, `string SetImage(string context, string svg)` (data URI `data:image/svg+xml;charset=utf8,` + escaped svg), `string ShowAlert(string context)`, `string SendToPropertyInspector(string action, string context, object payload)`, and `bool TryParseEvent(string json, out SdEvent? e)` where `SdEvent(string Event, string? Action, string? Context, JsonElement Payload)`; never throws.
- `EmberLink`: `ServiceConnection` on `ember.v1` (latest `Snapshot` and `Readouts`), `ControlClient`-like `SendModeAsync(FanMode)` on `ember.ctl.v1` (copy `src/Ember.App/ControlClient.cs` into the plugin or move it to Core - move is preferred, App keeps working), and `AppLink.RequestAsync(AppRequest)` on `ember.app.v1` with a 2 s timeout; before trusting `ember.app.v1`, check the server process (`GetNamedPipeServerProcessId`) is a process named `Ember.App` of the current session.
- Keys behaviour:
  - `scene`: press selects the scene in settings `{ "scene": "<name>" }`; face shows the name, highlighted when it is current; deleted scene -> `ShowAlert`.
  - `power`: toggles lights; face "Lights on/off".
  - `brightness`: settings `{ "step": 0.1 | -0.1 }` (two choices in the inspector: Brighter, Dimmer).
  - `fanmode`: settings `{ "mode": "<FanMode>" }`; highlighted when it is the current mode.
  - `cyclemode`: press sends `StreamDeckLogic.Next(current)`; face shows the current mode label.
  - `open`: sends `ShowWindowRequest`; if the pipe is unreachable and no `Ember.App` process exists, starts the exe whose path is read from `ember-path.txt` next to the plugin exe (Task 7 writes it); never starts when one is already running.
  - `readout`: settings `{ "value": "cpuTemp" | "cpuFan" | "gpuTemp" | "gpuFan" }`; face shows a label ("CPU", "CPU FAN", "GPU", "GPU FAN") and the formatted value, redrawn at most once a second from the latest snapshot; temperature tint warm from 70 C, red from 85 C (my call).
  - Offline state: face text "Ember offline", presses ignored.
- Refused or failed fan/lights command -> `ShowAlert(context)`.

- [ ] **Step 1: Failing tests** for `StreamDeckMessages`: `RegisterPluginJsonShape`, `SetImageProducesDataUriSvg`, `ParsesKeyDownAndWillAppear`, `ParseNeverThrowsOnGarbage`, `LargeOrNestedPayloadIsKept` (assert the exact JSON key names from the spike).
- [ ] **Step 2:** `dotnet test tests/Ember.Core.Tests --filter "FullyQualifiedName~StreamDeckMessages"`; Expected: FAIL.
- [ ] **Step 3:** Implement `StreamDeckMessages` in Core; the WebSocket client with `ClientWebSocket` (one receive loop, reassemble fragments, cap a message at 64 KB, exit code 0 when the server closes); key classes. Each key keeps its own `context`, tracked on `willAppear` and dropped on `willDisappear`.
- [ ] **Step 4:** Tests PASS; `dotnet build Ember.sln -c Release` clean.
- [ ] **Step 5:** Commit "Stream Deck plugin program".

### Task 6: Plugin package, property inspector and release packing

**Files:**
- Create: `src/Ember.StreamDeck/plugin/manifest.json`, `plugin/pi/inspector.html` (one page showing only the fields for the chosen action: scene select filled from `sendToPropertyInspector`, mode select, readout select, brightness step), `plugin/images/*.svg` (action icons, category icon; calm macOS-like look), `scripts/pack-streamdeck.ps1`
- Modify: `scripts/build-release.ps1` (publish `Ember.StreamDeck`, run the pack script, copy `Ember.streamDeckPlugin` into `dist\Ember-<version>\streamdeck\`), `scripts/install-ember.ps1` (copy `streamdeck\*` to `$InstallDir\StreamDeck`, same `Copy-WithRetry` pattern), `.github/workflows/*.yml` only if CI needs the packed file attached (no: it is inside the zip and the setup exe)
- Test: `tests/Ember.Core.Tests/PluginManifestTests.cs` (reads `src/Ember.StreamDeck/plugin/manifest.json`)

**Interfaces:**
- Produces: `dist\Ember-<version>\streamdeck\Ember.streamDeckPlugin` (zip with `dev.ember.pc.sdPlugin\` at root containing `manifest.json`, `EmberStreamDeck.exe` and its files, `pi\`, `images\`, and `Version` equal to the Ember version with `.0`-style four-part form Stream Deck requires, e.g. `1.2.0.1`); `scripts/pack-streamdeck.ps1 -PublishDir <dir> -Version <v> -Out <file>`.
- Consumes: manifest keys verified in Task 1.

- [ ] **Step 1: Failing tests:** `ManifestListsEveryActionUuidWithPrefix`, `ManifestCodePathPointsAtEmberStreamDeckExe`, `EveryActionHasAnExistingIcon`, `VersionIsFourNumericParts`.
- [ ] **Step 2:** `dotnet test tests/Ember.Core.Tests --filter "FullyQualifiedName~PluginManifest"`; Expected: FAIL.
- [ ] **Step 3:** Write manifest, inspector, icons, pack script (`Compress-Archive` then rename to `.streamDeckPlugin`), and the two script edits. The version is stamped from `Directory.Build.props` by the pack script (take the number before any `-beta`, append the beta number as the fourth part; production gets `.0`).
- [ ] **Step 4:** Run `scripts/build-release.ps1 -SkipOpenRgb -SkipInstaller`; Expected: the plugin file exists in `dist`. Tests PASS.
- [ ] **Step 5:** Commit "Package the Stream Deck plugin".

### Task 7: Install card on the Devices page

**Files:**
- Create: `src/Ember.App/StreamDeckInstaller.cs`, `tests/Ember.Core.Tests/StreamDeckInstallStateTests.cs` with the pure part in `src/Ember.Core/StreamDeckInstallState.cs`
- Modify: `src/Ember.App/DevicesPage.cs` (add the card in `Update`, only when `!BuildFeatures.IsProduction`, with the Beta tag like Nanoleaf/Razer), `docs/TESTING.md`, `CHANGELOG.md`, `README.md`

**Interfaces:**
- Produces: in Core `public enum StreamDeckState { NotFound, NotInstalled, Installed, NeedsUpdate }` and `public static StreamDeckState StreamDeckInstallState.Evaluate(bool appFound, string? installedVersion, string bundledVersion)` (parse four-part versions; unparsable installed version -> `NeedsUpdate`). In the App: `StreamDeckInstaller.Detect()` (Stream Deck app found when `%APPDATA%\Elgato\StreamDeck` exists; installed version from `...\Plugins\dev.ember.pc.sdPlugin\manifest.json`), `Install()` (writes `ember-path.txt`: the full path of the running `Ember.App.exe`, into the bundled plugin before opening - so write it into a temp copy of the `.streamDeckPlugin`, then `Process.Start` that copy with `UseShellExecute`), `Remove()` (best effort: delete the plugin folder, return false and a message if files are locked, telling the owner to quit and reopen Stream Deck).
- Card text: title "Stream Deck", blurb "Keys for lights, fan modes and live temperatures", states "Stream Deck not found / Not installed / Installed / Update available (beta)"; buttons Install or Update, and Remove. Card rebuilds only when the state changes (follow the existing `_key` pattern).

- [ ] **Step 1: Failing tests:** `NotFoundWhenNoStreamDeckApp`, `NotInstalledWhenNoVersion`, `InstalledWhenVersionsEqual`, `NeedsUpdateWhenOlder`, `NewerInstalledCountsAsInstalled`, `GarbageVersionNeedsUpdate`.
- [ ] **Step 2:** `dotnet test tests/Ember.Core.Tests --filter "FullyQualifiedName~StreamDeckInstallState"`; Expected: FAIL.
- [ ] **Step 3:** Implement the state evaluator, installer and card. Docs: CHANGELOG section for the next beta (`**Stream Deck plugin (beta).** ...`), a README line, and TESTING.md items: install from the card; each key type on the real deck; offline state; update after a newer plugin; remove; cycle key never leaves the tray's modes.
- [ ] **Step 4:** `dotnet test Ember.sln -c Release`; Expected: all green. Build `-SkipOpenRgb -SkipInstaller` release folder and run the dev app from `publish\app` with the plugin file next to it (or point `StreamDeckInstaller` at an overridable bundled path via a constructor argument for tests and the dev run).
- [ ] **Step 5:** Commit "Stream Deck card on the Devices page".

### Task 8: Live test on the owner's PC and beta release

**Files:** Modify `Directory.Build.props` (version bump), `CHANGELOG.md`, `docs/TESTING.md` (tick only what was seen).

- [ ] **Step 1:** Install via the real card on the PC. Expected: Stream Deck prompts; actions appear in a new "Ember (beta)" category.
- [ ] **Step 2:** The owner puts the keys on the deck; check each: scene, power, brightness up/down, set mode, cycle (watch the mode on the status pipe, keep a temperature monitor open per the fan safety rule), open Ember (closed and already running: no second instance in `Get-Process Ember.App`), four readouts against Ember's dashboard. Close Ember: keys read "Ember offline" and recover on start. Record what was actually seen; say plainly what could not be verified.
- [ ] **Step 3:** Fix anything found (new tests for each fix), then follow the release recipe from the handover notes: bump to the next beta, CHANGELOG section, `dotnet test`, commit (Ol775 identity, no trailer), push `phase2` and `phase2:main`, tag `v<version>`, wait for the "Release build" run, `gh release create ... --prerelease`, then press **Update now** on the PC. Lights off and fans on Super quiet when done.
