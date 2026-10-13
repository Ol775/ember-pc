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
- **Layered effects.** Stack effects with blend modes and save them as scenes.
- **Screen sync.** Lights follow the colours on your screen.
- **Experimental features.** Game detection (switches to a game mode) and GPU fan control sit behind an Experimental switch in Settings, off by default.
- **Private by design.** The app and service make no network calls of their own. The Govee cloud is used only if you add a key, which is stored in Windows Credential Manager.

## Screenshots

<p align="center">
  <img src="docs/images/fans.png" alt="Fans page with modes and curve graph" width="48%">
  <img src="docs/images/lights.png" alt="Lights page with devices, effects and scenes" width="48%">
</p>
<p align="center">
  <img src="docs/images/fan-headers.png" alt="Fan header testing and enabling" width="48%">
  <img src="docs/images/settings.png" alt="Settings page" width="48%">
</p>

## Requirements

- Windows 10 or 11 (x64)
- .NET 8 Desktop Runtime
- Administrator rights to install the services and the driver (the app itself runs as a normal user)
- The **PawnIO** driver, for motherboard temperatures and fan headers. Without it, CPU and GPU temperatures still work and the app explains how to install it.

## Install

Ember is in beta and is built from source for now.

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

**Can I use it with SignalRGB or NZXT CAM?** Not at the same time on the same devices. Ember warns you about fan-control programs and can close them.

**Where is the roadmap?** See [ROADMAP.md](ROADMAP.md).

## Contributing

Issues and pull requests are welcome. See [CONTRIBUTING.md](CONTRIBUTING.md) and the [code of conduct](CODE_OF_CONDUCT.md).

## Licence

MIT. See [LICENSE](LICENSE). Third-party components are listed in [THIRD-PARTY.md](THIRD-PARTY.md): [LibreHardwareMonitorLib](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) (MPL-2.0, used unmodified), [OpenRGB](https://openrgb.org) (GPL-2.0, bundled as a separate program) and PawnIO by namazso (a separate driver, installed separately).

## Disclaimer

Ember is an independent project by Ol775. It is not affiliated with, endorsed by or sponsored by NZXT, MSI, Govee, OpenRGB, SignalRGB, Noctua, AMD, Intel or NVIDIA. Product names are trademarks of their respective owners. Use at your own risk: you are responsible for your hardware.
