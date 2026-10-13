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
