# Stream Deck plugin (beta) - design

## Intent and why

The owner uses a Stream Deck to quickly manage lights, launch apps and see live visuals. They want the same for Ember: control the lights and fan mode with one key press, open Ember, and watch live readouts (CPU temperature, fan speed) on the key faces. Everything about it is **beta**: it only appears on the Beta channel and is labelled "(beta)".

## Goals

- Keys that act: pick a lighting scene, lights on/off, brightness up/down, set fan mode (Super quiet, Quiet, Balanced, ... whatever Ember's modes are), open Ember (bring the window to front or start it).
- Keys that show: CPU temperature, GPU temperature, fan speed/mode, current scene name, updating about once a second. Colour hints (e.g. warm as temperature rises) are a nice-to-have, builder's call.
- An **Install** option in Ember (a "Stream Deck" card on the Devices page, Beta only) that installs the plugin into the Stream Deck app, and can also remove it. The card shows a plain status: Stream Deck not found / Not installed / Installed / Needs update.
- Works with the normal installed Ember (service plus app) without the owner doing anything by hand.

## Non-goals

- Not part of production 1.x. No signing work. No Mac. No Stream Deck+ dials/touch strip, no multi-action profiles shipped by us (the owner builds their own pages).
- No remote or network control: local machine only.
- No fan curve editing from the deck. Fan safety rules still hold (see below).

## Anti-goals (a failure even if it works)

- Anything that lets a deck key put the fans below the existing safety floors, bypass the 90 C override or the CPU fan rules, or call SetDefault on the motherboard. The plugin may only send the same modes the app already offers, through the same checks.
- A second copy of Ember being started by the plugin (the app has a single-instance mutex; "open Ember" must reuse it).
- Needing Node.js, Python or any extra runtime on the PC.
- Telemetry, personal data in the repo, or the plugin reaching the internet.

## Constraints

- Stack: .NET, same solution (`Ember.sln`), `TreatWarningsAsErrors`, central package management.
- Stream Deck software is installed on the owner's PC (`%APPDATA%\Elgato\StreamDeck`). Plugin format: a Stream Deck plugin may be a native executable (`CodePath` in `manifest.json` pointing to an .exe) that talks to the Stream Deck app over a local WebSocket (registered with the port/UUID/event passed on the command line). Packaged as `com.<something>.ember.sdPlugin`, distributed as a `.streamDeckPlugin` file (a zip).
- Fans: the existing control pipe `ember.ctl.v1` (service) already takes `{"v":1,"op":"setMode","mode":"SuperQuiet"}`; status pipe `ember.v1` streams one JSON line with mode, source, game, GPU control, readings. Lights live only in the app process (`LightingManager`: `SelectScene`, `SetOn`, `SetBrightness`, scene list in `State`), so the app needs a new small local channel for lights and for "open window".
- Releases: betas are pre-releases; the setup exe/zip built by CI must carry the plugin file so Install works offline.
- Privacy/commits: nothing linked to the owner; commits use the Ol775 no-reply identity and no session trailer.

## Decided with the owner

- Uses: quick light management, launching/opening the app, seeing live visuals on keys. All of these, both action keys and live readout keys.
- All beta.
- Installed from Ember through an install option.

## My calls (not yet confirmed)

- Plugin is a C# executable, not Node/TypeScript, so no extra runtime.
- Plugin talks to Ember over local named pipes, not a network port. Lights/open-window go through a new app-side pipe; fan mode and readings go straight to the existing service pipes.
- Install card sits on the Devices page next to the other integrations, Beta only.
- Install = Ember writes the packaged `.streamDeckPlugin` out and opens it, so the Stream Deck app does the install itself (it asks its own confirmation). Stream Deck must be restarted/handled by its own app.

## Cycle speed mode key (owner request on approval)

One key that switches the fan speed mode each press, stepping through the modes the app offers in order and wrapping round, and showing the current mode name on its face. This sits alongside the keys that set a specific mode. It goes through the same checks as any mode change (refusal flashes the key).

## Readout keys (owner's answer: "CPU and GPU")

Live keys cover the CPU and the GPU: CPU temperature, CPU fan speed, GPU temperature, GPU fan speed (my reading of the answer: four keys, one per value, each shown on its own key). No per-system-fan keys. If GPU temperature or GPU fan speed is not in the status feed today, the builder adds it. Where there is no GPU reading, the key shows a dash.

## More calls, from the builder check (mine, not yet confirmed)

- **Unsigned plugin:** Stream Deck installs locally packed `.streamDeckPlugin` files without a signature, so no signing work. The builder must verify this first on the owner's PC before building the rest (a "hello" plugin); if it refuses, stop and come back.
- **Ember not running:** the "Open Ember" key starts the app if it is closed and brings it to front if it is open. Ember writes its own app path into the plugin's config when installing the plugin. Light and fan keys do nothing when Ember is offline and show a quiet "Ember offline" state; they never start the app.
- **Install state:** the plugin's version comes from its `manifest.json` in `%APPDATA%\Elgato\StreamDeck\Plugins\<id>.sdPlugin`; "Needs update" when it is older than the one bundled with Ember. Install hands the file to Stream Deck. Remove is best effort: delete the plugin folder and tell the owner to restart Stream Deck if it does not disappear.
- **Refused changes:** the fan mode keys offer only the modes the app already offers. If the service refuses a press, the key flashes Stream Deck's alert (the standard warning flash), nothing else.
- Fixed per key vs chosen in the property inspector: the scene key lets the owner choose which scene in the inspector.

## Spike result (2026-10-14)

Verified on the owner's PC (Stream Deck 7.6.0.23012, Windows 11): an unsigned `.streamDeckPlugin` with `SDKVersion` 2, `CodePath` `hello.exe` (a .NET 8 framework-dependent apphost), `Software.MinimumVersion` 6.4 and `OS` windows 10 installs after one click on Stream Deck's own prompt; Stream Deck launches the exe with `-port <n> -pluginUUID <id> -registerEvent registerPlugin -info <json>`; the plugin registers over `ws://127.0.0.1:<port>` and receives `deviceDidConnect`. Key presses were not exercised in the spike. The plugin folder is `%APPDATA%\Elgato\StreamDeck\Plugins\<uuid>.sdPlugin`. Plugin ids are lower-case dotted.

## Left to the builder

- Exact action list and key icons/artwork (clean, calm, matching Ember's macOS-like look), naming and the plugin UUID.
- Property inspector (the settings panel in the Stream Deck app) details, e.g. choosing which scene a key activates.
- Pipe message format for the new app channel, security (pipe ACL limited to the current user, as the existing pipes do), and reconnect behaviour when Ember is not running (keys show a quiet "Ember offline" state).
- Where the plugin is built and how CI bundles it; tests (protocol and pipe tests in the existing test projects).
- A testing checklist in `docs/hardware-checks.md`, CHANGELOG entry (user-facing bold lead), and a README line.
