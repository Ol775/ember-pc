# Privacy

Ember keeps everything on your PC. It has no telemetry, no accounts of its own and no ads, and it sells or shares nothing.

## What leaves your PC

Only what you switch on, and only to the places named here.

- **Update check.** When you press "Check now", and automatically at start and every few hours unless you turn off "Check for updates automatically" in Settings, Updates (it is on by default), the app asks `api.github.com` (or, when GitHub limits that, the public releases feed on `github.com`) for the list of Ember releases. Nothing about your PC is sent; GitHub sees an ordinary request from your IP address, as for any web page. When a newer version is found Ember asks whether to update, and nothing is downloaded unless you choose Update now. That fetches the release's installer and its checksum from `github.com`, checks the file against the checksum and starts it.
- **Govee.** LAN control talks to lamps on your local network (UDP). Govee cloud control is used only if you enter your own API key; it then calls Govee's servers for your devices.
- **Philips Hue.** Ember talks to your Hue bridge directly over your home network (HTTPS to the bridge, with its certificate remembered when you link it). "Find bridge" is the one call that leaves your network: it asks Signify's discovery service (`discovery.meethue.com`) which bridges share your public address, and only when you press it. You can type the bridge address instead.
- **Meross.** You sign in once so Ember can list your devices and learn the key they check commands against. Your email and a hash of your password (MD5, the way the Meross app sends it) go to Meross's account servers for that, and your password is never saved. Only the sign-in Meross hands back is kept. After that Ember talks to your bulbs and plugs directly on your home network.
- **Tapo (TP-Link).** You save your Tapo email and password in Windows Credential Manager. Ember uses them only to sign in to your Tapo devices on your home network, directly; they are never sent to TP-Link or any cloud.
- **Smart plugs** (Meross or Tapo) are only read, to show the Power page what they draw. Ember never switches a plug.
- **On this PC only:** OpenRGB (localhost), Logitech G HUB, Corsair iCUE, Razer Synapse (its local Chroma SDK) and the Stream Deck plugin (a local named pipe) are reached on this PC, and nothing is sent to their makers by Ember.
- **Nanoleaf.** Ember talks to your Nanoleaf controller directly over your home network (plain HTTP on port 16021, as the controller offers). Finding it sends a search on your local network only. Nothing goes to Nanoleaf or any cloud.

## What Ember reads on your PC

- **Sensors.** The Ember service reads temperatures, fan speeds, loads and power from your hardware, and writes fan speeds. Sensor data moves over a local named pipe.
- **Game libraries.** The Games page reads the install records of Steam, Epic Games, GOG, Ubisoft Connect and the EA app (and looks inside the game folders for program names) to know which programs are games. It only reads; nothing is changed or sent.
- **Game detection** (off until you switch on Experimental features) looks at which program is in the foreground and whether it fills the screen, to switch the fan mode. It keeps only the program names you mark as "not a game".
- **Music effects** listen to the sound your PC is playing (not a microphone) while a music layer is on, only to measure how loud each pitch is. Nothing is recorded, saved or sent.
- **Reactive keys** (an effect you add yourself) notices when a key goes down, only while that layer is on, to flash the keyboard lights where the key is. It keeps no text and no history: each press is reduced to a position on the keyboard and forgotten after a few seconds.
- **Screen sync** reads a very small, low-resolution copy of your screen in memory to work out colours. Nothing is saved or sent.
- **AI assist** (off until you switch on "Learn from my temperatures") keeps a count of how often your CPU and case were at each whole degree, and when it first and last looked, in `%AppData%\Ember\thermal.json`. It never records what you were doing. "Forget what it learned" deletes it.

## What Ember stores

- `%ProgramData%\Ember`: fan settings (writable only by administrators).
- `%AppData%\Ember`: preferences, lighting scenes, the saved paint library (`paints`), the game library from the last scan (`games.json`), fan modes you give single games (`game-profiles.json`), which smart plugs you linked (`power-plugs.json`), the Nanoleaf address (`nanoleaf.json`) and the diagnostic log.
- **Windows Credential Manager:** your Govee key, the Hue key, the Nanoleaf token, the Meross sign-in and your Tapo account. They are never kept in a settings file.
- Delete these folders and entries to remove everything.

## Diagnostic log

Ember keeps a plain text log of what it did and why a device or integration failed (start-up, integration status changes, errors, update steps) in `%AppData%\Ember\logs`, one file per day, deleted after 7 days. It is never sent anywhere: "Copy diagnostic report" in Settings, About puts it on your clipboard only when you press it, so you can paste it into a problem report if you choose. Keys, tokens and passwords are blanked before anything is written, and the log holds no colours, key presses or screen content. The report does list device names.

## Downloads

The Ember installer carries everything it needs except the PawnIO driver, which it downloads from the publisher's official GitHub release and checks against a pinned SHA-256 and signature before running it. Only the build scripts download OpenRGB, the Corsair SDK and the Microsoft runtimes, for the people who make a release. A setup that is run without internet access simply skips the PawnIO step and says so.

## Your agreement

Accepting the Terms of Use is recorded on your PC only (a registry value, and a setting in your app preferences).
