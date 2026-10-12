# Third-party software

## OpenRGB (bundled with the app build)

Ember's Lights page controls PC lighting through [OpenRGB](https://openrgb.org), which is shipped unmodified in the `OpenRGB`
folder next to `Ember.App.exe`.

- Version: 1.0 (Windows 64-bit), downloaded by `scripts/fetch-openrgb.ps1` from the official release and checked against a pinned SHA-256.
- Licence: GNU General Public License v2.0 (or later). OpenRGB is a separate program: Ember starts it and talks to it over its
  network SDK on this PC (127.0.0.1:6742). Ember does not link to or include OpenRGB's code. Ember itself is MIT-licensed.
- Source code and licence text: <https://codeberg.org/OpenRGB/OpenRGB> (release `release_1.0`).
- If you already run your own OpenRGB, Ember uses that one when it is listening and never starts a second copy.

## Libraries used by the app and service

LibreHardwareMonitorLib (MPL-2.0), PawnIO driver (installed separately, signed by its publisher). See `Directory.Packages.props`.
