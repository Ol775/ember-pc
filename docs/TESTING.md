# Testing list

Things that pass their unit tests but still need a person with the real hardware to try them. Tick an item when it has
been checked on the PC, and write what was actually seen. Update this list whenever a feature is added.

**Fan safety first:** while testing anything that touches fans, keep a temperature monitor open and be ready to set the
mode to Performance.

## Carried over from "in progress"

- [ ] **Govee segments**: detection finds the right segment count on a segmented lamp, per-segment colours land on the
      right segments, and gradients run in the right direction. Seen: _
- [ ] **Per-key keyboard layout**: reactive keys, ripples and per-key painting line up with the real keys on the
      Logitech keyboard (and on a keyboard from another brand through OpenRGB). Seen: _
- [ ] **HDR screen colours**: screen sync on an HDR display gives sensible colours, not washed out or too dark. Seen: _
- [ ] **Logitech lighting on more keyboards and mice**: per-device colour and effects work on each model tried. Seen: _
- [ ] **Game detection**: with Experimental on, a game starting raises the fans, and closing it releases them.
      A browser or video player is not mistaken for a game. Seen: _
- [ ] **GPU fan control**: with Experimental on and the GPU option ticked, the GPU fans follow the curve, and
      automatic control comes back when Ember stops. Seen: _

## Tray icon and start minimised

Already seen on the PC: `--minimised` starts with no window, the tray icon registers with Windows, close-to-tray hides the
window and keeps the app running, and the tooltip shows the mode and CPU temperature. Not yet seen (automation could not
reach the tray): the menu, double-click to open, and Quit.


- [ ] Closing the window with "Keep running in the tray" on hides it; the tray icon stays and fan control carries on.
- [ ] Double-clicking the tray icon, or Open Ember in its menu, brings the window back to the front.
- [ ] Quit Ember in the tray menu really closes the app (no Ember.App process left in Task Manager).
- [ ] The fan mode items in the tray menu change the mode, and the tick follows the current mode.
- [ ] With "Start Ember when I sign in" and "Start minimised" both on: sign out and in again, Ember starts in the
      tray with no window.
- [ ] With "Keep running in the tray" off, closing the window closes the app as before.
- [ ] The tray icon looks right on a light and a dark taskbar, and at 125% and 150% scaling.

## Continuous integration and signing

- [x] A push to GitHub runs the build workflow and it goes green (seen 2026-10-13, run 38393319698).
- [ ] The workflow artifact (installer and zip) installs on a clean PC. (The Release build workflow itself ran green and produced them, run 38393640897.)
- [ ] After a signing certificate is added to the repository secrets: the installer shows the publisher name in the
      Windows prompt instead of "Unknown publisher", and `signtool verify /pa` passes.

## More effects (fire, rain, per key group)

- [ ] Fire looks like flames on a strip and on a keyboard, and flickers rather than strobes.
- [ ] Rain drops fall from the top of a keyboard or grid, and on a single strip they travel along it.
- [ ] Key-group effects: a group colour only lights its own keys (function row, number row, letters, arrows, numpad).
- [ ] None of the new effects flash fast enough to be uncomfortable at their default speed.

## Several devices and shared paint files

Unit-tested (fills across the canvas, the library folder, name safety). The buttons on the Lights page were not reached by
UI automation, so they still need a person to click them.


- [ ] Lamps found later by Govee make room on the canvas without overlapping, and "Arrange devices" tidies a hand-made layout.
- [ ] Logitech: with the default (per-key) the keyboard shows individual keys for Rain, Fire and Key group, and the mouse keeps its G HUB colour; with "one colour for every Logitech device" on, both take the effect colour, and switching back restores per-key.
- [x] Key highlight: W A S D light correctly on the G915 (seen 2026-10-13). Still to check: other letters and the number row, the bottom letter row (Z to M), and a different keyboard model.
- [ ] "Give it its own lighting" on the Logitech keyboard: its layers can be edited alone (banner on the layer list), the Govee lamps and fans keep the scene, and "Follow the scene again" puts it back.
- [ ] Devices page: switching a device off under "Use" stops Ember sending to it (it keeps its last colour), it shows "Disabled", the choice survives a restart, and switching it on brings it back.
- [ ] Painting a picture and applying it to several devices at once resamples sensibly on each.
- [ ] Saving a paint to the library, closing Ember and loading it back gives the same picture.
- [ ] Deleting and renaming library entries works, and a damaged file in the library folder is ignored.
- [ ] Importing a paint file from another PC works.

## AI assist

Seen on the PC with seeded learning data: the panel shows, "Suggest a curve" gives readable notes and two curves, and the
Apply button was pressed (the mode and curves were put back to Super quiet and the Normal curve afterwards). Not yet seen:
real learning over days, and that the applied curve keeps temperatures sensible.

- [ ] After a few days of use, AI assist shows a suggested curve and explains it.
- [ ] Nothing changes on the fans until Apply is pressed; Dismiss leaves the current curve alone.
- [ ] The applied curve still obeys the safety rules (full speed at 90 °C, never below the stall floor).
- [ ] The suggestion follows the noise or cooling slider (quieter to cooler).
- [ ] The learning data stays on this PC (see PRIVACY.md) and can be cleared from Settings.
