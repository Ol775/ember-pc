# Hardware checks

<sub>[← All docs](README.md)</sub>

Things that pass their unit tests but still need a person with the real hardware to try them. Beta testers: see [beta-testing.md](beta-testing.md) for how to try a beta and report what you find. Tick an item when it has
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
- [x] **GPU fan control** (RTX 3060, 2026-10-13, after the fixes in docs/known-issues.md: silent card idle in Super quiet, steady curve-following in Extreme, hand-back on switching off): with Experimental on and the GPU option ticked, the GPU fans follow the curve, and
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
- [ ] Production build: no Roadmap page, What's new lists only production releases, no Experimental switch; every integration is there, with Nanoleaf and Razer marked Beta; the release channel choice is there and Beta offers to switch to the newest beta.
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

## Philips Hue (confirmed working on a real PC with a bridge and 6 lights, 2026-10-14)

_The unticked lines below were written before the check; the owner confirmed the integration works._

- [ ] Find bridge lists the bridge, or typing its address works.
- [ ] Press the bridge button, then Link bridge: the note says linked, and the lights appear on the Lights page and the Devices page.
- [ ] Hue lights follow the scene, with the right colour and brightness (xy colour conversion), without flooding the bridge when several lights are linked.
- [ ] Black or the lights switched off turns the Hue lights off, and switching on brings them back.
- [ ] Unlink forgets the bridge and its key; linking again works. A bridge whose address changes shows "could not reach" and recovers when its address is updated.

## Games (marked Beta in every build)

- [ ] Games page: the games you have installed appear under Steam, Epic Games, GOG, Ubisoft Connect and the EA app, with a count. Scan now rescans; installing or removing a game and scanning again changes the list.
- [ ] With Experimental features on, start a library game in a window (windowed or borderless): within about 5 seconds the Fans page shows game mode and the Games page shows "Playing: <game>". Closing the game returns to the normal mode.
- [ ] "Use a fan mode for each game" is off by default. Switch it on, give one game Performance, start it: the Fans page shows that mode (marked as a game override) and it ends with the game. A game with no pick uses the game mode.

## Meross (marked Beta in every build; written from the community-documented protocol and unit-tested, never tried on a device)

- [ ] Settings, Meross: sign in with the Meross account; the note says how many bulbs and light strips the account lists (plugs, hubs and thermostats are ignored). A wrong password says so plainly. With two-step verification on the account, signing in without a code asks for one, and the code box then works.
- [ ] Within about a minute the devices appear on the Devices page (same Wi-Fi as the PC). If they do not, note the Meross status line.
- [ ] Place a bulb on the Lights page: it follows the scene colour about once a second, and goes off when the lights go off.
- [ ] Plugs, hubs and thermostats on the account never appear on the Devices page and are never switched.
- [ ] Tapo (marked Beta in every build, written from the community-documented KLAP protocol and unit-tested against a simulated device, never tried on a real one): Settings, Tapo: save the account. Within a minute bulbs appear on the Devices page and follow the scene; Tapo plugs appear on the Power page. A wrong password says no device accepted it. Remove forgets the account.
- [ ] Power page: CPU and GPU show plausible watts that rise under load; Motherboard and the rest says it needs the wall plug until one is linked. Link a plug with energy monitoring and mark it as the PC's plug: Overall switches to the wall draw and Everything else shows the difference. Another linked plug shows its own watts. No plug is ever switched on or off by Ember. Unlinking clears the figures.
- [ ] Sign out forgets the account and the devices disappear. The Meross and Tapo cards and settings are marked Beta.

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

- [ ] Settings, About: Copy diagnostic report puts versions, integration status, devices and the recent log on the clipboard (no keys or passwords), and Open log folder opens %AppData%\Ember\logs, which holds one ember-yyyymmdd.log per day.
- [ ] Devices page: every integration card (OpenRGB, Govee, Logitech, Hue, Nanoleaf, Razer, Corsair) has a switch; off stops Ember looking for or sending to its devices (and lets go of G HUB, iCUE or OpenRGB), on brings them back, and the choice is remembered after a restart.

## Corsair iCUE (confirmed working on a real PC once iCUE was updated, 2026-10-14; the older CUE SDK fallback is untested on hardware)

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

- [ ] Corsair with an iCUE that refuses the newer SDK: Ember falls back to the older CUE SDK and lists the devices; the Devices page and the log say which SDK is in use.

