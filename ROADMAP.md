# Roadmap

Where Ember is and where it is going. Done work is folded away at the bottom. See [CHANGELOG.md](CHANGELOG.md) for what changed in each version. Plans can change.

## Being checked on real hardware

- WiZ, LIFX, Yeelight, Kasa and Shelly (new, marked Beta): written from the makers' documented local protocols and tested against simulated devices only; no one has tried them on real hardware yet
- Nanoleaf, Razer Chroma, Meross and Tapo (marked Beta in every build): written and unit-tested, but not yet tried on enough real devices
- Game detection (Beta, in the production build behind Experimental features) and GPU fan control (beta builds only): working on the test PC, to be tried on other PCs and graphics cards before they leave Experimental
- Scheduled fan modes and scenes, and Windows alerts for a hot part or a stopped fan (beta builds only)
- The new look, gradient layers, game covers and last played, and renaming devices: tried in a simulation and partly on the test PC, not yet on other PCs
- Logitech G keys and badge (beta.3): the keyboard and G keys staying lit together after updating, and the badge on its own
- Stream Deck plugin (beta builds only): hold actions, the readout keys, and Update and Remove from the Devices page
- Per-key keyboard layouts and Govee segments on more keyboards and lamps
- A clean install on a PC that never had Ember

## Next

- Take Nanoleaf, Razer Chroma, Meross, Tapo and game detection out of Beta, and bring GPU fan control into the production build, once the hardware checks above are done
- Razer devices: only show the ones that are plugged in, once Synapse offers a way to tell (waiting on Razer)
- Windows Dynamic Lighting (the built-in Windows lighting standard) as another source of devices: waiting on Windows, which lets only the foreground app (or an app Windows lists for background control) drive the lamps, and a tray app is neither

## Later (ideas, no promise)

- More fan sources, such as other fan controllers, if their makers publish a way to talk to them
- Code signing, so Windows stops warning about an unknown publisher and Smart App Control stops blocking Ember

## Works through OpenRGB

ASUS, MSI, Gigabyte, ASRock, HyperX, SteelSeries and many other brands show up in Ember when OpenRGB supports them and their own software (iCUE, Armoury Crate and so on) is closed. They need no extra code in Ember.

## Not planned

- Overclocking or voltage control
- Cloud accounts or telemetry

## Done

### Monitoring and fans

- Live sensor dashboard with history
- Fan modes: Super quiet, Quiet, Normal, Performance, Extreme, and Manual curves with an editor
- Safety rules, header testing, conflict detection, watchdog service, stall recovery for fans that stop at low speed
- AI assist: learns your temperatures and noise preference to suggest a curve you approve
- Experimental game detection and GPU fan control, made to suit a graphics card
- A Power page: CPU, GPU, motherboard and the rest, and other plugs, with a wall plug (Meross, Tapo, Kasa or Shelly, read only) for the whole PC, a choice of currency, and a CSV export of the daily figures
- The redesigned look in every build, and a tray tooltip with the temperatures and the draw
- A Games page that scans Steam, Epic, GOG, Ubisoft Connect and the EA app, with a fan mode per game (off by default)
- A schedule for fan modes and lights (for example quiet fans and dim lights at night), and Windows notifications for a hot part or a stopped fan (beta)
- A Specs page with S.M.A.R.T. drive health, a Power cost estimate with a daily and monthly split, and a last played date and cover for each game (beta)

### Lighting

- Lighting through OpenRGB and Govee (LAN and cloud), layered effects, scenes, screen sync, music-reactive effects
- Logitech G keyboards and mice through G HUB, per key, with the programmable G keys and the LIGHTSPEED badge as a device of their own
- Corsair devices through iCUE (keep iCUE up to date)
- A Stream Deck plugin (beta): keys for scenes, lights, brightness, fan mode and live temperature and fan readouts
- Philips Hue lights through the bridge, Nanoleaf light panels through the controller, Razer Chroma through Synapse, Meross and Tapo bulbs on the local network
- Scenes and manual fan curves as shareable files, and a compact always-on-top window
- A lighting scene per game next to the fan mode per game, global hotkeys (Ctrl+Alt+Shift with L, S and F), Nanoleaf panels laid out as they sit on the wall, the G keys device only on keyboards that have G keys, and no second listing of a keyboard G HUB already drives
- WiZ, LIFX, Yeelight and Kasa bulbs, found on the local network with no account (beta)
- Per-LED painting, gradients, rainbow, import and export, a paint library, and paint for several devices at once
- Effects: spectrum bars, ripples, reactive keys, key highlight, fire, rain, and an effect per key group
- Own lighting for one device, switching a device off, and arranging devices on the canvas
- Govee segments and segment detection, and Govee over a VPN

### The app

- Themes: dark, OLED black, light and match Windows, with colour schemes, accent colours and highlight strength
- Tray icon, keep running in the tray, and start minimised
- Update checker with Stable and Beta channels, and Update now with a checksum
- Changelog, roadmap, terms, privacy and third-party notices inside the app

### The project

- An installer with terms, runtimes included, upgrade and uninstall from Windows
- Continuous integration builds and a release workflow
- The first production release, 1.0.0, with beta builds continuing alongside it
- The repository is public, with a safety audit and cleaned history
