# Installing Ember

<sub>[← All docs](README.md)</sub>

Most people only need **Option 1**. Options 2 and 3 are for building Ember yourself.

## Requirements

- Windows 10 or 11 (x64)
- .NET 8 Desktop Runtime (the installer adds it if it is missing)
- Administrator rights to install the services and the driver (the app itself runs as a normal user)
- The **PawnIO** driver, for motherboard temperatures and fan headers. Without it, CPU and GPU temperatures still work and the app explains how to install it.

## Which release?

- **Production** (for example 1.5.0) holds what has been checked on real hardware: sensors and fans, the Power and Games pages, OpenRGB, Govee, Logitech, Philips Hue and Corsair. A few things in it are marked **Beta** because they are written and tested but not yet proven on enough real devices: Razer Chroma, Nanoleaf, Meross, Tapo and game detection.
- **Beta builds** (for example 1.6.0-beta.1, marked as pre-releases on the [releases page](https://github.com/Ol775/ember-pc/releases)) add what is not in production yet: the schedule and alerts, GPU fan control, the Stream Deck plugin and the roadmap page.

You can also switch from inside Ember: Settings, Updates, Release channel moves the installation to the newest Stable or Beta version after you confirm.

## Option 1: the installer (easiest)

Download `Ember-Setup-<version>.exe` from the [releases page](https://github.com/Ol775/ember-pc/releases), run it, read and accept the Terms of Use, and follow the pages. It installs the .NET 8 and Visual C++ runtimes if your PC lacks them, adds the services, and downloads the PawnIO driver (checked before it runs). Ember appears in the Start menu and under Installed apps in Windows Settings, where you can also remove it.

Windows will show an "unknown publisher" warning because the installer is not code-signed yet (see [signing.md](signing.md)). You can check the file against the `.sha256` next to it on the releases page first: `Get-FileHash Ember-Setup-<version>.exe`.

**Silent install:** `Ember-Setup-<version>.exe /S /ACCEPT`. Add `/D=C:\Path\Ember` for another folder, `/NODESKTOP` for no desktop shortcut, `/NOPAWNIO` to skip the driver. `/ACCEPT` says you have read [TERMS.md](../TERMS.md). The installer writes a log to `%TEMP%\EmberSetup.log`.

## Option 2: from source, with one script

Build the release folder once. In PowerShell, from the repository folder:

```powershell
scripts\build-release.ps1          # fetches OpenRGB and the Microsoft runtimes, publishes everything, writes dist\Ember-<version>.zip and dist\Ember-Setup-<version>.exe
```

Then, in an **elevated** PowerShell, from the unzipped `dist\Ember-<version>` folder:

```powershell
scripts\install-ember.ps1          # installs to C:\Program Files\Ember, adds both services and a Start Menu shortcut
```

Run the same script again to upgrade; your settings are kept. An installed Ember can also update itself: Settings, Updates, Check now, then Update now. `scripts\uninstall-ember.ps1` removes it (add `-RemoveFiles` to delete the program files too).

## Option 3: by hand

The steps the script follows:

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

If you install PawnIO after the service, run `Restart-Service EmberService` (or press **Restart services** on the Devices page). To remove the service, run `scripts\uninstall-service.ps1` (elevated).

## Updating

Ember checks for updates when it starts and every few hours, and asks before it does anything: **Update now**, **Remind me later** or **Skip this version**. Settings, Updates has **Check now** and the **Release channel**. The installer is checked against the checksum published with the release before it runs.
