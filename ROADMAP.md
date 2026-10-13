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
- Faster screen capture through DXGI, and music-reactive effects
- Per-LED painting, a per-key Logitech keyboard grid, and Govee segments (experimental)
- Gradients, rainbow, import and export for painted LEDs, and Govee segment detection
- Spectrum bars, ripples and reactive keys
- An installer with terms, runtimes included, and uninstall from Windows
- Terms of Use, Privacy and third-party notices inside the app

## In progress

- Checking Govee segments, per-key keyboard layout and HDR screen colours on more hardware
- Verifying Logitech lighting on more keyboards and mice
- Verifying game detection and GPU fan control on real hardware, then taking them out of Experimental

## Next

- Tray icon and start minimised
- AI mode: learns your temperatures and noise preference to suggest a curve you approve
- Continuous integration builds
- Signed installer, so Windows does not warn about an unknown publisher
- Per-LED painting for several devices at once, and a library of shared paint files
- More effects: fire, rain, an effect per key group on keyboards

## Works through OpenRGB

Corsair, Razer, ASUS, MSI, Gigabyte, ASRock, HyperX, SteelSeries and many other brands show up in Ember when OpenRGB supports them and their own software (iCUE, Synapse, Armoury Crate) is closed. They need no extra code in Ember.

## Not planned

- Overclocking or voltage control
- Cloud accounts or telemetry
