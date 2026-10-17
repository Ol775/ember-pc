# How Ember is put together

<sub>[← All docs](README.md)</sub>

Ember controls real fans, so the part that touches hardware is small, separate and locked down.

```
 Ember.App (WPF, your user)  ──── ember.v1 (status) ───▶  Ember.Service (LocalSystem)
   pages, lighting, plugs,   ──── ember.ctl.v1 (commands) ─▶   reads sensors, writes fan speeds
   games, tray, updates                                        through LibreHardwareMonitor + PawnIO
          │                                                          ▲
          │ Ember.Lighting: OpenRGB, Govee, Hue, Nanoleaf,           │ restores safe speeds
          │ Razer, Corsair, Logitech, Meross, Tapo                   │ if the service stops answering
          ▼                                                          │
   your lights and plugs                                       Ember.Watchdog (service)
```

## Projects

| Project | What it is |
|---|---|
| `src/Ember.Core` | Shared logic with no Windows or hardware code: the wire protocol, fan curves and safety rules, mode resolver, game detection and game library scanner, power tracking, update checker, preferences. Most of the tests live here. |
| `src/Ember.Service` | The Windows service. The only code that talks to the PawnIO driver and writes fan speeds. Owns the safety rules (90 °C override, speed floors, CPU guard, blind-sensor rule). |
| `src/Ember.Watchdog` | A second service that restores safe fan speeds if the main service stops responding. |
| `src/Ember.App` | The WPF app: dashboard, fans, lights, devices, games, power, specs and settings pages, the tray icon, the schedule and alerts, and the update prompt. It only displays and asks. Every build draws the redesigned look; the classic look was removed in 1.4.0. |
| `src/Ember.Lighting` | Device sources and the lighting engine: layered effects, scenes, and one source per integration. Also the smart-plug readers used by the Power page. |
| `src/Ember.StreamDeck` | The Stream Deck plugin (beta builds). Talks to the app over `ember.app.v1`. |
| `src/Ember.Setup` | The installer program. |
| `tests/` | Unit tests for Core, Service and Lighting, and `Ember.App.Tests`, which runs the real window as a simulation (no devices, network or service, files in a scratch folder: set `EMBER_UI_TESTS=1`, and `EMBER_UI_SHOTS=<folder>` to save a picture of every page). Those tests check that every page opens and draws, how much processor time and memory the window uses, and that browsing the pages again and again does not leak. The install code and real hardware are verified by hand on Windows (see [hardware-checks.md](hardware-checks.md)). |

## Talking between parts

- **`ember.v1`** is the status pipe: one JSON line per snapshot of sensors and fan state.
- **`ember.ctl.v1`** is the control pipe: single JSON lines of at most 4 KB, checked before use. Anything that changes a fan goes through the service and its rules.
- **`ember.app.v1`** (beta builds, current user only) lets the Stream Deck plugin ask the app for scenes, lights and brightness.

Both service pipes are restricted to SYSTEM, Administrators and the interactive user. See [SECURITY.md](../SECURITY.md).

## Production and beta builds

A release without a label (1.4.0) is the production build; one with a label (1.5.0-beta.1) is a beta build. `BuildFeatures` in `Ember.Core` says what each includes, and the service refuses messages for a feature the build does not have. Beta builds also show the schedule and alerts, GPU fan control, the Stream Deck plugin and the roadmap page. Features that are written and tested but not yet proven on enough hardware are marked **Beta** in the app.

## Why PawnIO

Motherboard sensors and fan headers sit behind port I/O that user mode cannot reach, so a kernel driver is needed. Many monitoring tools use WinRing0, which is vulnerable (CVE-2020-14979: any local user can read and write physical memory) and is flagged by Microsoft Defender. Ember uses the signed PawnIO driver instead. Details: [driver-research.md](driver-research.md).
