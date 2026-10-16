# Signing the installer

<sub>[← All docs](README.md)</sub>

> On the back burner: Ember is released unsigned for now and Windows shows "Unknown publisher" (SmartScreen may warn and Smart App Control may block it). Options are listed in ROADMAP.md. The build can already sign when a certificate is supplied; nothing here needs doing until then.

Windows shows "Unknown publisher" for programs that are not signed with a code-signing certificate. Ember's build can sign
its three programs (`Ember.App.exe`, `Ember.Service.exe`, `Ember.Watchdog.exe`) and the installer, but the certificate has to
come from a certificate authority, and costs money. Ember does not ship one, and the repository never holds one.

## What you need

- A code-signing certificate exported as a `.pfx` file. An EV or OV certificate from a certificate authority removes the
  "Unknown publisher" text. A new OV certificate may still trigger SmartScreen warnings until the file builds reputation.
- The Windows SDK "Signing Tools" (`signtool.exe`). The GitHub `windows-latest` runner already has it.

## Signing on GitHub

1. Convert the certificate to text: `[Convert]::ToBase64String([IO.File]::ReadAllBytes('ember.pfx'))`.
2. In the repository, Settings, Secrets and variables, Actions, add `EMBER_SIGN_PFX` (that text) and
   `EMBER_SIGN_PASSWORD`.
3. Run the **Release build** workflow. The log says "Signed Ember.App.exe by ..." for each file when it worked, and the
   installer in the artifact carries the signature.

## Signing on your own PC

```powershell
$env:EMBER_SIGN_PFX = 'C:\path\to\ember.pfx'   # a path, or the base64 text
$env:EMBER_SIGN_PASSWORD = '...'
./scripts/build-release.ps1
```

Without `EMBER_SIGN_PFX` the build skips signing and says so. `scripts/sign-files.ps1` can also sign any file by itself.

## Checking

`signtool verify /pa /v dist\Ember-Setup-<version>.exe`, or right-click the file, Properties, Digital Signatures.
