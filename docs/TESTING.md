# Testing list

Things that pass their unit tests but still need a person with the real hardware to try them. Tick an item when it has
been checked on the PC, and write what was actually seen. Update this list whenever a feature is added.

**Fan safety first:** while testing anything that touches fans, keep a temperature monitor open and be ready to set the
mode to Performance.

## Carried over from "in progress"

- [x] **Govee segments** (H6056: detected 12, a 12-colour rainbow showed in order on the bars, seen by the owner 2026-10-13): detection finds the right segment count on a segmented lamp, per-segment colours land on the
      right segments, and gradients run in the right direction. Seen: _
- [ ] **Per-key keyboard layout**: reactive keys, ripples and per-key painting line up with the real keys on the
      Logitech keyboard (and on a keyboard from another brand through OpenRGB). Seen: _
- [x] **HDR screen colours** (HDR desktop on the main monitor, two HDR videos, seen by the owner 2026-10-13; needed the empty-frame fix below): screen sync on an HDR display gives sensible colours, not washed out or too dark. Seen: _
- [ ] **Logitech lighting on more keyboards and mice**: per-device colour and effects work on each model tried. Seen: _
- [x] **Game detection** (LEGO Batman: Legacy of the Dark Knight, 2026-10-13: detected about 10 s after the GPU passed 50%, fans to Performance, back to Super quiet within seconds of leaving, detected again on return; the browser-not-mistaken-for-a-game part is covered by the exclusion list, not yet tried with a full-screen browser at high GPU load): with Experimental on, a game starting raises the fans, and closing it releases them.
      A browser or video player is not mistaken for a game. Seen: _
- [x] **GPU fan control** (RTX 3060, 2026-10-13, after the fixes in docs/FIXES.md: silent card idle in Super quiet, steady curve-following in Extreme, hand-back on switching off): with Experimental on and the GPU option ticked, the GPU fans follow the curve, and
      automatic control comes back when Ember stops. Seen: _

## Tray icon and start minimised

Already seen on the PC: `--minimised` starts with no window, the tray icon registers with Windows, close-to-tray hides the
window and keeps the app running, and the tooltip shows the mode and CPU temperature. Not yet seen (automation could not
reach the tray): the menu, double-click to open, and Quit.


- [x] Closing the window with "Keep running in the tray" on hides it; the tray icon stays and fan control carries on.
- [x] Double-clicking the tray icon, or Open Ember in its menu, brings the window back to the front.
- [x] Quit Ember in the tray menu really closes the app (no Ember.App process left in Task Manager).
- [ ] The fan mode items in the tray menu change the mode, and the tick follows the current mode. (Menu shows and works per the owner 2026-10-13, but a watch on the service never saw the mode leave Super quiet: repeat while watching.)
- [ ] With "Start Ember when I sign in" and "Start minimised" both on: sign out and in again, Ember starts in the
      tray with no window.
- [ ] With "Keep running in the tray" off, closing the window closes the app as before.
- [ ] The tray icon looks right on a light and a dark taskbar, and at 125% and 150% scaling.

## Appearance and the settings pages

- [x] Settings, Appearance: Dark, OLED black, Light and Match Windows switch at once; OLED black is pure black behind everything (checked on the PC, with the Cool scheme and Vivid highlights).
- [ ] Production build (1.0.0): no Roadmap page, no release channel choice (only production updates are offered), What's new lists only production releases, no Hue, Nanoleaf or Razer, no Experimental switch.
- [ ] Colour scheme Warm, Neutral and Cool, and each accent colour including Red, Teal and Gold, look right on Dark, OLED black and Light.
- [ ] A custom colour typed as `#rrggbb` is applied as you type, a wrong one is refused with a note, and a very dark or very light colour stays readable.
- [ ] Subtle, Normal and Vivid change the selected menu item and banner fill, and the preview under Highlights matches.
- [x] Updates, Roadmap, Legal and About pages show the new layout (checked on the PC); the settings menu ends with Legal, Updates, About.
- [ ] Legal: Expand all, Collapse all and Read on GitHub work; About: the three links open the project pages and Copy version details puts the text on the clipboard.

## Updates

- [x] Settings, Updates: Check now reads the public releases and, on the Beta channel, says "You have the latest version" when running beta.5 (2026-10-13).
- [x] With an older Ember installed (checked with beta.6 on the PC), the check on the Beta channel offers beta.7 and links to its release page.
- [x] Update now (checked beta.7 to beta.8 on the PC): downloads the installer, checks it, closes Ember and opens the upgrade wizard; after accepting the terms Ember is beta.8, the services run and the fans read.
- [x] Update now without the wizard (checked on the PC with the button, beta.9 to beta.10): about 16 seconds, no window, Ember reopened by itself as beta.10 with both services running. When the terms changed since you accepted them it should show the wizard instead (not tried).

## Continuous integration

- [x] A push to GitHub runs the build workflow and it goes green (seen 2026-10-13, run 38393319698).
- [x] The released installer upgrades an existing install silently (beta.5 over a dev build, 2026-10-13: services running, fans on Super quiet, settings kept).
- [ ] The released installer on a clean PC (never had Ember). (The Release build workflow itself ran green and produced them, run 38393640897.)

## More effects (fire, rain, per key group)

- [ ] Fire looks like flames on a strip and on a keyboard, and flickers rather than strobes.
- [ ] Rain drops fall from the top of a keyboard or grid, and on a single strip they travel along it.
- [ ] Key-group effects: a group colour only lights its own keys (function row, number row, letters, arrows, numpad).
- [ ] None of the new effects flash fast enough to be uncomfortable at their default speed.

