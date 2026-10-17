# Beta testing guide

<sub>[← All docs](README.md)</sub>

Thank you for trying Ember's beta builds. Beta builds hold features that are written and tested in software but not yet proven on real hardware, so what you see and report is what moves them into the production build.

## Before you start

- **Fans come first.** Ember controls real fans. While you test anything that touches fans, keep a temperature monitor open and be ready to switch Ember to **Performance** (the tray menu has every mode). Ember always goes to full speed at 90 °C, but do not rely on that alone.
- **Try one thing at a time.** Test one feature, note what happened, then move on.
- **Nothing leaves your PC by itself.** Ember has no accounts or telemetry. A report only contains what you choose to paste (see PRIVACY.md).

## Getting the beta

1. Download the newest release marked **Pre-release** from the [releases page](https://github.com/Ol775/ember-pc/releases) and run the setup, **or**
2. In Ember, open Settings, Updates, and set the **Release channel** to Beta. Ember offers the newest beta and installs it after you confirm; your settings are kept.

The bottom of the left menu says **Beta build** when you are on one. Moving back to **Stable** is the same switch.

## What to try: the new look and the new features

The beta draws every page again (icon sidebar, ring gauges, rounded cards). Look at each page at a small window size and at your usual one, in Dark, OLED black and Light, and tell us about anything cut off, overlapping or hard to read.

- **Lights:** add an Aurora wave layer, drag its colour handles, and use **Applies to** and **Solo layer**. Rename a device on the Devices page and check the new name shows on the Lights page.
- **Schedule and alerts** (Settings, Schedule and alerts): add a window that covers the current time, then check the fan mode and lights change and are put back when it ends. Send a test alert.
- **Power, Games and Specs:** check the cost estimate and ranges, the covers and "Last played" lines, and that Specs shows your own hardware (Copy specs leaves out the PC name and your user name).
- **Logitech G keys:** on a keyboard with G keys, check they show as a device and the keyboard stays lit.

## What to try: the Stream Deck plugin (beta)

You need an Elgato Stream Deck with its software installed.

1. In Ember, go to **Devices** and find the **Stream Deck** card. Press **Install**, then confirm in the Stream Deck window.
2. In the Stream Deck app, find the **Ember (beta)** group and drag keys onto a page. Pick the scene, fan mode or readout in each key's settings.
3. Try each key and compare with Ember:
   - **Lighting scene:** tap switches to the scene; hold steps to the next one.
   - **Lights on/off** and **Brightness** (hold to keep going; it stops at 0 and 100 percent).
   - **Fan mode** and **Cycle fan mode** (hold goes back one mode). It should never choose Manual or AI.
   - **Open Ember:** brings the window to the front, or starts Ember when it is closed (never a second copy).
   - **Temperature or fan speed:** CPU and GPU temperature and fan speed should match Ember's dashboard.
4. Close Ember: the scene, lights and brightness keys should say "offline" and recover when Ember starts again.
5. Press **Update** or **Remove** on the card when a newer plugin ships; both should work (if Stream Deck holds the files, quit it and try again).

Tick what you checked in [hardware-checks.md](hardware-checks.md) if you are the project owner; otherwise just tell us in your report.

## Reporting what you find

Open an issue on [GitHub](https://github.com/Ol775/ember-pc/issues) and include:

- The Ember version (Settings, About) and whether it is the Beta or Stable channel.
- What you did, what you expected and what happened. A screenshot helps, especially for key faces or the Devices page.
- Your hardware only where it matters (Stream Deck model, motherboard or fan controller for fan problems).
- The diagnostic report: Settings, About, **Copy diagnostic report**, pasted into the issue. Read it first and remove anything you would rather not share.

Please do not paste API keys, passwords or your Hue bridge details.
