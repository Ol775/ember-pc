# Windows verification checklist (phase 1)

Phase 1 was written and unit-tested on macOS. The app, the service's hardware code, the pipe ACL and the install
scripts can only be verified on Windows. Run these checks on the Windows PC and fill in the **Result** column
(pass / fail) and **Notes**. Nothing in this table has been run yet.

Phase 1 is read-only. During every step, watch that no fan speed changes (check l).

## Setup

1. Publish both projects (from the repo root, in a normal PowerShell):

   ```powershell
   dotnet publish src/Ember.Service -c Release -r win-x64 --self-contained false -o publish\service
   dotnet publish src/Ember.App -c Release -r win-x64 --self-contained false -o publish\app
   ```

2. As administrator, copy `publish\service\*` to `C:\Program Files\Ember\` (a folder only admins can write).
3. Have HWiNFO (or Task Manager's Performance tab) and Sysinternals PipeList/AccessChk ready.

## Checks

| Step | Expected | Result | Notes |
|---|---|---|---|
| **a1.** On a clean machine (no PawnIO), elevated PowerShell: `scripts\install-pawnio.ps1` | Prints the signer subject and thumbprint, `SHA-256 OK`, the installer runs with no UI and exits 0, the `Program Files\Ember-pawnio-<guid>` folder is removed. | | |
| **a2.** Record the signer subject and thumbprint printed in a1 | Values written in Notes, to be pinned in `install-pawnio.ps1`. | | Subject: <br> Thumbprint: |
| **a3.** Repeat a1 under both Windows PowerShell 5.1 and PowerShell 7 (uninstall PawnIO in between) | Same result on both. | | |
| **a4.** After a1 with the service installed: `Restart-Service EmberService` | Service restarts; the app's driver banner disappears and fan RPMs appear. Note whether fan RPMs appeared before the restart. | | |
| **b1.** Elevated: `scripts\install-service.ps1 -ExePath "C:\Program Files\Ember\Ember.Service.exe"` | Succeeds; `Get-Service EmberService` shows Running, startup type Automatic. | | |
| **b2.** Uninstall, copy the publish output to a user-writable folder (for example `$env:USERPROFILE\EmberTest`), run install-service.ps1 against it (no `-AllowInsecurePath`) | Refused with "Refusing to install: non-admins can modify the service binary or its folder"; no `EmberService` is left behind. Reinstall from Program Files afterwards. | | |
| **b3.** `(Get-ItemProperty HKLM:\SYSTEM\CurrentControlSet\Services\EmberService).ImagePath` | Double-quoted path containing spaces: `"C:\Program Files\Ember\Ember.Service.exe"`. | | |
| **b4.** Run `scripts\install-service.ps1` from a non-elevated PowerShell | Refused: must be run elevated. | | |
| **c1.** Start `publish\app\Ember.App.exe` as a normal (non-admin) user, not elevated | Window titled "Ember"; the app connects and the sidebar shows "Service running" with a green dot. No UAC prompt. | | The connector trusts the pipe only if its server PID equals the PID the Service Control Manager reports for EmberService; a normal user must be able to do this. |
| **c2.** Compare CPU, GPU, Motherboard and Fans (average) with HWiNFO or Task Manager | Values agree within a few degrees / a few percent of RPM. Missing sensors show "—", never 0. | | |
| **c3.** On a machine with two GPUs (or an iGPU plus a dGPU) | The GPU card shows the hottest GPU. | | |
| **c4.** Leave the app running 2 minutes | History chart draws CPU (orange) and GPU (blue) lines that move left over time; values stay within 20 to 100 °C. | | |
| **d1.** Elevated: `Stop-Service EmberService` with the app open | Within 2 s the app shows "Service not running"; cards show "—", the fan list is empty; the app does not crash. | | |
| **d2.** `Start-Service EmberService` | The app reconnects by itself within 6 s and shows live values again. | | |
| **e1.** Stop the service, close and restart the app, then start the service | The app shows "Service not running", then "Service running" once the service is up, without user action. | | |
| **f1.** Elevated: `Stop-Process -Name Ember.Service -Force` | Windows restarts the service within about 5 s (service recovery); the app reconnects. | | |
| **f2.** Make startup fail (for example, rename `C:\Program Files\Ember\LibreHardwareMonitorLib.dll` or otherwise make the sensor/driver layer throw at start), then `Start-Service EmberService`; restore the file afterwards | Service fails to start with a Critical event in the Event Log. Note whether SCM applies the failure actions (restart attempts visible in the System log, event 7031/7034) for a start failure. | | |
| **g1.** `Get-Acl \\.\pipe\ember.v1 \| Format-List` (or `accesschk.exe \\.\pipe\ember.v1`) | SYSTEM: full control; Administrators: ReadWrite; INTERACTIVE: Read only. No Everyone, Users or Authenticated Users entries. | | |
| **g2.** Start a second instance of the app while the first is open | Both connect and update (second pipe instance works). | | |
| **h1.** Stop the service. As a non-admin user, create a listening pipe named `ember.v1` (squatter), for example in PowerShell: `$s = New-Object IO.Pipes.NamedPipeServerStream('ember.v1', 'Out'); $s.WaitForConnection()` in one window. Then, elevated, `Start-Service EmberService` | The service refuses to start (non-zero exit) and logs a Critical "Cannot claim pipe ember.v1 exclusively" event. | | |
| **h2.** With the real service stopped and the squatter from h1 listening, start the app as a normal user | The app rejects the squatter (a non-service process serving `ember.v1`): it shows "Service not running" and no values. Close the squatter, start the service, the app connects. | | |
| **h3.** With the service running, confirm the app connects (c1), then `Stop-Service EmberService` and immediately start the squatter from h1 | The app shows "Service not running" and never shows values from the squatter. | | |
| **i1.** Uninstall PawnIO (Apps & features), `Restart-Service EmberService` | The app shows the banner "Motherboard sensors and fan headers need the PawnIO driver. Run scripts/install-pawnio.ps1 as administrator, then restart the Ember service." CPU and GPU temperatures still work. | | |
| **i2.** Run `scripts\install-pawnio.ps1` elevated, then `Restart-Service EmberService` | The banner disappears and fan RPMs appear, without restarting the app. | | |
| **j1.** Elevated: `scripts\install-pawnio.ps1 -Sha256 0000000000000000000000000000000000000000000000000000000000000000` | Refuses with "SHA-256 mismatch ... Refusing to run the installer"; nothing is installed. | | |
| **j2.** Run `scripts\install-pawnio.ps1` from a non-elevated PowerShell | Refuses: must be run elevated. | | |
| **k1.** Leave the app running 15 minutes; watch its memory (Task Manager, Details tab) | Memory stays flat (no steady growth after the first minute or two). | | Start: <br> End: |
| **l1.** During all of the above, watch fan speeds in HWiNFO | Fan speeds do not change while Ember runs (Ember's own code writes no fan or control values). | | |
| **m1.** `dotnet test` on Windows | `dotnet test` is green on Windows (as a normal user and as admin). | | |

## Driver items still unverified on Windows (from `docs/driver-research.md`)

| Step | Expected | Result | Notes |
|---|---|---|---|
| **r1.** With PawnIO 2.2.0 installed, check the app/service | `PawnIoProbe` reports `Ok` (no banner). On a clean machine it reports `Missing` (banner). | | |
| **r2.** `install-pawnio.ps1`: `-install -silent` switches | Installer runs silently with no UI and exit code 0 (the switches come from third-party winget docs, not the PawnIO author). | | |
| **r3.** After installing PawnIO with the service already running | Record whether fan RPMs appear without `Restart-Service EmberService` (expected: a restart is needed, because LHM caches PawnIO status per process). Record the result in `docs/driver-research.md`. | | |
| **r4.** Signature check | `Get-AuthenticodeSignature` status is `Valid` for `PawnIO_setup.exe` 2.2.0; pin the subject and thumbprint recorded in a2 in the script so a different valid signer is refused. | | |
