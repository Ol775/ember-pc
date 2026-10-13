# Security

## Reporting a problem

Please report security problems privately, not in a public issue. Use GitHub's "Report a vulnerability" button on the Security tab of this repository, or contact Ol775 through the address on their GitHub profile. Include what you did, what you expected and what happened. You will get a reply within a few days.

## How Ember is locked down

- **Only the service writes to hardware.** The Ember service runs as LocalSystem and is the only part that talks to the PawnIO driver and writes fan speeds. The app and the lighting code run as your normal user.
- **Pipes are restricted.** The read pipe (`ember.v1`) and the control pipe (`ember.ctl.v1`) are created with an access list: SYSTEM and Administrators, plus the interactive user. Remote clients are refused.
- **Input is bounded.** Control messages are single JSON lines of at most 4 KB and are checked before use. Unknown or malformed messages are refused. Curves must pass the safety rules (at least 60% at 80 °C, at least 80% at 85 °C, 2 to 10 points).
- **Safety rules live in the service.** The 90 °C override, the CPU guard ramp, the fan floors and the blind-sensor rule cannot be turned off from the app.
- **The service folder must be admin-only.** `install-service.ps1` refuses folders that normal users can write, so a user cannot swap the executable that runs as SYSTEM.
- **Settings are protected.** `%ProgramData%\Ember` is writable only by SYSTEM and Administrators; users can read it.
- **Driver.** Ember uses the signed PawnIO driver, not WinRing0 (CVE-2020-14979). The install script checks the SHA-256 and signature.
- **Logitech.** Ember loads G HUB's lighting engine only from the file G HUB registers, and only if its name is `sdk_legacy_led_x64.dll` and it sits under Program Files (administrator-writable).
- **OpenRGB** is bundled, pinned by SHA-256 and listens on localhost only.
- **Secrets.** A Govee cloud API key is stored in Windows Credential Manager, never in a settings file or in the repository.
- **No telemetry.** See [PRIVACY.md](PRIVACY.md).

## Out of scope

Anyone who already has administrator rights on the PC can stop or replace the service. Ember does not try to defend against that.
