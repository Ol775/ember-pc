# Third-party software

## OpenRGB (bundled with the app build)

Ember's Lights page controls PC lighting through [OpenRGB](https://openrgb.org), which is shipped unmodified in the `OpenRGB`
folder next to `Ember.App.exe`.

- Version: 1.0 (Windows 64-bit), downloaded by `scripts/fetch-openrgb.ps1` from the official release and checked against a pinned SHA-256.
- Licence: GNU General Public License v2.0 (or later). OpenRGB is a separate program: Ember starts it and talks to it over its
  network SDK on this PC (127.0.0.1:6742). Ember does not link to or include OpenRGB's code. Ember itself is MIT-licensed.
- Source code and licence text: <https://codeberg.org/OpenRGB/OpenRGB> (release `release_1.0`).
- If you already run your own OpenRGB, Ember uses that one when it is listening and never starts a second copy.

## Corsair iCUE SDK (bundled with beta builds)

To light Corsair devices Ember uses Corsair's iCUE SDK library, `iCUESDK.x64_2019.dll`, shipped unmodified in the `iCUE` folder next
to `Ember.App.exe`. It is taken from the redistributable folder of Corsair's official SDK release.

- Version: 4.0.84, downloaded by `scripts/fetch-icue-sdk.ps1` from <https://github.com/CorsairOfficial/cue-sdk> and checked against a pinned SHA-256.
- The library only talks to the Corsair iCUE program on this PC, which must be running with the SDK switched on. Nothing leaves the PC.
- The SDK is Corsair's software under Corsair's own terms (see the SDK documentation in the release above). It is not covered by Ember's MIT licence.

## Libraries used by the app and service

- **LibreHardwareMonitorLib 0.9.6** (MPL-2.0): reads sensors and fan headers.
- **NAudio.Wasapi 2.2.1** (MIT): listens to the sound being played, for music effects.
- **Vortice.Direct3D11 and Vortice.DXGI 3.6.2** (MIT): fast screen capture for screen sync.
- **Microsoft.Extensions.Hosting and related 8.x** (MIT): runs the services.
- **Microsoft .NET 8 Desktop Runtime** (MIT, with Microsoft's installer terms): runs Ember. The installer includes Microsoft's own installer and only runs it when the runtime is missing.
- **Microsoft Visual C++ Redistributable, x64** (Microsoft Software Licence Terms, shown by its installer): needed by OpenRGB. The installer includes Microsoft's own installer and only runs it when it is missing.
## PawnIO driver (downloaded, not bundled)

The signed PawnIO driver lets Ember read motherboard sensors and fan headers. The Ember installer downloads PawnIO's official setup from <https://github.com/namazso/PawnIO.Setup> and checks its SHA-256 and signature before running it. It is not included in Ember's files because its setup program publishes no redistribution terms. PawnIO itself is published by its author under its own licence (<https://github.com/namazso/PawnIO>).

- **Logitech G HUB** (not bundled): if it is installed, Ember loads its LED SDK engine (sdk_legacy_led_x64.dll, found through G HUB's own registry entry and only if it sits under Program Files) to light Logitech G keyboards and mice. Logitech and G HUB are trademarks of Logitech; Ember is not affiliated with Logitech.
