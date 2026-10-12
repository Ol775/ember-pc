# Driver research: PawnIO

Researched 2026-10-12 for phase 1 (Task 10).

## Why a driver at all

Ember reads motherboard fan headers and Super I/O temperatures through LibreHardwareMonitorLib (LHM). That
hardware sits behind port I/O and MSRs, which user mode cannot reach. LHM needs a kernel driver.

## LibreHardwareMonitorLib 0.9.6 uses PawnIO

LHM 0.9.6 (stable, released 2026-02-14, pinned in `Directory.Packages.props`) dropped WinRing0 and uses the
PawnIO driver. It ships PawnIO modules as embedded resources (`LibreHardwareMonitor.Resources.PawnIo.*.bin`:
LpcIO, IntelMSR, AMDFamily17, RyzenSMU, SMBus and so on), but **not** the driver itself. PawnIO has to be
installed separately.

### Why WinRing0 was rejected

- CVE-2020-14979: WinRing0's device object lets any local user read and write arbitrary physical memory, I/O
  ports and MSRs, which is a direct path to SYSTEM or kernel.
- Microsoft Defender detects it as `VulnerableDriver:WinNT/Winring0` and `HackTool:Win32/Winring0`, so an app that
  ships it gets quarantined or flagged.

## PawnIO editions

| Edition | Signed | Use in Ember |
|---|---|---|
| Official | Yes (Microsoft-attested kernel signature) | **Used: the only edition Ember supports** |
| Unrestricted | No (needs test signing or similar) | Not used |
| Open-source | Built from source, unsigned | Not used |
| Licensed | Commercial terms | Not used |

Ember uses ONLY the Official signed edition, installed from the official `namazso/PawnIO.Setup` GitHub releases.

## Licence and redistribution

pawnio.eu and github.com/namazso/PawnIO.Setup do not publish licence or redistribution terms for the setup
binary. So Ember **never bundles** the installer. `scripts/install-pawnio.ps1` downloads it from the official
release and verifies it before running it.

**Open question (must be answered before the phase 8 installer):** ask the PawnIO author (namazso) whether
Ember may bundle or chain-install `PawnIO_setup.exe`, and under what terms.

## Verified release (used as script defaults)

- Tag: `2.2.0` ("Release 2.2.0", published 2026-03-15, not a prerelease). This is the `releases/latest` result on 2026-10-12.
- Asset: `PawnIO_setup.exe`, 3,410,960 bytes, PE32+ x86-64.
- URL: `https://github.com/namazso/PawnIO.Setup/releases/download/2.2.0/PawnIO_setup.exe`
- SHA-256: `1f519a22e47187f70a1379a48ca604981c4fcf694f4e65b734aaa74a9fba3032`

How it was checked (on macOS; the binary was downloaded to a temp folder outside the repo and then deleted):

```
gh api repos/namazso/PawnIO.Setup/releases/latest   # tag 2.2.0; asset digest sha256:1f519a22...3032
curl -fsSL --proto '=https' -o "$D/PawnIO_setup.exe" https://github.com/namazso/PawnIO.Setup/releases/download/2.2.0/PawnIO_setup.exe
shasum -a 256 "$D/PawnIO_setup.exe"                 # 1f519a22e47187f70a1379a48ca604981c4fcf694f4e65b734aaa74a9fba3032
```

The local hash matches the digest GitHub publishes for the asset. The Authenticode signature was **not** checked
(it cannot be checked on macOS). The script enforces `Valid` on Windows and prints the signer subject and thumbprint.

## Download folder

The script downloads into a fresh `Ember-pawnio-<guid>` folder under Program Files, not ProgramData. In
ProgramData, BUILTIN\Users can create folders, so a non-admin could turn the new folder into a junction before
`Set-Acl` runs. Then the checks would pass against the genuine file and the junction would be re-pointed before
the installer runs elevated. Under Program Files, Users only have read and execute, so the folder is admin-only
from the moment it exists. The script also sets a protected ACL, refuses reparse points (on the folder and on the
file), and re-checks the hash and signature immediately before running the installer by its full path.

## Install switches

`PawnIO_setup.exe -install -silent` comes from third-party winget manifest documentation, not from the PawnIO
author. **It must be confirmed on Windows.** If it is wrong, the installer may show UI or return a non-zero exit
code, and the script fails on any non-zero exit code.

## Detection decision (`PawnIoProbe`)

LHM 0.9.6 has a **public** API: `LibreHardwareMonitor.PawnIo.PawnIo.IsInstalled` (static bool), plus `Version`
(static `System.Version?`). Checked by reflection over the win-arm64 runtime assembly and the package XML docs.
The type and both getters are public and static. Ember uses it.

How it works (from the IL): the static constructor reads
`HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO` value `DisplayVersion`, once with the default
registry view and once with an explicitly opened base key (64-bit view). `IsInstalled` is `Version != null`.

Why this API instead of opening `\\.\PawnIO` or querying `HKLM\SYSTEM\CurrentControlSet\Services\PawnIO`:

- Ember then reports exactly what LHM believes, so the "driver missing" banner and LHM's behaviour cannot disagree.
- No P/Invoke or native handle code to maintain.

Uncertainties:

- **It is cached per process.** The value is read once, in the static constructor. If PawnIO is installed while the
  service runs, the status stays `Missing` until the service restarts. LHM also opens the driver when `Computer.Open()`
  runs (at service start), so a restart is probably needed for fan RPMs anyway. The install script says to restart.
- It detects the **installer's uninstall registration**, not a loaded driver. A broken install (registry entry
  present, driver not loading) would show `Ok` with no fan sensors. If that turns up on real hardware, add a
  `\\.\PawnIO` open check.
- On non-Windows platforms, `ServiceHost` does not register the probe (status `Unknown`). Any exception inside the
  probe yields `Unknown`, and `SensorPoller` also catches and logs probe exceptions.

## Needs Windows verification

- [ ] `PawnIoProbe` reports `Ok` on a machine with PawnIO 2.2.0 installed and `Missing` on a clean machine.
- [ ] `install-pawnio.ps1` on a clean Windows VM under both PowerShell 5.1 and 7: the signature is `Valid`, the
      signer is printed, `-install -silent` runs with no UI and exit code 0, and the temp folder is removed.
- [ ] After the install, LHM fan RPMs appear. Check whether this needs a `Restart-Service EmberService` (expected,
      because of the per-process cache above) or works without one. Record the result here.
- [ ] The script refuses a bad `-Sha256`, and refuses when not elevated.
- [ ] Record the real signer subject and thumbprint of `PawnIO_setup.exe` 2.2.0 (`Get-AuthenticodeSignature`; the
      script prints both), then pin them in `install-pawnio.ps1` so a different valid signer is refused.

## Sources

- https://pawnio.eu
- https://github.com/namazso/PawnIO.Setup (releases: 2.2.0)
- https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/releases/tag/v0.9.6
- https://www.nuget.org/packages/LibreHardwareMonitorLib
- Neowin, "Windows 11/10 is flagging Winring0 on your PC monitoring, fan control apps, here's why": https://www.neowin.net/news/windows-1110-is-flagging-winring0-on-your-pc-monitoring-fan-control-apps-heres-why/
- NVD CVE-2020-14979: https://nvd.nist.gov/vuln/detail/CVE-2020-14979
