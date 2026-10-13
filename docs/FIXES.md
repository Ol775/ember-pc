# Fix sheet

Problems found while testing on real hardware, to fix. Move an item to the changelog when it is fixed.

## Open

- [ ] **Logitech mouse lighting sleeps.** With the lights on, the mouse glows only after it is moved, and then only for a
      short while. It should stay lit for as long as the lights are on. Likely the mouse's own idle dimming (G HUB's lighting
      sleep setting, or the device's power saving) rather than Ember's frames; find out whether Ember can keep it awake (for
      example by re-sending the colour on a slower keepalive, or by switching the sleep timer off through G HUB's SDK), and if
      not, say so in the app next to the Logitech status. Seen 2026-10-13 during the first lighting test.

## Fixed

- [x] **Govee H6056 ignored every colour while Tailscale was running.** Tailscale's route for 192.168.1.0/24 took the unicast
      commands; the multicast scan still worked, so the lamp showed as Online. Commands are now sent from the PC's address on
      the lamp's subnet. Confirmed on the PC 2026-10-13 (Fire on all devices).
- [x] **Logitech keyboard flashed one colour instead of showing per-key effects.** G HUB's lighting engine treats the keyboard as
      a whole-device lamp too, so the mouse's single colour (sent every frame) overwrote the per-key picture, and even one such
      write locks the keyboard to that colour until the engine is restarted. Found 2026-10-13 by testing the engine directly.
      Ember now lights the keyboard key by key and leaves the mouse to G HUB; a switch on the Lights page ("Logitech: one colour
      for every Logitech device") gives the old one-colour behaviour for all Logitech devices instead. Windows Dynamic Lighting
      would address them separately, but it only lets the foreground app (or a chosen background app) control them, which does
      not suit a tray app.
- [x] **Devices page "Use" switch did nothing for the motherboard and NZXT.** Their OpenRGB ids are about 115 characters (they contain
      the Windows device path) and the saved list only accepted ids up to 80, so the change was silently rejected. The limit is now 400.
      Found 2026-10-13 and confirmed on the PC (NZXT shows Disabled and is left alone).