- [ ] Automatic updates: with an older Ember and a newer release available, the update prompt appears after about 8 seconds (or a tray notification when the window is hidden); Update now installs, Remind me later asks again at the next check, Skip this version stays quiet for that version, and the setting in Settings, Updates turns the checks off.

## Several devices and shared paint files

Unit-tested (fills across the canvas, the library folder, name safety). The buttons on the Lights page were not reached by
UI automation, so they still need a person to click them.


- [ ] Lamps found later by Govee make room on the canvas without overlapping, and "Arrange devices" tidies a hand-made layout.
- [ ] Logitech G keys: a keyboard with G keys (G915: G1 to G5 at the side, G6 to G9 on top) shows a "Logitech G keys and badge" device with ten lights (G1 to G9, then the LIGHTSPEED badge) on the Lights page; painting or an effect on it lights each G key, and a keyboard with fewer G keys ignores the rest. (Seen on the G915 in the beta.3 build: the keyboard and G keys stay lit together.) Seen: _
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

- [x] After a few days of use, AI assist shows a suggested curve and explains it. (Checked 2026-10-14 after 21 hours of readings: it explained the usual range and gave both curves.)
- [ ] Nothing changes on the fans until Apply is pressed; Dismiss leaves the current curve alone. (Dismiss checked: the settings file was byte for byte the same. Apply not tried on the PC.)
- [ ] The applied curve still obeys the safety rules (full speed at 90 °C, never below the stall floor).
- [x] The suggestion follows the noise or cooling slider (quieter to cooler). (Checked: 0, 25, 50 and 100 on the slider give floors of 20, 24, 28 and 35 percent.)
- [ ] The learning data stays on this PC (see PRIVACY.md) and can be cleared from Settings.

## Personalisation (beta.5; seen on the PC with a test settings file, 2026-10-15)

- [x] Interface size Large drew the whole window larger and began on the Lights page when chosen as the start page (seen on screenshots).
- [x] GPU colour Teal made the GPU number and chart line teal with a Blue accent (seen).
- [ ] Clicking Small, Normal and Large in Settings, Appearance changes the size at once, and a narrow window widens when the size goes up.
- [ ] Clicking a GPU colour swatch, then Auto, changes the GPU number and chart line at once in dark, OLED and light themes.
- [ ] With "Open the window where I left it" on: move and resize the window, close and reopen Ember (from the tray too); it comes back in the same place and size. Unplug or change a monitor and check it still opens on screen.
- [ ] The Lights and Devices start pages open on those pages.

## Stream Deck plugin (beta; the install and a hello plugin were seen on the PC, the real keys are not yet checked)

An unsigned plugin was installed by Stream Deck 7.6 and its program registered with the app (2026-10-14). Fan keys follow the fan safety rule: keep a temperature monitor open and be ready to set Performance.

- [ ] Devices page, Stream Deck card (beta builds only): Install opens the file in Stream Deck, it asks to confirm, and the card then says Installed with the plugin version.
- [ ] The "Ember (beta)" group shows seven actions in Stream Deck; the settings panel for Scene lists Ember's scenes, Fan mode lists the five modes, Brightness offers Brighter or Dimmer, and Temperature or fan speed offers the four readouts.
- [ ] Scene key: pressing it changes the lights and the key lights up while that scene is current.
- [ ] Lights on/off and Brightness keys change the lights; brightness stops at 0 and 100 percent.
- [ ] Fan mode keys and the Cycle key change the mode (watch the tray or the Fans page); the Cycle key goes Super quiet, Quiet, Normal, Performance, Extreme, then round again, and never selects Manual or AI.
- [ ] The four readout keys match the dashboard (CPU and GPU temperature, CPU fan and GPU fan speed) and show a dash when a reading is missing; temperatures turn amber from 70 C and red from 85 C.
- [ ] Open Ember brings the window to the front when Ember is running, and starts Ember (only one copy) when it is closed.
- [ ] With the Ember app closed the Scene, Lights and Brightness keys say "offline" (a press flashes the warning), Open still works, and they recover within a few seconds of Ember starting. The fan and readout keys follow the service, so they keep working while only the app is closed.
- [ ] Hold the Brightness key: it keeps stepping until released. Hold the Cycle key for about half a second: it goes back one mode instead of forward. Hold a Scene key: it steps to the next scene; a short tap picks the key's own scene.
- [ ] Stop the Ember service (or let it hang): within about five seconds the readout keys show a dash rather than the last numbers.
- [ ] Restarting Stream Deck, and restarting the Ember service, leave the keys working.
- [ ] Update (after installing a newer Ember) replaces the plugin after Stream Deck confirms; Remove deletes it (if Stream Deck holds the files, quit Stream Deck and try again).

