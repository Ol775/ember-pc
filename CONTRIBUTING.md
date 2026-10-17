# Contributing

Thanks for helping. Ember controls real fans, so changes are held to a high bar.

## Set up

You need Windows 10 or 11, the .NET 8 SDK and git.

```powershell
git clone https://github.com/Ol775/ember-pc
cd ember-pc
scripts\fetch-openrgb.ps1
dotnet build Ember.sln
dotnet test
```

The window itself runs as a simulation in `tests/Ember.App.Tests` (no devices, network or service; set `EMBER_UI_TESTS=1`). The service's hardware, pipe-security and install code can only be verified on Windows, by hand: see [docs/hardware-checks.md](docs/hardware-checks.md). How the projects fit together: [docs/architecture.md](docs/architecture.md). Installing a build you made: [docs/install.md](docs/install.md).

## Pull requests

- Open an issue first for anything large.
- Keep a change focused. Add or update tests; the suite must pass.
- Anything that writes to a fan must keep the safety rules in the service (90 °C override, floors, CPU guard, blind-sensor rule). Do not weaken them.
- Never call the library's `SetDefault` or `Close` on fan controllers (see the Safety section of the [README](README.md#safety)).
- Smart plugs are read only. Do not add a way to switch one.
- Match the style around your change (see `.editorconfig`). Use British spelling in text, no emojis.
- Add a user-facing entry to [CHANGELOG.md](CHANGELOG.md) with a bold lead, and update the docs if behaviour changed.
- Say what you tested on real hardware, and what you could not.

## Reporting bugs

Use the bug report form on the [issues page](https://github.com/Ol775/ember-pc/issues/new/choose): your Windows version, motherboard, what you did, what happened, and the diagnostic report from Settings, About. Security problems go through [SECURITY.md](SECURITY.md), not a public issue.
