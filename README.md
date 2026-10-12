# Ember

Ember is a Windows PC monitoring app. A small Windows service reads CPU, GPU, motherboard and fan sensors
(through LibreHardwareMonitorLib) and streams them over a local named pipe to a WPF dashboard that shows live
temperatures, a 10-minute temperature history and fan speeds.

## Status

**Phase 2 and the first lighting slice (alpha, in verification).** Ember shows live sensors and can drive your motherboard fan
headers with Quiet, Normal, Performance and Extreme curves, or your own **Manual** curves for the CPU fan and the system fans, drawn and edited on the Fans page. **Game detection** (switching to a game mode, with a manual
override and a "not a game" list) and **GPU fan control** are written and tested but are **beta features**: they are switched
off in this alpha build and arrive in the beta build (`BuildFeatures.Channel` in `src/Ember.Core/BuildFeatures.cs`). AI mode
and the rest come in later phases. The **Lights** page lights PC devices through OpenRGB (install it separately) and Govee lamps (LAN, or cloud with your own API key) with layered effects, scenes and screen sync. Fan control has
been unit-tested but is still being verified on real hardware: see
[`docs/windows-checklist.md`](docs/windows-checklist.md). Nothing is written to a fan until you test and enable that
header yourself.

## Safety

Fan speeds are written only by the Ember service, through LibreHardwareMonitorLib and the PawnIO driver, and only for
headers you have tested and enabled. The app never touches hardware; it can only choose a mode, report a game, and
enable or test a header. The service enforces the safety rules itself:

- at 90 °C on any source every controlled fan goes to 100% (released at 85 °C), and a missing or stale sensor sends the
  affected fans to 100%;
- fans never go below a floor (system fans 20%, CPU fan 30%, GPU fan 30%), and the CPU fan ramps early: at least 60% from 70 °C, 85% from 75 °C and 100% from 80 °C;
- the pump is left entirely to the board;
- when the service stops, each system fan is left at the speed it had before Ember first touched it and the CPU fan at 100%.
  LibreHardwareMonitor cannot hand a fan back to automatic control on this kind of chip (its restore call left a fan stuck
  low on the author's MSI B550-A PRO), so Ember never calls it. A restart of the PC returns the board to automatic control.
  A separate **watchdog** service writes the same safe speeds if the Ember service is killed or hangs.

If another program that controls fans is running (MSI Center, Fan Control, NZXT CAM, iCUE and similar), Ember warns
you by name and blocks enabling and testing headers until you close it. The Fans page has a **Close these apps** button\nfor that (it asks first, closes each program politely and ends it if it does not close), and **Restart as administrator** for\nprograms that run elevated. The app and service make no network calls; only
`scripts/install-pawnio.ps1` downloads, from GitHub. Ember keeps one small settings file in `%ProgramData%\Ember`.

## Requirements

- Windows 10 or 11 (x64)
- .NET 8 runtime (Desktop Runtime for the app)
- Administrator rights to install the service and the driver (the app itself runs as a normal user)
- The **PawnIO** driver, for motherboard temperatures and fan headers

### Why PawnIO

Motherboard sensors and fan headers sit behind port I/O that user mode cannot reach, so a kernel driver is needed.
Many monitoring tools use WinRing0, which is vulnerable (CVE-2020-14979: any local user can read and write physical
memory) and is flagged by Microsoft Defender. Ember uses the signed PawnIO driver instead, installed separately from
its official release. Details: [`docs/driver-research.md`](docs/driver-research.md).

Without PawnIO, CPU and GPU temperatures still work and the app shows a banner explaining how to install it.

## Build and test

```powershell
dotnet build Ember.sln
dotnet test
```

The tests run on Windows and on an arm64 Mac. x64 macOS and Linux need the LibreHardwareMonitorLib dll selection
fixed first (phase 8 CI). The WPF app and the service's hardware, pipe-security and install code are only verified
on Windows.

## Install

1. Publish the service and the app:

   ```powershell
   dotnet publish src/Ember.Service -c Release -r win-x64 --self-contained false -o publish\service
   dotnet publish src/Ember.App -c Release -r win-x64 --self-contained false -o publish\app
   ```

2. In an elevated PowerShell, install PawnIO (downloads the official release and checks its SHA-256 and signature):

   ```powershell
   scripts\install-pawnio.ps1
   ```

3. Copy `publish\service\*` to a folder only administrators can write, such as `C:\Program Files\Ember\`, then,
   elevated:

   ```powershell
   scripts\install-service.ps1 -ExePath "C:\Program Files\Ember\Ember.Service.exe"
   ```

   The service runs as LocalSystem, so the script refuses folders that normal users can write (such as anything
   under your user profile).

4. Install the watchdog, a second service that restores automatic fan control if the Ember service dies:

   ```powershell
   dotnet publish src/Ember.Watchdog -c Release -r win-x64 --self-contained false -o publish\watchdog
   # copy publish\watchdog\* to C:\Program Files\Ember\Watchdog\, then, elevated:
   scripts\install-watchdog.ps1 -ExePath "C:\Program Files\Ember\Watchdog\Ember.Watchdog.exe"
   ```

5. Run `publish\app\Ember.App.exe` as your normal user. It shows "Service running" once connected. Open the Fans
   page, test one System Fan header, then enable it. Close any other fan-control program first.

If you install PawnIO after the service, restart it: `Restart-Service EmberService`.

To remove the service: `scripts\uninstall-service.ps1` (elevated).

## Licence

MIT. See [`LICENSE`](LICENSE).

Credits: [LibreHardwareMonitorLib](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) (MPL-2.0, used
unmodified as a library, compatible with this MIT project) and PawnIO by namazso (a separate driver, installed
separately).

## Trademarks

Ember is an independent project by Ol775. It is not affiliated with, endorsed by or sponsored by NZXT, Logitech,
Govee, Noctua or NVIDIA. Product names are trademarks of their respective owners.