## Schedule and alerts (beta)

- [ ] Settings, Schedule and alerts: add a window that covers the current time with a different fan mode and the lights off; within about 15 seconds the mode changes and the lights go off, and the status line names the window. Seen: _
- [ ] Switching the schedule off, or the window ending, puts the mode and lights back (but a mode picked by hand in between is kept). Seen: _
- [ ] A window that runs past midnight (22:00 to 07:00) and a window limited to some days behave as set. Seen: _
- [ ] While a game is running, the schedule waits and says so. Seen: _
- [ ] "Send a test alert" shows a Windows notification, and clicking it opens Ember. Seen: _
- [ ] With a low CPU limit (60 °C) under load, the hot alert appears once, not repeatedly. Seen: _
- [ ] Unplugging or stopping a case fan while the PC is warm gives the "A fan has stopped" alert (only try this with a fan you can safely stop for a moment). Seen: _

## The new look and the beta.5 additions (written and tested as a simulation only: the window, every page and the lighting engine ran against made-up readings and devices, never on the real PC)

- [ ] Lights: add an Aurora wave layer. Drag its colour handles, click the bar to add a stop (up to six), remove one, and change Speed, Softness and Direction (→ ← ↑ ↓ Out). The keyboard, RAM and the strip show the colours moving the way the preview does. Seen: _
- [ ] Lights: the Applies to chips. Take RAM off a layer and the layer stops lighting RAM and nothing else; the last device cannot be removed. Seen: _
- [ ] Lights: Solo layer plays only the chosen layer on the real devices and the preview; pressing it again, or picking another scene, brings the rest back. Seen: _
- [ ] Power: the standing charge changes the three costs, the ranges 10 min, 1 hour, Today and 7 days draw, and the tip names the part that uses the most. Seen: _
- [ ] Games: each Steam game shows its own cover when Steam has one in its picture cache, other games show their program icon; "Last played" fills in after a game has been in front for half a minute; "Not played yet" for the rest. Seen: _
- [ ] Devices: the logos (and the plain marks for Ember, OpenRGB, Govee, Meross and Nanoleaf) look right in dark, OLED and light themes. Seen: _
- [ ] Settings: Fan control, Experimental and Updates are laid out as rows; Check now, Update now (do not press it on the PC without meaning to), the channel picker and Open settings folder all still work. Seen: _
- [ ] Update now: the setup program is held and checked again just before it starts, so a changed file is refused with a message instead of running. (Hard to provoke by hand; covered by a unit test.) Seen: _
- [ ] Watch the Task Manager figures for Ember with the window open on the Dashboard, Fans and Power pages: about 2 % of one core on the Dashboard with live sensors (measured 2026-10-17 on beta.5, with no automation tool running) and about 230 MB is what was seen; a lot more than that is worth reporting. Seen: _
- [x] Games: a Steam game launched from Steam was detected within about a minute, the fans went to the game mode and back to Super quiet when it closed, and the Games page showed "Last played today" (seen on the PC 2026-10-17, beta.5). The Games page shows "No game running" once you have left the game for 5 seconds (by design: switching to Ember ends the game, as it does for the fans). Not seen: covers for other launchers, and the dates after a restart.
- [x] Lights: an Aurora wave layer limited to the Govee lamp showed in the live preview moving along its 12 segments while the other devices kept their colours (seen 2026-10-17). Seen on the lamp itself: _
- [ ] Devices: rename a device by typing in its name box and pressing Enter; the new name shows on the Lights page and in Applies to, and clearing the box brings back the device's own name. Seen: _
- [ ] Window: make the window as small as it goes (1040 by 600 in the new look): the Dashboard rings and words, the Lights editor and the Devices rows are not cut off. Seen: _
- [ ] Lights: Spectrum bars on a single-row light (the Govee lamp or a strip) with a steady colour layer underneath: in silence the colour shows, with sound the bars add brightness. Seen: _
