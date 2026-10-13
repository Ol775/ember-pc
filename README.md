<p align="center">
  <img src="docs/images/banner.svg" alt="Ember: monitor, cool and light your Windows PC" width="100%">
</p>

<p align="center">
  <img alt="Beta" src="https://img.shields.io/badge/release-beta-orange">
  <img alt="Licence: MIT" src="https://img.shields.io/badge/licence-MIT-blue">
  <img alt="Windows 10 and 11" src="https://img.shields.io/badge/Windows-10%20%7C%2011-0078D4">
  <img alt=".NET 8" src="https://img.shields.io/badge/.NET-8-512BD4">
  <img alt="Local only" src="https://img.shields.io/badge/network-local%20only-success">
</p>

**Ember is a free, open source Windows app that shows your PC's temperatures, runs your fans on quiet-to-extreme curves, and lights your RGB gear, all from one calm, macOS-style window.** A small background service reads the sensors and writes fan speeds; the app only displays and asks. Nothing leaves your PC.

<p align="center">
  <img src="docs/images/dashboard.png" alt="Ember dashboard with live temperatures and fan speeds" width="80%">
</p>

## What it does

- **Live dashboard.** CPU, GPU, motherboard and fan sensors with a 10-minute temperature history, in Celsius or Fahrenheit.
- **Fan modes.** **Super quiet**, **Quiet**, **Normal**, **Performance** and **Extreme** curves, switched with one click.
- **Manual curves.** Draw and edit your own curve for the CPU fan and the system fans, on a graph, with safety limits enforced.
- **Safety first.** At 90 °C every controlled fan goes to 100%, a lost sensor sends fans to 100%, and a separate **watchdog** service restores safe speeds if Ember stops responding.
- **Header testing.** Nothing is written to a fan header until you have tested and enabled it yourself. Headers with no fan attached are hidden.
- **Conflict detection.** Warns by name if NZXT CAM, MSI Center, Fan Control, iCUE or similar is running, and can close them for you.
- **Lighting.** Drives PC devices through **OpenRGB** (bundled with the app) and **Govee** lamps over your LAN, or through the Govee cloud with your own API key.
- **Logitech G keyboards and mice.** Lit through G HUB's own lighting engine (Ember loads it from your G HUB install; nothing from Logitech is bundled). Needs G HUB running, with "Allow games and applications to control lighting" switched on for the device.
- **Light profile per device.** Any device, such as your keyboard, can show a different saved scene from the rest.
- **Music-reactive lights.** Pulse to the bass or spread the spectrum across your devices; Ember listens to what the PC is playing, only while the effect is on.
- **Paint single LEDs** and give any device its own colours; keyboards and other grid devices are drawn and lit as grids.
- **Corsair, Razer and more.** Anything OpenRGB supports appears in Ember (close the maker's own software first).
- **Layered effects.** Stack effects with blend modes and save them as scenes.
- **Screen sync.** Lights follow the colours on your screen, read straight from the graphics card (DXGI) and including HDR desktops.
- **Themes and accent colours.** Dark, light or match Windows, with five accent colours, chosen in Settings.
- **Update checks.** On request (or at start-up if you turn it on), Ember asks GitHub whether a newer release exists on your channel (Stable or Beta). It never downloads or installs anything by itself.
- **Experimental features.** Game detection (switches to a game mode) and GPU fan control sit behind an Experimental switch in Settings, off by default.
- **Private by design.** The app and service make no network calls of their own, apart from the optional update check and the Govee cloud (used only if you add a key), which is stored in Windows Credential Manager.

## Screenshots

<p align="center">
  <img src="docs/images/fans.png" alt="Fans page with modes and curve graph" width="48%">
  <img src="docs/images/lights.png" alt="Lights page with the device canvas, per-LED painting and gradient tools" width="48%">
</p>
<p align="center">
  <img src="docs/images/fan-headers.png" alt="Devices page with every device and the fan header tests" width="48%">
  <img src="docs/images/settings.png" alt="Settings page" width="48%">
</p>
<p align="center">
  <img src="docs/images/installer.png" alt="The Ember installer" width="48%">
</p>

## Requirements

- Windows 10 or 11 (x64)
- .NET 8 Desktop Runtime (the installer adds it if it is missing)
- Administrator rights to install the services and the driver (the app itself runs as a normal user)
- The **PawnIO** driver, for motherboard temperatures and fan headers. Without it, CPU and GPU temperatures still work and the app explains how to install it.

## Install

Ember is in beta.

**Installer (easiest).** Download `Ember-Setup-<version>.exe` from the [releases page](https://github.com/Ol775/ember-pc/releases), run it, read and accept the Terms of Use, and follow the pages. It installs the .NET 8 and Visual C++ runtimes if your PC lacks them, adds the services, and downloads the PawnIO driver (checked before it runs). Ember appears in the Start menu and under Installed apps in Windows Settings, where you can also remove it. Windows will show an "unknown publisher" warning because the installer is not code-signed yet. For a scripted install: `Ember-Setup-<version>.exe /S /ACCEPT` (add `/D=C:\Path\Ember` for another folder, `/NODESKTOP`, `/NOPAWNIO`; `/ACCEPT` says you have read TERMS.md).

**From source.** Build the release folder once, then install it with one script.

**Quick install (recommended).** In PowerShell, from the repository folder:

```powershell
scripts\build-release.ps1          # fetches OpenRGB and the Microsoft runtimes, publishes everything, writes dist\Ember-<version>.zip and dist\Ember-Setup-<version>.exe
```

Then, in an **elevated** PowerShell, from the unzipped `dist\Ember-<version>` folder:

```powershell
scripts\install-ember.ps1          # installs to C:\Program Files\Ember, adds both services and a Start Menu shortcut
```

Run the same script again to upgrade; your settings are kept. `scripts\uninstall-ember.ps1` removes it (add `-RemoveFiles` to delete the program files too).

**Manual install** (the steps the script follows):
1. Fetch the bundled OpenRGB (checks a pinned SHA-256) and publish:

   ```powershell
   scripts\fetch-openrgb.ps1
   dotnet publish src/Ember.Service  -c Release -r win-x64 --self-contained false -o publish\service
   dotnet publish src/Ember.Watchdog -c Release -r win-x64 --self-contained false -o publish\watchdog
   dotnet publish src/Ember.App      -c Release -r win-x64 --self-contained false -o publish\app
   ```

2. In an elevated PowerShell, install PawnIO (downloads the official release and checks its SHA-256 and signature):

   ```powershell
   scripts\install-pawnio.ps1
   ```

3. Copy `publish\service\*` to a folder only administrators can write, such as `C:\Program Files\Ember\`, then, elevated:

   ```powershell
   scripts\install-service.ps1 -ExePath "C:\Program Files\Ember\Ember.Service.exe"
   ```

   The service runs as LocalSystem, so the script refuses folders that normal users can write.

4. Copy `publish\watchdog\*` to `C:\Program Files\Ember\Watchdog\`, then, elevated:

   ```powershell
   scripts\install-watchdog.ps1 -ExePath "C:\Program Files\Ember\Watchdog\Ember.Watchdog.exe"
   ```

5. Run `publish\app\Ember.App.exe` as your normal user. Open the Fans page, test one System Fan header, then enable it. Close any other fan-control program first.

If you install PawnIO after the service, run `Restart-Service EmberService`. To remove the service, run `scripts\uninstall-service.ps1` (elevated).

## Build and test

```powershell
dotnet build Ember.sln
dotnet test
```

The WPF app and the service's hardware, pipe-security and install code are only verified on Windows.

## Safety

Fan speeds are written only by the Ember service, through LibreHardwareMonitorLib and the PawnIO driver, and only for headers you have tested and enabled. The app never touches hardware.

- At 90 °C on any source every controlled fan goes to 100% (released at 85 °C). A missing or stale sensor sends the affected fans to 100%.
- Fans never go below a floor: system fans 20%, CPU fan 30%, GPU fan 30%. The CPU fan ramps early: at least 60% from 70 °C, 85% from 75 °C and 100% from 80 °C.
- The pump is left entirely to the board.
- When the service stops, each system fan is left at the speed it had before Ember first touched it and the CPU fan at 100%. Ember never calls the library's restore call, because it left a fan stuck low on the author's MSI B550-A PRO. Restarting the PC returns the board to automatic control.

See [SECURITY.md](SECURITY.md) for how the service is locked down and how to report a problem.

## Why PawnIO

Motherboard sensors and fan headers sit behind port I/O that user mode cannot reach, so a kernel driver is needed. Many monitoring tools use WinRing0, which is vulnerable (CVE-2020-14979: any local user can read and write physical memory) and is flagged by Microsoft Defender. Ember uses the signed PawnIO driver instead. Details: [docs/driver-research.md](docs/driver-research.md).

## FAQ

**Is it safe to run?** Fan control has unit tests and has been checked on one real PC, and the safety rules above are enforced in the service. It is still beta. Test one header at a time and watch your temperatures.

**Does it work on my motherboard?** It is built and tested on an MSI B550-A PRO (NCT6687D). Other chips supported by LibreHardwareMonitor may work; [docs/windows-checklist.md](docs/windows-checklist.md) shows what has been verified.

**Why are some of my fans at 0 RPM in Super quiet?** Some fans stop below about 20% speed. Pick Quiet, or use Manual to set a higher minimum.

**Does it send data anywhere?** No. See [PRIVACY.md](PRIVACY.md).

**Can I use it with SignalRGB or NZXT CAM?** Not at the same time on the same devices (Logitech G HUB, SignalRGB and Ember all want to set the lighting; only one should be active at a time). Ember warns you about fan-control programs and can close them.

**Where is the roadmap?** See [ROADMAP.md](ROADMAP.md), and [CHANGELOG.md](CHANGELOG.md) for what changed in each version. Both are also inside the app, in Settings.

## Contributing

Issues and pull requests are welcome. See [CONTRIBUTING.md](CONTRIBUTING.md) and the [code of conduct](CODE_OF_CONDUCT.md).

## Licence and terms

MIT. See [LICENSE](LICENSE). Using Ember is also covered by the [Terms of Use](TERMS.md) and the [Privacy notice](PRIVACY.md). Third-party components are listed in [THIRD-PARTY.md](THIRD-PARTY.md): [LibreHardwareMonitorLib](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) (MPL-2.0, used unmodified), [OpenRGB](https://openrgb.org) (GPL-2.0, bundled as a separate program) and PawnIO by namazso (a separate driver, installed separately).

## Disclaimer

Ember is an independent project by Ol775. It is not affiliated with, endorsed by or sponsored by NZXT, MSI, Govee, Logitech, OpenRGB, SignalRGB, Noctua, AMD, Intel or NVIDIA. Product names are trademarks of their respective owners. Use at your own risk: you are responsible for your hardware.
