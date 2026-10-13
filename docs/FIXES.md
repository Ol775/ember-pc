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

## Notes from testing

- The tray icon could not be opened on this PC until Explorer was restarted (two Explorer sessions were running, the second belonging to
  a disconnected Plex user). Not an Ember fault, but worth a line in a troubleshooting page.
- [x] **Update check thought Ember was 0.1.0-beta.1.** The version string a build reports ends in a long commit hash (`+4560d21…`), and the parser rejected anything over 40 characters, so the app fell back to beta.1 and would have offered beta-channel users the version they already had. Found 2026-10-13 while testing the update check against the public release.
- [x] **Screen sync followed nothing on an HDR desktop.** DXGI desktop duplication also hands out frames with no picture update (pointer or metadata only, `AccumulatedFrames == 0`), whose texture is empty on an HDR desktop; Ember read them as a black screen. Found 2026-10-13 with the owner's HDR monitor; confirmed with two HDR videos.

## Open (found while testing)

- [ ] **GPU fan control does not hold a speed on an RTX 3060.** With Experimental and "GPU fan control" on, the header reads Controllable and the fan responds to a mode change (0 to 66% in Performance), but at Super quiet it never settles at the case-curve speed (about 38%, never below the 30% floor): it runs the card's own zero-RPM cycle instead, ramping 66% down to a stop and back up about every 40 seconds. Likely the card's firmware overriding manual fan values near its zero-RPM threshold, or Ember's write not being applied between ramps. Needs a look at what is written and read back each cycle, and perhaps a hold-off while the card is below its own minimum. Until then the option should stay Experimental and say so.