## Philips Hue (written and unit-tested against a pretend bridge only)

- [ ] Find bridge lists the bridge, or typing its address works.
- [ ] Press the bridge button, then Link bridge: the note says linked, and the lights appear on the Lights page and the Devices page.
- [ ] Hue lights follow the scene, with the right colour and brightness (xy colour conversion), without flooding the bridge when several lights are linked.
- [ ] Black or the lights switched off turns the Hue lights off, and switching on brings them back.
- [ ] Unlink forgets the bridge and its key; linking again works. A bridge whose address changes shows "could not reach" and recovers when its address is updated.

## Nanoleaf (written and unit-tested against a pretend controller only)

- [ ] Find controller lists the controller, or typing its address works.
- [ ] Hold the controller power button 5 to 7 seconds, then Link: the note says linked, and one Nanoleaf device appears on the Lights and Devices pages.
- [ ] Effects run across the panels from left to right; a colour fill lights them all.
- [ ] Black or the lights switched off turns the panels off, and switching on brings them back.
- [ ] Unlink forgets the controller; linking again works.

## Razer Chroma (written and unit-tested against a pretend Synapse only)

- [ ] Settings, Razer Chroma, Use Razer Chroma: with Synapse running and Chroma Connect on, the Razer devices appear on the Lights and Devices pages.
- [ ] Effects reach the keyboard (22 columns by 6 rows) and the other Razer devices; devices you do not own can be switched off on the Devices page.
- [ ] Lights keep working for longer than a minute (the heartbeat keeps the Synapse session alive), and after Synapse restarts.
- [ ] Switching Razer Chroma off stops Ember talking to Synapse.

- [ ] Settings, About: Copy diagnostic report puts versions, integration status, devices and the recent log on the clipboard (no keys or passwords), and Open log folder opens %AppData%mberogs, which holds one ember-yyyymmdd.log per day.
- [ ] Devices page: every integration card (OpenRGB, Govee, Logitech, Hue, Nanoleaf, Razer, Corsair) has a switch; off stops Ember looking for or sending to its devices (and lets go of G HUB, iCUE or OpenRGB), on brings them back, and the choice is remembered after a restart.

## Corsair iCUE (written and unit-tested against a pretend iCUE only; the SDK library loads and connects)

- [ ] With iCUE running and Enable SDK switched on (iCUE Settings, Software and Games), the Corsair devices appear on the Lights and Devices pages.
- [ ] Effects reach a Corsair keyboard, mouse and fans/strips; iCUE keeps running and gets its own lighting back when Ember closes.
- [ ] With iCUE closed the Devices page says to start it, and the devices appear once it is running.

## Elgato Stream Deck and the second-copy fix

- [x] The Stream Deck is no longer listed as a light (checked on the PC).
- [x] A second Ember copy no longer holds G HUB: the Logitech keyboard appears once only one copy runs (checked on the PC).
- [ ] Starting Ember again while it is open (or hidden in the tray) brings the open window forward and does not start a second copy.

- [ ] Screen sync: with more than one screen the layer offers Main screen and the others; each scene keeps its own choice, the lights follow the chosen screen (DXGI and the fallback), and an unplugged screen falls back to the main one with a note.
- [ ] Devices page: Identify flashes the right physical device three times, also while the lights are off or the device is switched off in Ember.

- [ ] Dashboard temperature history: gridlines every 20 degrees in the chosen unit, minute marks, and a dot with a label at the right end of each line; Fans page: the curve shows a dot at the current temperature with a label of temperature and speed.
- [ ] Installer: with OpenRGB running from the Ember folder, and with PawnIO already installed, a quiet update completes and the summary lists what Ember needs.

- [ ] OpenRGB starts without a window and without the Zone Initialization question (needs a PC with addressable RGB headers, such as the MSI board JRAINBOW1 and 2).

## Several devices and shared paint files

Unit-tested (fills across the canvas, the library folder, name safety). The buttons on the Lights page were not reached by
UI automation, so they still need a person to click them.


- [ ] Lamps found later by Govee make room on the canvas without overlapping, and "Arrange devices" tidies a hand-made layout.
- [ ] Logitech: with the default (per-key) the keyboard shows individual keys for Rain, Fire and Key group, and the mouse keeps its G HUB colour; with "one colour for every Logitech device" on, both take the effect colour, and switching back restores per-key.
- [x] Key groups on the G915: Function row, Number pad, Arrows and navigation and Space bar row checked against photos (2026-10-13). Still to check: Number row, Letter keys, Space bar row, and other keyboard models.
- [x] Key highlight: W A S D light correctly on the G915 (seen 2026-10-13). Still to check: other letters and the number row, the bottom letter row (Z to M), and a different keyboard model.
- [ ] "Give it its own lighting" on the Logitech keyboard: its layers can be edited alone (banner on the layer list), the Govee lamps and fans keep the scene, and "Follow the scene again" puts it back.
- [x] Devices page: switching a device off under "Use" stops Ember sending to it (it keeps its last colour), it shows "Disabled", the choice survives a restart, and switching it on brings it back.
- [x] Painting a picture and applying it to several devices at once resamples sensibly on each.
- [x] Saving a paint to the library (saved, cleared and loaded back on the keyboard 2026-10-13; closing Ember not tried) and loading it back gives the same picture.
- [x] Deleting and renaming library entries works, and a damaged file in the library folder is ignored.
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
