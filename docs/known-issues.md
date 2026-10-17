# Known issues

<sub>[← All docs](README.md)</sub>

What is still open, and tips for problems that are not Ember's fault. Fixed problems are in [CHANGELOG.md](../CHANGELOG.md); the engineering notes behind some of them are kept at the bottom.

## Open

- [ ] **Logitech mouse lighting sleeps.** With the lights on, the mouse glows only after it is moved, and then only for a
      short while. It should stay lit for as long as the lights are on. Likely the mouse's own idle dimming (G HUB's lighting
      sleep setting, or the device's power saving) rather than Ember's frames; find out whether Ember can keep it awake (for
      example by re-sending the colour on a slower keepalive, or by switching the sleep timer off through G HUB's SDK), and if
      not, say so in the app next to the Logitech status. Seen 2026-10-13 during the first lighting test.
- [ ] **Logitech G915 listed twice.** OpenRGB can also list a keyboard that Ember already drives through G HUB, so it appears as a second device. Switch the extra one off on the Devices page.
- [ ] **The new look is beta only.** The production build still has the classic window.

## Troubleshooting

- **The tray icon will not open.** Seen once when two Explorer sessions were running (the second belonged to a disconnected user). Restarting Explorer fixed it. Not an Ember fault.

<details>
<summary><strong>Fixed (engineering notes)</strong></summary>

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

- [x] **Update check thought Ember was 0.1.0-beta.1.** The version string a build reports ends in a long commit hash (`+4560d21…`), and the parser rejected anything over 40 characters, so the app fell back to beta.1 and would have offered beta-channel users the version they already had. Found 2026-10-13 while testing the update check against the public release.
- [x] **Screen sync followed nothing on an HDR desktop.** DXGI desktop duplication also hands out frames with no picture update (pointer or metadata only, `AccumulatedFrames == 0`), whose texture is empty on an HDR desktop; Ember read them as a black screen. Found 2026-10-13 with the owner's HDR monitor; confirmed with two HDR videos.

- [x] **GPU fan control on an RTX 3060: wrong at low speeds, and it could not be tested.** Found 2026-10-13, in three parts.
      (1) Measured against the card with LibreHardwareMonitor: the fan holds a steady speed only from about 60%; at 40 to 50% it stops and
      starts, and at 35% it does not turn. Ember's 30% floor sat inside that zone and its stall recovery then kicked the fan to full speed in
      a loop. Now a graphics card fan is driven only at 60% or more, and given back to the card's own control (which stops the fan quietly at
      idle) while the curve asks for less than 45% (back under it at 55%, always driven above 70 C or when the temperature is unknown); stall
      kicks are skipped for it.
      (2) Switching GPU control off left the card in manual mode at a fixed speed until the PC restarted; the library restore is now used for
      graphics cards (it is only skipped for motherboard headers).
      (3) The header test lowered the speed by 20 points and expected the RPM to drop, which does not suit a card that is stopped at idle; for
      graphics cards it now commands 80%, checks the RPM rises, and hands the fan back.
      Checked on the PC: Super quiet leaves the fan at the card's silent 0%, Extreme holds it steady at the curve (86%, about 2,150 RPM), and
      switching control off returns it to the card.

</details>
