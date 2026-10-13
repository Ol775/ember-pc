# Changelog

All notable changes to Ember. Newest first. Ember follows [semantic versioning](https://semver.org); versions with a label such as `beta.1` are pre-releases.

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
