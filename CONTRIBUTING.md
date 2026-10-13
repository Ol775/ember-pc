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

## Pull requests

- Open an issue first for anything large.
- Keep a change focused. Add or update tests; the suite must pass.
- Anything that writes to a fan must keep the safety rules in the service (90 °C override, floors, CPU guard, blind-sensor rule). Do not weaken them.
- Never call the library's `SetDefault` or `Close` on fan controllers (see the Safety section of the README).
- Match the style around your change. Use British spelling in text, no emojis.
- Say what you tested on real hardware, and what you could not.

## Reporting bugs

Include your Windows version, motherboard, what you did and what happened. Security problems go through [SECURITY.md](SECURITY.md), not a public issue.
