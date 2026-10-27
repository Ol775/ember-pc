# Changelog

All notable changes to Ember. Newest first. Ember follows [semantic versioning](https://semver.org); versions with a label such as `beta.1` are pre-releases.

## 1.6.0-beta.4 (2026-10-27)

### Fixed

- **The lights preview moves smoothly.** It now redraws with every frame instead of ten times a second.

## 1.6.0-beta.3 (2026-10-27)

### Fixed

- **Light effects run smoothly on the devices and in the preview.** One slow device, such as a Wi-Fi bulb, no longer holds back the preview or the other devices.

## 1.6.0-beta.2 (2026-10-18)

### Fixed

- **Lights off now switches off every device**, including one Ember never lit since it started, such as NZXT lights left in their own effect. It sends a few black frames to each enabled device.
- **Layouts at small window sizes.** Power: the Currency and Export CSV buttons wrap under their label instead of overlapping text. Lights: the colour box and the Blend row fit a narrow card. Devices: the Beta tag no longer splits a name or covers the switch. Games: the per-game switch keeps clear of its text.

## 1.6.0-beta.1 (2026-10-17)

### Added

- **GPU fan on the Stream Deck shows a percentage** when the card idles with its fans stopped (0 RPM), instead of a bare 0.

## 1.5.0 (2026-10-17)

### Added

- **A lighting scene for each game.** On the Games page, with the per-game switch on, a game can have a scene of its own next to its fan mode. The scene (and lights on) show while the game runs, and the earlier scene and on or off come back when it ends, unless you chose another scene in between.
- **Global hotkeys.** Settings, General: with them on, Ctrl+Alt+Shift with L switches the lights, S goes to the next scene and F to the next fan mode, even while a game has the focus. A combination another program already holds is skipped.
- **Share scenes and fan curves as files.** On Lights, Export saves the chosen scene (with no device names in it) and Import adds one from a file; on Fans, Export curves and Import curves do the same for your two manual curves, and an imported curve is checked against the safety rules before it is used.
- **A compact view.** The Compact view button on the Dashboard (and the tray menu) opens a small window that stays on top with the temperatures, fan mode and power draw. Drag it anywhere; double-click or press Escape to close it.

### Changed

- **Nanoleaf panels sit on the Lights canvas as they hang on the wall** (a grid built from the controller's own layout) instead of in one row, so effects sweep across the real shape.
- **The G keys device appears only on a Logitech keyboard that has G keys**, and a keyboard Ember already drives through G HUB is no longer listed a second time from OpenRGB.

## 1.5.0-beta.1 (2026-10-17)

### Added

- **WiZ, LIFX, Yeelight and Kasa lights, and Kasa and Shelly plugs (beta).** Ember finds them on your home network with no account and no cloud, shows them on the Devices page, and lights them like any other device. Kasa and Shelly plugs with a power meter can be linked on the Power page; plugs are only ever read, never switched. Turn LAN Control on for Yeelight, local communication on for WiZ, and know that newer Kasa firmware that needs a TP-Link sign-in is not found.
- **Currencies on the Power page.** Pick any of 17 currencies next to the price per kWh; only the symbol changes, the price is your own.
- **Export the daily power figures as a CSV file** from the Power page's cost card.
- **A tray tooltip with the temperatures and the draw.** Hover the tray icon for the fan mode, CPU and GPU temperature and the watts.

### Changed

- **Single-row Spectrum bars are see-through in silence**, so a steady colour layer underneath shows instead of being blacked out.
- **Last played is saved every minute** instead of every five.

## 1.4.0 (2026-10-17)

### Changed

- **The new look is in the stable build.** Every page is drawn again: an icon sidebar with a status card, Instrument Sans and JetBrains Mono type, rounded cards with small capital headings, ring gauges on the Dashboard, the fan curve beside the fan headers, a live preview on Lights, and Settings as rows. The window can be made as small as 1040 by 600.

### Added

- **Lights: more control.** The Aurora wave takes up to six colours with a draggable stop bar, Speed, Softness and Direction. Each layer has *Applies to* chips to choose the devices it lights, a *Solo layer* button to play one layer alone, and Reset. Spectrum bars on a single-row light are see-through in silence.
- **Rename any device** on the Devices page: type a new name and press Enter; clear it to get the device's own name back.
- **Power: standing charge, more ranges and a saving tip.** Add your daily standing charge to the cost estimate, switch the chart between 10 minutes, 1 hour, today and 7 days, see a plain tip about the part that uses the most, show the cost in one of 17 currencies and export the daily figures as a CSV file.
- **Games: covers and last played.** Each game shows its Steam cover (from Steam's own picture cache) or its program icon, and a "Last played" line learned from the games that are in front of you. The dates stay on this PC.
- **Logos for every launcher and device.** The Steam, Epic Games, GOG, Ubisoft Connect and EA app marks head the Games cards; Ember, OpenRGB, Govee, Meross and Nanoleaf have plain marks of their own on the Devices page.
- **WiZ, LIFX, Yeelight and Kasa lights, and Kasa and Shelly plugs (beta).** Found on your home network with no account and no cloud; plugs are only read, never switched.
- **A tray tooltip with the temperatures and the draw.**

### Fixed

- **The Dashboard and Lights fit a small window.** Status words and tiles trim instead of wrapping, the layer editor fits its column, and Applies to lists devices by name.
- **Quieter on the PC.** The Dashboard, Fans and Power pages no longer measure unchanged text again every half second, so the window uses a few per cent of one core at most.

## 1.4.0-beta.1 (2026-10-17)

### Fixed

- **The new look fits a small window.** The Dashboard rings and the "Healthy" and "Working" words no longer wrap or cut off, the four summary tiles trim long text instead of running past their edge, and the window cannot be made narrower than the pages are laid out for.
- **The Lights layer editor fits its column.** The layer name sits above its Reset, move and Remove buttons, the sliders keep their track, the hex boxes show the whole colour, and Remove stop sits under the colour.
- **Applies to shows device names.** The chips list only the devices that are here and switched on, by name, instead of raw ids of devices that are gone.

### Changed

- **Rename shows at once on the Lights page.** A device renamed on the Devices page changes on the canvas straight away.

## 1.3.0 (2026-10-17)

### Added

- **Rename any device.** On the Devices page every device name is a text box: type a new name and press Enter. The new name shows on the Lights page and everywhere else, and clearing the box brings the device's own name back.
- **A gradient effect with colour stops.** The new Aurora wave rolls a row of up to six colours across the lights. Drag the handles on the colour bar, click the bar to add a stop, and set Speed and Softness. Scenes can now also limit a layer to chosen devices.
- **Logitech G keys and the LIGHTSPEED badge** show as a device on the Lights page on a keyboard that has them, so they can be painted and animated; the keyboard itself stays lit.

### Changed

- **Quieter on the PC.** The Dashboard, Fans and Power pages no longer measure unchanged text again every half second and long chart lines use fewer curve segments, so the window uses less processor time.
- **Friendlier messages.** "Asking Govee…" reads properly and the privacy notice lists Meross, Tapo, smart plugs and game library scanning.

### Fixed

- **The update installer cannot be swapped.** Update now holds the downloaded setup program and checks its checksum once more right before it starts, so a file that changed in between is refused.
- **Network folders are never read.** A game folder that points at another PC on the network is ignored.

## 1.3.0-beta.5 (2026-10-17)

### Added

- **Gradient layers with colour stops.** A new Aurora wave effect rolls any row of up to six colours across the lights. Drag the handles on the colour bar to move a stop, click the bar to add one, and set Speed, Softness and the Direction (right, left, up, down or out from the middle).
- **Choose which devices a layer lights.** Every layer has Applies to chips, one for each device on the canvas, so a layer can paint the keyboard only, or everything but the fans.
- **Solo layer.** Play one layer alone, on the lights and in the preview, to see what it does by itself. Reset puts a layer's effect back to its starting look.
- **Power: standing charge, more ranges and a tip.** Add your daily standing charge to the cost estimate, switch the chart between 10 minutes, 1 hour, today and 7 days, and read a plain tip about the part of the PC that uses the most.
- **Games: covers and last played.** Each game shows its Steam cover (from Steam's own picture cache) or its program icon, and a line such as "Last played 2 days ago", learned from the games that are in front of you. The dates stay on this PC (play-history.json in your Ember folder).
- **Logos for every launcher and device.** The Steam, Epic Games, GOG, Ubisoft Connect and EA app marks head the Games cards, and Ember, OpenRGB, Govee, Meross and Nanoleaf have plain marks of their own on the Devices page.

### Changed

- **Settings as rows.** Fan control, Experimental and Updates are laid out like General and Appearance: the name and a sentence on the left, the control on the right.
- **Quieter on the PC.** The Dashboard, Fans and Power pages no longer measure unchanged text again every half second, the hidden classic chart is no longer drawn, and long chart lines use fewer curve segments, so the window uses less processor time and makes less garbage.

### Fixed

- **The update installer cannot be swapped.** Update now holds the downloaded setup program and checks its checksum once more right before it starts, so a file that changed in between is refused.
- **Network folders are never read.** A game folder that points at another PC on the network is ignored, so a bad entry cannot make Ember contact it.

## 1.3.0-beta.4 (2026-10-16)

### Changed

- **A new look (beta).** Every page is redrawn: a sidebar with icons and a status card, Instrument Sans and JetBrains Mono type, rounded cards with small capital headings, and the Dashboard with ring gauges, a fan mode list, a smooth ten-minute chart and four summary tiles. Fans has the curve beside the fan headers and the AI assist; Lights has scene chips, a live preview, an effect gallery and a layer list; Devices shows each integration with its logo; Games shows a card for each launcher with a mode picker per game; Settings lists every option as a row. Only the beta build has it; the stable build looks as before.
- **Lighter in the tray.** With the window hidden or minimised, Ember redraws and reads the smart plugs far less often and hands unused memory back to Windows, so it idles at a fraction of the CPU and RAM.

### Added

- **A Specs page.** Your processor, graphics card, memory, motherboard, drives, displays and Windows version in one place, with a button to copy them for a support request. It never includes the PC name or your user name.
- **Drive health on the Specs page.** Each drive shows its S.M.A.R.T. status, temperature, hours powered on and, for SSDs, wear. Drives that are nearly full are flagged.
- **Power cost estimate and daily use.** The Power page shows what today, a month and a year of use cost at your own price per kWh, how the draw splits between the CPU, the graphics card and the rest of the PC, and the last seven days as bars. Ember keeps the daily energy figures on this PC (the file power-days.json in your Ember folder).

### Fixed

- **A failed click or timer no longer closes the window.** Errors are logged and Ember carries on; only running out of memory ends it.

## 1.3.0-beta.3 (2026-10-16)

### Fixed

- **The Logitech keyboard stays lit with the G keys.** In beta.2 the new G keys device sent its colours so often that the keyboard itself went dark. The G keys are now sent only when their colour changes, with a refresh every couple of seconds.

### Added

- **The LIGHTSPEED badge** on a Logitech G915 can be lit like the G keys: the device is now called "Logitech G keys and badge" and has ten lights (G1 to G9, then the badge).

## 1.3.0-beta.2 (2026-10-16)

### Added

- **A schedule for fan modes and lights (beta).** Settings, Schedule and alerts: add a time window, such as 22:00 to 07:00 on the days you choose, and pick a fan mode, a lighting scene or lights off for it. The mode and scene go back to what they were when the window ends, unless you changed them yourself in between. Nothing changes while a game is running.
- **Alerts for a hot part or a stopped fan (beta).** Switch them on in Settings, Schedule and alerts. Ember sends a Windows notification when the CPU or the graphics card stays over your limit, or when a fan that was turning stops while the PC is warm. Each alert is sent once, with a reminder after ten minutes.
- **Logitech G keys.** On a Logitech keyboard with G keys (G1 to G9) the Lights page now shows a "Logitech G keys" device, so you can paint and animate them like any other light.

### Fixed

- **Asking Govee** now shows a plain "Asking Govee…" instead of garbled characters.
- **The privacy notice** now lists Meross, Tapo, smart plugs and game library scanning.

## 1.3.0-beta.1 (2026-10-15)

### Changed

- **The beta line continues after 1.2.0.** This build has everything in 1.2.0 plus what is still being proven: GPU fan control (Experimental), the Stream Deck plugin and the roadmap page. Use Settings, Updates, Release channel to move between this beta and the Stable build.

## 1.2.0 (2026-10-15)

The production release. It adds the Power page, a Games page with better game detection, and Meross and Tapo lights, with the personalisation and fixes from the beta line.

### Added

- **A Power page.** Shows what your PC draws: the whole PC, the CPU, the graphics card, the motherboard and the rest, and any other plugs, each with the live watts, a small graph, the average, the peak and the energy used since Ember started, plus a large graph and a bar showing where the power goes. CPU and GPU come from their own sensors. No motherboard reports its own power, so the motherboard, memory, drives, fans and power supply losses are measured as the difference between the wall draw and the CPU and GPU: link a smart plug that measures energy (Meross MSS310 or MSS315, Tapo P110 or P115) and mark it as your PC's plug. Other linked plugs are listed with their own watts. Plugs are read only: Ember asks what they draw and never switches them. Figures are kept for the session only.
- **A Games page and better game detection (Beta).** Ember reads your Steam, Epic Games, GOG, Ubisoft Connect and EA app libraries and lists the games it finds. It scans when Ember starts, when you open the page and when you press **Scan now**, and only reads folders and files. A game from your libraries switches the fans to game mode after 4 seconds, needs only 20% GPU load, and is found even in a window; other programs are found the old way (full screen and a busy graphics card). Game detection is behind Settings, Fan control, Experimental features, and is off until you turn it on.
- **A fan mode for each game (Beta, off unless you switch it on).** On the Games page, switch on "Use a fan mode for each game" and pick a mode on any game. A game with no pick keeps using the game mode from the Fans page. The safety rules (90 °C override, early CPU ramp, speed floors) still apply.
- **Meross and Tapo / TP-Link lights (Beta).** Settings, Meross: sign in with your Meross account once (two-step codes are supported, and your password is never saved). Settings, Tapo: save your Tapo account email and password (kept in Windows Credential Manager, used only to sign in to your devices on this network). Ember finds your bulbs and light strips on the network and lights them with the scene, one colour at a time. Plugs, hubs and thermostats are never switched. Both are written and tested in software but not yet tried on enough real devices; for Tapo, switch on Third-Party Compatibility for the devices in the Tapo app if they are not found.
- **Restart services.** A button on the Devices page, on the Sensors and fans card, restarts the Ember service and watchdog without closing the app, which reconnects by itself. Windows asks for permission unless Ember runs as administrator. The fans go back to the board for a moment while it restarts.
- **More ways to make Ember your own.** Settings, Appearance has **Interface size** (Small, Normal or Large: the whole window, text and spacing together) and **GPU colour** (Auto, or any of the accent colours, for the GPU number and its line on the chart). Settings, General lets Ember **open on Lights or Devices** as well as Dashboard or Fans, and **open the window where you left it** (size and position, moved back onto the screen if that monitor is gone).

### Fixed

- **The GPU colour now matches your accent and theme.** With a Blue, Purple or Teal accent (and Green in the light theme) the GPU temperature and chart line fell back to a fixed bright orange. It is now a partner colour worked out from your accent and adjusted for the light or dark theme.

## 1.2.0-beta.8 (2026-10-15)

### Added

- **A Games page.** Ember reads your Steam, Epic Games, GOG, Ubisoft Connect and EA app libraries and lists the games it finds, grouped by launcher. It scans when Ember starts, when you open the page (if the last scan is more than five minutes old) and when you press **Scan now**. It only reads folders and files; nothing is changed or sent anywhere.
- **Better game detection.** A game from your libraries switches the fans to game mode after 4 seconds instead of 10, needs only 20% GPU load instead of 50%, and is found even when it runs in a window. Programs that are not in a library are found the old way (full screen and a busy graphics card).
- **A fan mode for each game (off unless you switch it on).** On the Games page, switch on "Use a fan mode for each game" and pick a mode on any game. A game with no pick keeps using the game mode from the Fans page. The safety rules (90 °C override, early CPU ramp, speed floors) still apply.

## 1.2.0-beta.7 (2026-10-15)

### Added

- **Tapo / TP-Link lights and plugs (beta builds only, not yet tried on real devices).** Settings, Tapo: save the email and password of your Tapo account (kept in Windows Credential Manager, used only to sign in to your devices on this network, never sent to the cloud). Ember finds Tapo bulbs and light strips and lights them with the scene, one colour at a time. Tapo plugs appear on the Power page and are only read, never switched. If devices are not found, switch on Third-Party Compatibility for them in the Tapo app.
- **A Power page (beta builds only).** Shows what your PC draws: the whole PC, the CPU, the graphics card, the motherboard and the rest, and any other plugs, each with the current watts, a small graph, the average, the peak and the energy used since Ember started, plus a large graph and a bar showing where the power goes. CPU and GPU come from their own sensors. No motherboard has a sensor for its own power, so the motherboard, memory, drives, fans and power supply losses are measured as the difference between the wall draw and the CPU and GPU: link a smart plug with energy monitoring (Meross MSS310 or MSS315, Tapo P110 or P115) and mark it as your PC's plug. Other linked plugs are listed with their own watts. Plugs are read only: Ember asks what they draw and never switches them. Figures are kept for the session only.
- **Restart services.** A button on the Devices page, on the Sensors and fans card, restarts the Ember service and watchdog without closing the app, which reconnects by itself. Windows asks for permission unless Ember runs as administrator. The fans go back to the board for a moment while it restarts.

### Changed

- **Meross is for bulbs and light strips only.** Plugs, hubs and thermostats are never switched; Meross plugs are only read, on the Power page.

### Fixed

- **A second Meross light was not found.** Devices on Wi-Fi power-save can take a couple of seconds to answer, more than Ember waited, so they were missed. Ember now waits four seconds, and the status line names any light it could not find.

## 1.2.0-beta.6 (2026-10-15)

### Added

- **Meross bulbs and plugs (beta builds only).** Settings, Meross: sign in with your Meross account once (two-step codes are supported, and your password is never saved) and Ember finds your bulbs and plugs on your network and controls them directly. Bulbs follow the scene as one colour; a plug switches on while its lights show a colour and off when they go dark. Not yet tried on real devices.

## 1.2.0-beta.5 (2026-10-15)

### Added

- **More ways to make Ember your own.** Settings, Appearance has **Interface size** (Small, Normal or Large: the whole window, text and spacing together) and **GPU colour** (Auto, or any of the accent colours, for the GPU number and its line on the chart). Settings, General lets Ember **open on Lights or Devices** as well as Dashboard or Fans, and **open the window where you left it** (size and position, moved back onto the screen if that monitor is gone). The Fans page is unchanged.

## 1.2.0-beta.4 (2026-10-14)

### Fixed

- **The GPU colour now matches your accent and theme.** With a Blue, Purple or Teal accent (and Green in the light theme) the GPU temperature and chart line fell back to a fixed bright orange. It is now a partner colour worked out from your accent and adjusted for the light or dark theme, so a Blue accent gets a pink GPU line. The default orange accent keeps its blue GPU line.

## 1.2.0-beta.3 (2026-10-14)

### Fixed

- **The Stream Deck card no longer covers its own title.** The Install, Update and Remove buttons now sit under the text instead of over the heading, and the note after an install goes away once the card changes state.
- **Letting go of a held Stream Deck key no longer flashes a false warning.** Holding Brightness, Cycle fan mode or a Scene key and releasing while the action was still being sent cancelled it half way.
- **The Stream Deck keys keep updating after a hiccup.** One failed refresh used to freeze every key until Stream Deck was restarted, and two redraws could leave an older picture on a key.

## 1.2.0-beta.2 (2026-10-14)

### Added

- **Stream Deck plugin (beta).** Settings, Devices has a new Stream Deck card with an Install button that adds an "Ember (beta)" group of keys to the Stream Deck app: switch lighting scene (hold to step to the next scene), lights on or off, brighter and dimmer (hold to keep going), set a fan mode, one key that steps to the next fan mode (hold to go back), open Ember (or start it when it is closed), and live CPU and GPU temperature and fan speed on the key faces, each with its own picture in Ember's orange. The scene, lights and brightness keys say "offline" while the Ember app is not running, and the readouts show a dash when the service stops reporting. The card also updates and removes the plugin. Beta builds only.

## 1.2.0-beta.1 (2026-10-14)

### Changed

- **The beta line continues after 1.1.0.** This build has everything in 1.1.0 plus what is still being proven: game detection and GPU fan control (Experimental) and the roadmap page. Use Settings, Updates, Release channel to move between this beta and the Stable build.

## 1.1.0 (2026-10-14)

The production release. It adds the lighting that has now been checked on real PCs, and everything from the beta line that is ready.

### Added

- **Philips Hue and Corsair iCUE lighting are in the production build.** Both are confirmed working on real PCs. Corsair works through iCUE with Enable SDK switched on, and falls back to Corsair's older SDK when a newer one is refused.
- **Nanoleaf and Razer Chroma are in the production build, marked Beta.** They are written and tested in software but not yet tried on real devices.
- **Automatic update checks, with a prompt.** Ember looks for a newer version at start and every few hours, and asks whether to update now, later, or skip that version (a tray notification when Ember is hidden). Turn it off in Settings, Updates.
- **The release channel switch moves you to that version.** Choosing Beta offers the newest beta, and choosing Stable on a beta build offers the newest stable release. Your settings are kept.
- **A switch for every integration** on the Devices page, and an **Identify** button that flashes one device so you can tell which it is.
- **Screen sync can follow a chosen screen**, kept per scene.
- **A diagnostic log and report** in Settings, About, with keys and passwords left out, for problem reports.
- **Temperature history and fan curve markers:** gridlines and a labelled dot for the temperature now.
- **A "What Ember needs" check at the end of install.**

### Changed

- **Updating works from shared and VPN networks.** If GitHub limits the usual request, Ember reads the public releases feed instead.
- **Install no longer fails** when the PawnIO driver is already installed or OpenRGB is running.
- **OpenRGB starts without a window**, so its "Zone Initialization" question no longer pops up.
- **A tidier Lights and Devices page,** friendlier status lines, hints in empty boxes, and consistent Settings cards.
- **The Elgato Stream Deck is no longer treated as a light.**
- **AI assist suggestions** never show a fan slower than Ember will run it.

### Fixed

- Logitech not being found when a second copy of Ember was running: Ember now runs once.

## 1.1.0-beta.7 (2026-10-14)

### Added

- **Automatic update checks, with a prompt.** Ember looks for a newer version when it starts and every few hours, and asks whether to update now, later, or skip that version. When Ember is in the tray, a notification tells you instead. It is on by default; turn it off in Settings, Updates. Nothing is downloaded until you choose Update now.

### Changed

- **Tidier Lights page.** The list of integrations is one line each, with a green dot when it is working. Technical details (version numbers and the like) now go to the diagnostic log, and the status lines say plainly what to do.
- **Devices page cards fit their content** instead of all being as tall as the biggest one, and OpenRGB says "Starting up, looking for devices" for its first 20 seconds.
- **Empty text boxes now say what goes in them** (scene name, picture name, OpenRGB path, Hue bridge, Nanoleaf controller), and the Govee key box matches the rest of the look.
- **Settings cards are all the same width**, and the "Restart as admin" button is no longer cut off.
- Layers no longer read "Wave · Wave" when the name matches the effect.
- If iCUE refuses Ember, the message now also suggests updating iCUE.

### Fixed

- **AI assist suggestions never show a fan slower than Ember will actually run it.** Ember keeps the CPU fan at 30% or more and system fans at 20% or more, but a suggestion could show 20 to 24% for the CPU fan.

## 1.1.0-beta.6 (2026-10-14)

### Added

- **Corsair: falls back to Corsair's older CUE SDK.** Some iCUE installs refuse the newer iCUE SDK even with Enable SDK switched on. Ember now tries the newer SDK first and, when iCUE refuses it or does not answer, uses the older one, which both ship in the `iCUE` folder. The Devices page and the diagnostic log say which one is in use and what each answered. Devices on Corsair's fan and LED controllers (Commander, Lighting Node) may not light through the older SDK.
- **`scripts/test-icue.ps1`** asks iCUE directly whether it lets a program connect, for tracking down a refusal.

## 1.1.0-beta.5 (2026-10-14)

### Added

- **A "What Ember needs" check at the end of install.** Setup lists whether the app and services, OpenRGB, the Corsair iCUE library, the PawnIO driver and the Visual C++ runtime are in place, and warns with a fix for anything that is not. Programs made by other companies (G HUB, iCUE, Razer Synapse, the Govee and Hue apps) are listed as optional.
- **Temperature history chart.** Gridlines every 20 degrees in your unit, minute marks, and a dot with a label at the end of each line showing the temperature now.
- **Fan curve marker.** A dot on the curve at the current temperature, with guides to both axes and a label such as "53 °C · 22%".

### Fixed

- **Install no longer fails when the PawnIO driver is already installed.** Setup skips it when the installed version is current, and closes OpenRGB for a moment to update an older one. A PawnIO setup that reports an error but leaves the driver in place no longer stops the install.
- **Install no longer fails when OpenRGB is running.** Setup closes programs running from Ember's own folder (the OpenRGB that Ember starts) and retries locked files. An OpenRGB you run from somewhere else is left alone.
- **OpenRGB's "Zone Initialization" window no longer pops up.** Ember starts OpenRGB as a server without a window.
- **Corsair: a clear message when iCUE refuses Ember.** It says to switch on Enable SDK in iCUE (Settings, Software and Games), restart iCUE, and try Ember without Run as administrator. The log records each iCUE session outcome once.

## 1.1.0-beta.4 (2026-10-14)

### Fixed

- **Updating no longer fails with 403 on shared or VPN addresses.** GitHub allows only 60 anonymous API requests an hour per address, so an office, school, VPN or shared connection could be refused. When the API says 403 or 429, Ember now reads the public releases feed instead (same releases, no such limit), with the same download and checksum checks. If both fail, the message explains the limit.
- **Update problems are logged.** The update check and the installer download write what GitHub answered to the diagnostic log.
- **The iCUE log is quieter.** With iCUE not running, the SDK's "connecting" and "timed out" cycle is logged once instead of every second.

## 1.1.0-beta.3 (2026-10-14)

### Added

- **A switch for every integration.** On the Devices page each card (OpenRGB, Govee, Logitech, Hue, Nanoleaf, Razer, Corsair) can be switched off and on. While off, Ember does not look for its devices or send to them, and lets go of G HUB, iCUE or OpenRGB.
- **Identify button.** Each device row on the Devices page has an Identify button that flashes that device white three times, so you can tell which physical device it is.
- **Choose the screen for screen sync, per scene.** With more than one screen, the Screen sync layer offers which screen to follow. Each scene keeps its own choice.
- **Diagnostic log and report.** A plain log of what Ember did and why something failed is kept in `%AppData%\Ember\logs` for a week and never leaves the PC. In Settings, About, **Copy diagnostic report** puts versions, integration status, devices and the recent log on the clipboard (keys and passwords are blanked), and **Open log folder** opens the folder.

### Changed

- **Corsair iCUE is more patient and more talkative.** A refused or lost iCUE connection is retried after 15 seconds instead of on every look, and the Corsair card and the log now say what iCUE answered.

## 1.1.0-beta.2 (2026-10-14)

### Added

- **Corsair iCUE support (beta, not yet tried on real Corsair hardware).** Keyboards, mice, headsets, fans, coolers, memory and LED controllers show up through Corsair's iCUE SDK, which Ember ships in an `iCUE` folder. iCUE has to be running with Enable SDK switched on (Settings, Software and Games). Ember lights the devices alongside iCUE and hands them back when it closes.

### Fixed

- **Elgato Stream Deck and Steam Deck no longer appear as lights.** OpenRGB lists them with key "LEDs"; Ember now leaves them out.
- **Logitech was not found when a second copy of Ember was running.** A leftover hidden copy (left behind by an update relaunch) kept G HUB's lighting session, so the visible Ember said "G HUB is installed but not answering". Ember now runs once per sign-in: starting it again brings the open window forward.

## 1.1.0-beta.1 (2026-10-13)

### Changed

- **The beta line continues after the first production release.** This build has everything in 1.0.0 plus what is still being checked: game detection and GPU fan control (Experimental), Philips Hue, Nanoleaf and Razer Chroma lighting, the roadmap page and the Beta update channel. Production builds hide these.

## 1.0.0 (2026-10-13)

The first production release. It holds what has been checked on real hardware; the newer, less-tested parts stay in the beta builds.

### Added

- **Monitoring.** A live dashboard of CPU, GPU, motherboard and fan sensors with a ten-minute history, in Celsius or Fahrenheit.
- **Fan control.** Super quiet, Quiet, Normal, Performance and Extreme modes, manual curves with an editor, and header tests before anything is written. Safety rules stay on: every controlled fan goes to full speed at 90 °C or when a sensor is lost, a watchdog service restores safe speeds, and fans that stall at low speed are kicked back to life. Ember warns when NZXT CAM, MSI Center, Fan Control or iCUE is running.
- **AI assist.** Off by default. Learns how hot your PC runs and suggests curves; nothing changes until you press Apply.
- **Lighting.** Devices through OpenRGB, Govee lamps (LAN, cloud and segments, also over a VPN) and Logitech G keyboards and mice, key by key. Layered effects, scenes, painting single LEDs with a library, own lighting for one device, screen sync (HDR too) and music-reactive effects.
- **Make it yours.** Dark, OLED black, light or match Windows; warm, neutral or cool surfaces; nine accent colours or your own; Subtle, Normal or Vivid highlights. A tray icon, keep running in the tray and start minimised.
- **Updates.** Check for updates on request or at start; Update now downloads the installer, checks it against the release checksum, updates and opens Ember again.
- **Install.** An installer with the Terms of Use, runtimes included, upgrade in place and uninstall from Windows.
- **Private.** No accounts and no telemetry; the only network use is what you switch on.

### Only in the beta builds

Game detection and GPU fan control (Experimental), Philips Hue, Nanoleaf and Razer Chroma lighting, and the roadmap page. They arrive in the production build when they have been checked on real hardware.

## 0.1.0-beta.10 (2026-10-13)

### Added

- **OLED black theme.** Settings, Appearance has a fourth theme with a pure black background for OLED screens, next to Dark, Light and Match Windows.
- **Colour scheme.** Warm (the original), Neutral grey or Cool blue-grey surfaces, for every theme.
- **More accent colours.** Red, Teal and Gold join the five from before, and a Custom colour takes any `#rrggbb`. A colour that would be hard to read is lightened or darkened a little.
- **Highlight strength.** Subtle, Normal or Vivid for how strongly the accent tints the selected menu item and banners, with a preview.

### Changed

- **Updates look tidier.** The version and its status sit together with a coloured dot, a newer release shows in a card with its notes and the Update now button, and the options are grouped below.
- **Roadmap** is grouped (being checked, next, later, not planned) with the finished work folded away, and **Legal** opens with a short plain summary, folds the terms into sections and links to the full text on GitHub. **About** shows the version, links, a button that copies version details for a problem report, and what Ember is built on.

## 0.1.0-beta.9 (2026-10-13)

### Added

- **Razer Chroma.** Settings has a Razer Chroma switch. On, Ember lights Razer keyboards, keypads, mousepads, headsets and Chroma Link strips through Razer Synapse on this PC (its local Chroma SDK, so Synapse must be running with Chroma Connect on). Mice are not used. Ember cannot tell which Razer devices are plugged in, so switch off the ones you do not have on the Devices page. Not yet tried on real Razer hardware.

### Changed

- **Update now skips the wizard.** If you have already accepted the Terms of Use, the installer updates quietly and Ember opens again by itself. If the terms changed, the wizard still asks first.

## 0.1.0-beta.8 (2026-10-13)

### Changed

- A version to try Update now on: nothing else is different from beta.7.

## 0.1.0-beta.7 (2026-10-13)

### Added

- **Update now.** When a newer release exists, Settings, Updates has an "Update now" button: it downloads the installer from the release, checks it against the checksum published with the release, then starts it (Windows asks for permission, Ember closes and the installer upgrades it). Nothing is downloaded until you press the button. Needs a release that carries the installer and its checksum file, as beta.6 and later do.
- **Nanoleaf.** Settings has a Nanoleaf section: find or type the controller address, hold its power button for 5 to 7 seconds and press Link. The panels then appear as one device on the Lights page, one light per panel from left to right, over the controller's local API (no account, no cloud). Not yet tried on real panels.

## 0.1.0-beta.6 (2026-10-13)

### Fixed

- **Screen sync no longer goes black on an HDR desktop.** Windows sends capture frames that only report the mouse pointer or metadata, with an empty picture, and Ember was reading those as a black screen. They are now skipped.
- **GPU fan control now suits a graphics card.** The fan is driven only at 60% or more (below that a card fan stops and starts) and is handed back to the card's own silent idle when the curve asks for less; the header test fits a card that is stopped at idle; and switching GPU control off really gives the fan back to the card. Still Experimental.

## 0.1.0-beta.5 (2026-10-13)

### Added

- **Tray icon.** Ember now has an icon in the notification area. Double-click it to open the window; its menu changes the fan mode or quits Ember. Settings has two new switches: "Keep running in the tray when I close the window" and "Start minimised, in the tray" (used when Ember starts with Windows). Both are off by default.
- **AI assist** (Fans page). If you switch on "Learn from my temperatures", Ember keeps a count of how hot your CPU and case run, one count per degree, in a small file on your PC. After about two hours it can suggest fan curves that stay low through your usual range and only climb above it, tilted towards quiet or cool with a slider. It only suggests: nothing changes until you press Apply, and the safety rules (full speed at 90 °C) and the service's own curve checks still apply.
- **More effects.** "Fire" (flames that are hot at the bottom and flicker upwards), "Rain" (drops that fall down a keyboard or run along a strip) and "Key group" (any of Colour, Wave, Breathing, Fire or Rain on just the function row, number row, letters, space bar row, arrows and navigation, or number pad).
- **Paint several devices at once.** A switch on the Lights page applies fills to every device on the canvas, and a gradient or rainbow then runs across the whole canvas so devices side by side share one picture.
- **Paint library.** Save the painted LEDs of a device under a name, and use a saved picture on the selected device or on all of them. Rename and delete are there too. The pictures are plain files in your Ember folder that you can copy to another PC.
- **Continuous integration.** The repository builds and tests itself on GitHub for every push, and a release workflow builds the zip and the installer.
- **Signing.** The release build signs Ember's programs and the installer when a code-signing certificate is provided (see docs/signing.md). Until one is, Windows still shows "Unknown publisher".

- **Own lighting for one device.** "Give it its own lighting" (Lights page, device list) makes a copy of the layers just for that device, so a keyboard can show a different look from the Govee lamps and fans. The layer editor then works on that device alone until you press "Back to the whole scene".
- **Switch off a device.** Each device on the Devices page has a "Use" switch. Off, Ember leaves it out of every scene and stops sending to it; the choice is remembered.
- **Arrange devices.** Lamps found later (for example more Govee lights) get room on the canvas instead of overlapping, and a button tidies the canvas into one row per device.

### Fixed

- **Key groups follow the real keys.** The number pad, the arrows and navigation block and the function row now line up with the keys on a Logitech G915 (checked with a colour per column): Print Screen belongs to the navigation block, the Right arrow to it and not the number pad, and Right Ctrl to the bottom row.
- **Switching a device off now works for the motherboard and NZXT too.** Their long OpenRGB ids were being refused when saved.
- **Philips Hue.** Settings has a Philips Hue section: find or type your bridge address, press the bridge button and Link. Each colour light then shows up on the Lights page and follows the scene as one colour, over the bridge's local API (no Hue account, no cloud). Not yet tried on a real bridge.
- **A save at the moment Ember closes can no longer be lost.** A delayed save still writing the lighting file and the closing save now take turns.
- **The update check knew which version it was.** A build id after the version number made Ember read its own version as beta.1, so beta-channel users would have been offered an update they already had.
- **Key highlight.** A layer that lights chosen letter and number keys (W A S D by default) in one colour and leaves the other keys to the layers below, for gaming. Type any letters or numbers to light; the bottom letter row (Z to M) has not been checked against a real keyboard.
- **Logitech keyboard shows per-key effects again.** The mouse's single colour used to overwrite the whole keyboard every frame (G HUB cannot do both at once). The keyboard is now lit key by key and the mouse is left to G HUB; a switch on the Lights page makes every Logitech device take one colour instead.
- **Govee lamps did not change colour when a VPN covered the home network.** Ember found the lamp but sent its colours into the VPN (for example a Tailscale subnet route for 192.168.1.0/24). Commands now go out from the PC's own address on the lamp's network.

### Changed

- What's new in Settings folds each release: the newest is open, older ones open with a click, and long entries show two lines with "Show more".

- The AI chip in the mode list now points to the AI assist panel instead of saying "coming later". It is not a mode the service runs.

## 0.1.0-beta.4 (2026-10-13)

### Added

- **An installer.** `Ember-Setup-<version>.exe` is a normal Windows installer: a welcome page, the Terms of Use to accept, a choice of folder and shortcuts, a progress page with a log, and an Open Ember button at the end. It carries everything Ember needs, including Microsoft's own installers for the .NET 8 Desktop Runtime and the Visual C++ runtime (used by OpenRGB), and only runs them when the PC does not have them. The one thing it downloads is the PawnIO driver, checked against a pinned checksum and signature. It can also run silently for scripted installs.
- **Uninstall from Windows.** Ember now appears under Installed apps, with its version, and removes itself from there. It asks whether to keep your settings.
- **Legal pages.** Terms of Use, Privacy, Third-party notices and the Licence are in Settings, Legal. Ember asks you to accept the Terms the first time it opens if the installer did not already ask, and again if they change.
- **Per-LED painting, further.** Drag over LEDs to paint them like a brush. Fill a device with one colour, a gradient at any angle, or a rainbow; flip a picture left to right; export it to a file and import it again, fitted to the LEDs of another device.
- **Govee segment detection.** A "Detect segments" button asks Govee (through your API key) how many parts a lamp can light separately, and knows the H6056 light bars have 12.
- **More effects.** "Spectrum bars" rise up a keyboard (or become brightness on a strip), "Ripples" spread rings across the canvas or each device, and "Reactive keys" flash the keyboard where you press a key. Reactive keys only notice that a key went down, only while the layer is on, and keep no text.
- A logo and an app icon.

### Changed

- Effects can now be drawn on each device separately, so a keyboard shows the whole picture wherever it sits on the canvas.

## 0.1.0-beta.3 (2026-10-13)

### Added

- **Music effects.** "Music pulse" (one colour that follows the bass, middle, treble or overall sound) and "Music spectrum" (low pitches on the left, high on the right). Ember listens to what the PC is playing, only while a music layer is on, and keeps nothing.
- **Faster screen sync.** The screen is read through DXGI desktop duplication, straight from the graphics card, and works with HDR desktops. If it cannot, Ember falls back to the older method.
- **Per-LED painting.** Select a device on the Lights canvas, switch on "Paint single LEDs" and click LEDs to give them a fixed colour; right-click gives one back to the effects.
- **Per-key Logitech keyboard.** The keyboard is now a grid of 126 keys (21 by 6), so effects run across and down it, not just along it.
- **Govee segments (experimental).** For lamps that accept per-segment colours, choose how many segments to drive from the device list on the Lights page. One segment is the whole lamp, as before.
- Devices with a grid of LEDs are drawn as a grid on the canvas.

### Fixed

- The changelog and roadmap in Settings showed "Nothing to show" in beta 2 because the files were not packed into the app.

## 0.1.0-beta.2 (2026-10-13)

### Added

- **Logitech G lighting.** Keyboards and mice are driven through G HUB's own lighting engine, which Ember loads from your G HUB install (nothing from Logitech is copied or shipped). The keyboard is lit per key column, the mouse as one lamp.
- **Light profiles per device.** Any device on the Lights page can show a different saved scene from the rest, for example a keyboard with its own look while the fans and strips follow the main scene.
- **Changelog and roadmap** in Settings, and as `CHANGELOG.md` and `ROADMAP.md` on GitHub.
- **Devices page** now carries the fan header tests, so the Fans page is only modes and curves.

### Fixed

- The update checker now says plainly that a private repository hides its releases, instead of "no releases yet".

## 0.1.0-beta.1 (2026-10-13)

First beta.

### Added

- **Dashboard** with live CPU, GPU and motherboard temperatures, load and clock lines, a 10-minute history chart and fan speeds.
- **Fan control** with Super quiet, Quiet, Normal, Performance and Extreme curves, and Manual mode with a curve graph and editor for the CPU fan and the system fans.
- **Safety rules** enforced in the service: 90 °C override, early CPU fan ramp, speed floors, blind-sensor rule, and a watchdog service that restores safe speeds if Ember stops responding.
- **Stall recovery.** A fan that stops turning at a low speed is spun up at 100% for a few seconds, and that fan keeps a slightly higher minimum afterwards.
- **Fan headers** are tested and enabled one at a time; headers with no fan attached are hidden; competing fan programs are detected by name and can be closed from the app.
- **Lighting** through OpenRGB (bundled, started on localhost only) and Govee (LAN, or cloud with your own key stored in Windows Credential Manager), with layered effects, blend modes, scenes and screen sync.
- **Devices page** with the state of each integration and every device, plus the fan header tests.
- **Settings** for temperature unit, start page and start with Windows, theme (dark, light or match Windows), accent colour, updates and release channel.
- **Update checker** that only reads the list of GitHub releases and never installs anything by itself.
- **Experimental switch** for game detection and GPU fan control, off by default.
- **Install scripts** to build a release zip and install or upgrade Ember with its two services and a Start Menu shortcut.

### Security

- Pipes are restricted to SYSTEM, administrators and the interactive user; control messages are size-limited, validated and rate-limited.
- The service refuses to install from a folder that normal users can write to.
- Ember uses the signed PawnIO driver, not WinRing0.
