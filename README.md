# Ember

Ember is a Windows PC monitoring app. A small Windows service reads CPU, GPU, motherboard and fan sensors
(through LibreHardwareMonitorLib) and streams them over a local named pipe to a WPF dashboard that shows live
temperatures, a 10-minute temperature history and fan speeds.

## Status

**Phase 1: read-only monitoring.** Ember shows sensor values only. It does not control fans yet; fan control,
lighting and the rest come in later phases. The Windows-side behaviour is still being verified: see
[`docs/windows-checklist.md`](docs/windows-checklist.md).

## Safety

Phase 1 never writes to fans or any other hardware setting. It only reads sensors. Ember makes no network calls
and stores no data.

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

The tests run on any OS. The WPF app and the service's hardware, pipe-security and install code are only verified
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

4. Run `publish\app\Ember.App.exe` as your normal user. It shows "Service running" once connected.

If you install PawnIO after the service, restart it: `Restart-Service EmberService`.

To remove the service: `scripts\uninstall-service.ps1` (elevated).

## Licence

MIT. See [`LICENSE`](LICENSE).

## Trademarks

Ember is an independent project by Ol775. It is not affiliated with, endorsed by or sponsored by NZXT, Logitech,
Govee, Noctua or NVIDIA. Product names are trademarks of their respective owners.
