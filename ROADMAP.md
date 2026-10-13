# Roadmap

What is done, what is being worked on and what is planned. See [CHANGELOG.md](CHANGELOG.md) for what changed in each version. Plans can change.

## Done

- Live sensor dashboard with history
- Fan modes: Super quiet, Quiet, Normal, Performance, Extreme, and Manual curves with an editor
- Safety rules, header testing, conflict detection, watchdog service, stall recovery for fans that stop at low speed
- Lighting through OpenRGB and Govee (LAN and cloud), layered effects, scenes, screen sync
- Logitech G keyboard and mouse lighting through G HUB, and a light profile per device
- Themes (dark, light, match Windows) and accent colours
- Update checker with Stable and Beta channels, release zip and an install and upgrade script
- Experimental switch for game detection and GPU fan control
- Changelog and roadmap inside the app

## In progress

- Verifying Logitech lighting on more keyboards and mice
- Verifying game detection and GPU fan control on real hardware, then taking them out of Experimental

## Next

- Tray icon and start minimised
- AI mode: learns your temperatures and noise preference to suggest a curve you approve
- Signed releases and a proper installer (MSI or similar)
- Continuous integration builds
- Faster screen capture (DXGI) and audio-reactive effects
- Per-LED editing, Govee segments, per-key Logitech effects
- Corsair, Razer and other brands, through OpenRGB where it supports them

## Not planned

- Overclocking or voltage control
- Cloud accounts or telemetry
