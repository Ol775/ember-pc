# Roadmap

Where Ember is and where it is going. Done work is folded away at the bottom. See [CHANGELOG.md](CHANGELOG.md) for what changed in each version. Plans can change.

## Being checked on real hardware

- Philips Hue, Nanoleaf, Razer Chroma and Corsair iCUE: written and unit-tested, but not yet tried on real devices
- AI assist: needs a few days of real use to see that its suggested curves are sensible
- Game detection and GPU fan control: working on the test PC, to be tried on other graphics cards before they leave Experimental
- Per-key keyboard layouts and Govee segments on more keyboards and lamps
- A clean install on a PC that never had Ember

## Next

- Bring game detection, GPU fan control, Hue, Nanoleaf, Razer and Corsair into the production build once the hardware checks above are done
- Nanoleaf panels placed on the Lights canvas where they really are, not in one row
- Razer devices: only show the ones that are plugged in, once Synapse offers a way to tell

## Later (ideas, no promise)

- Share and import fan curves and lighting scenes as files
- More fan sources, such as other fan controllers, if their makers publish a way to talk to them
- A compact dashboard window for a second monitor
- Stream Deck integration: buttons to switch lights on and off, pick a scene, change the fan mode (Super quiet to Extreme) and show the current temperature. Needs a way for the Stream Deck software to call Ember; to be designed
- Code signing, so Windows SmartScreen and Smart App Control stop warning or blocking: free through SignPath Foundation (open source), about $10 a month through Azure Trusted Signing, or a certificate at roughly $220 to $620 a year. Until then Ember is released unsigned

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

### Lighting

- Lighting through OpenRGB and Govee (LAN and cloud), layered effects, scenes, screen sync, music-reactive effects
- Logitech G keyboards and mice through G HUB, per key
- Philips Hue lights through the bridge, Nanoleaf light panels through the controller, Razer Chroma through Synapse
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
