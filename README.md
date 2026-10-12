# Ember

Ember is a Windows PC monitoring app. A small Windows service reads CPU, GPU, motherboard and fan sensors
(through LibreHardwareMonitorLib) and streams them over a local named pipe to a WPF dashboard that shows live
temperatures, a 10-minute temperature history and fan speeds.

## Status

**Phase 2: monitoring and fan control (in verification).** Ember shows live sensors and can drive your motherboard fan
headers with Quiet, Normal, Performance and Extreme curves, switching to a game mode when it detects a game (you can
override it, or mark a program "not a game"). AI mode, lighting and the rest come in later phases. Fan control has
been unit-tested but is still being verified on real hardware: see
[`docs/windows-checklist.md`](docs/windows-checklist.md). Nothing is written to a fan until you test and enable that
header yourself.

## Safety

Fan speeds are written only by the Ember service, through LibreHardwareMonitorLib and the PawnIO driver, and only for
headers you have tested and enabled. The app never touches hardware; it can only choose a mode, report a game, and
enable or test a header. The service enforces the safety rules itself:

- at 90 °C on any source every controlled fan goes to 100% (released at 85 °C), and a missing or stale sensor sends the
  affected fans to 100%;
- fans never go below a floor (20%, pump 60%, GPU 30%);
- when the service stops, every fan Ember controls is handed back to the board's own control, and a separate
  **watchdog** service does the same if the service is killed or hangs.

If another program that controls fans is running (MSI Center, Fan Control, NZXT CAM, iCUE and similar), Ember warns
you by name and blocks enabling and testing headers until you close it. The app and service make no network calls; only
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
