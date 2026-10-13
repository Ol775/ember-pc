# Changelog

All notable changes to Ember. Newest first. Ember follows [semantic versioning](https://semver.org); versions with a label such as `beta.1` are pre-releases.

## 0.1.0-beta.5 (2026-10-13)

### Added

- **Tray icon.** Ember now has an icon in the notification area. Double-click it to open the window; its menu changes the fan mode or quits Ember. Settings has two new switches: "Keep running in the tray when I close the window" and "Start minimised, in the tray" (used when Ember starts with Windows). Both are off by default.
- **AI assist** (Fans page). If you switch on "Learn from my temperatures", Ember keeps a count of how hot your CPU and case run, one count per degree, in a small file on your PC. After about two hours it can suggest fan curves that stay low through your usual range and only climb above it, tilted towards quiet or cool with a slider. It only suggests: nothing changes until you press Apply, and the safety rules (full speed at 90 °C) and the service's own curve checks still apply.
- **More effects.** "Fire" (flames that are hot at the bottom and flicker upwards), "Rain" (drops that fall down a keyboard or run along a strip) and "Key group" (any of Colour, Wave, Breathing, Fire or Rain on just the function row, number row, letters, space bar row, arrows and navigation, or number pad).
- **Paint several devices at once.** A switch on the Lights page applies fills to every device on the canvas, and a gradient or rainbow then runs across the whole canvas so devices side by side share one picture.
- **Paint library.** Save the painted LEDs of a device under a name, and use a saved picture on the selected device or on all of them. Rename and delete are there too. The pictures are plain files in your Ember folder that you can copy to another PC.
- **Continuous integration.** The repository builds and tests itself on GitHub for every push, and a release workflow builds the zip and the installer.
- **Signing.** The release build signs Ember's programs and the installer when a code-signing certificate is provided (see docs/SIGNING.md). Until one is, Windows still shows "Unknown publisher".

- **Own lighting for one device.** "Give it its own lighting" (Lights page, device list) makes a copy of the layers just for that device, so a keyboard can show a different look from the Govee lamps and fans. The layer editor then works on that device alone until you press "Back to the whole scene".
- **Switch off a device.** Each device on the Devices page has a "Use" switch. Off, Ember leaves it out of every scene and stops sending to it; the choice is remembered.
- **Arrange devices.** Lamps found later (for example more Govee lights) get room on the canvas instead of overlapping, and a button tidies the canvas into one row per device.

### Fixed

- **Key groups follow the real keys.** The number pad, the arrows and navigation block and the function row now line up with the keys on a Logitech G915 (checked with a colour per column): Print Screen belongs to the navigation block, the Right arrow to it and not the number pad, and Right Ctrl to the bottom row.
- **Switching a device off now works for the motherboard and NZXT too.** Their long OpenRGB ids were being refused when saved.
- **Key highlight.** A layer that lights chosen letter and number keys (W A S D by default) in one colour and leaves the other keys to the layers below, for gaming. Type any letters or numbers to light; the bottom letter row (Z to M) has not been checked against a real keyboard.
- **Logitech keyboard shows per-key effects again.** The mouse's single colour used to overwrite the whole keyboard every frame (G HUB cannot do both at once). The keyboard is now lit key by key and the mouse is left to G HUB; a switch on the Lights page makes every Logitech device take one colour instead.
- **Govee lamps did not change colour when a VPN covered the home network.** Ember found the lamp but sent its colours into the VPN (for example a Tailscale subnet route for 192.168.1.0/24). Commands now go out from the PC's own address on the lamp's network.

### Changed

- What's new in Settings folds each release: the newest is open, older ones open with a click, and long entries show two lines with "Show more".

- The AI chip in the mode list now points to the AI assist panel instead of saying "coming later". It is not a mode the service runs.

## 0.1.0-beta.4 (2026-10-13)

### Added

- **An installer.** `Ember-Setup-<version>.exe` is a normal Windows installer: a welcome page, the Terms of Use to accept, a choice of folder and shortcuts, a progress page with a log, and an Open Ember button at the end. It carries everything Ember needs, including Microsoft's own installers for the .NET 8 Desktop Runtime and the Visual C++ runtime (used by OpenRGB), and only runs them when the PC does not have them. The one thing it downloads is the PawnIO driver, checked against a pinned checksum and signature. It can also run silently for scripted installs.
- **Uninstall from Windows.** Ember now appears under Installed apps, with its version, and removes itself from there. It asks whether to keep your settings.
- **Legal pages.** Terms of Use, Privacy, Third-party notices and the Licence are in Settings, Legal. Ember asks you to accept the Terms the first time it opens if the installer did not already ask, and again if they change.
- **Per-LED painting, further.** Drag over LEDs to paint them like a brush. Fill a device with one colour, a gradient at any angle, or a rainbow; flip a picture left to right; export it to a file and import it again, fitted to the LEDs of another device.
- **Govee segment detection.** A "Detect segments" button asks Govee (through your API key) how many parts a lamp can light separately, and knows the H6056 light bars have 12.
- **More effects.** "Spectrum bars" rise up a keyboard (or become brightness on a strip), "Ripples" spread rings across the canvas or each device, and "Reactive keys" flash the keyboard where you press a key. Reactive keys only notice that a key went down, only while the layer is on, and keep no text.
- A logo and an app icon.

### Changed

- Effects can now be drawn on each device separately, so a keyboard shows the whole picture wherever it sits on the canvas.

## 0.1.0-beta.3 (2026-10-13)

### Added

- **Music effects.** "Music pulse" (one colour that follows the bass, middle, treble or overall sound) and "Music spectrum" (low pitches on the left, high on the right). Ember listens to what the PC is playing, only while a music layer is on, and keeps nothing.
- **Faster screen sync.** The screen is read through DXGI desktop duplication, straight from the graphics card, and works with HDR desktops. If it cannot, Ember falls back to the older method.
- **Per-LED painting.** Select a device on the Lights canvas, switch on "Paint single LEDs" and click LEDs to give them a fixed colour; right-click gives one back to the effects.
- **Per-key Logitech keyboard.** The keyboard is now a grid of 126 keys (21 by 6), so effects run across and down it, not just along it.
- **Govee segments (experimental).** For lamps that accept per-segment colours, choose how many segments to drive from the device list on the Lights page. One segment is the whole lamp, as before.
- Devices with a grid of LEDs are drawn as a grid on the canvas.

### Fixed

- The changelog and roadmap in Settings showed "Nothing to show" in beta 2 because the files were not packed into the app.

## 0.1.0-beta.2 (2026-10-13)

### Added

- **Logitech G lighting.** Keyboards and mice are driven through G HUB's own lighting engine, which Ember loads from your G HUB install (nothing from Logitech is copied or shipped). The keyboard is lit per key column, the mouse as one lamp.
- **Light profiles per device.** Any device on the Lights page can show a different saved scene from the rest, for example a keyboard with its own look while the fans and strips follow the main scene.
- **Changelog and roadmap** in Settings, and as `CHANGELOG.md` and `ROADMAP.md` on GitHub.
- **Devices page** now carries the fan header tests, so the Fans page is only modes and curves.

### Fixed

- The update checker now says plainly that a private repository hides its releases, instead of "no releases yet".

## 0.1.0-beta.1 (2026-10-13)

First beta.

### Added

- **Dashboard** with live CPU, GPU and motherboard temperatures, load and clock lines, a 10-minute history chart and fan speeds.
- **Fan control** with Super quiet, Quiet, Normal, Performance and Extreme curves, and Manual mode with a curve graph and editor for the CPU fan and the system fans.
- **Safety rules** enforced in the service: 90 °C override, early CPU fan ramp, speed floors, blind-sensor rule, and a watchdog service that restores safe speeds if Ember stops responding.
- **Stall recovery.** A fan that stops turning at a low speed is spun up at 100% for a few seconds, and that fan keeps a slightly higher minimum afterwards.
- **Fan headers** are tested and enabled one at a time; headers with no fan attached are hidden; competing fan programs are detected by name and can be closed from the app.
- **Lighting** through OpenRGB (bundled, started on localhost only) and Govee (LAN, or cloud with your own key stored in Windows Credential Manager), with layered effects, blend modes, scenes and screen sync.
- **Devices page** with the state of each integration and every device, plus the fan header tests.
- **Settings** for temperature unit, start page and start with Windows, theme (dark, light or match Windows), accent colour, updates and release channel.
- **Update checker** that only reads the list of GitHub releases and never installs anything by itself.
- **Experimental switch** for game detection and GPU fan control, off by default.
- **Install scripts** to build a release zip and install or upgrade Ember with its two services and a Start Menu shortcut.

### Security

- Pipes are restricted to SYSTEM, administrators and the interactive user; control messages are size-limited, validated and rate-limited.
- The service refuses to install from a folder that normal users can write to.
- Ember uses the signed PawnIO driver, not WinRing0.
