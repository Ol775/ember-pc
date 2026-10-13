# Privacy

Ember keeps everything on your PC.

- **No telemetry, no accounts, no ads.** Ember does not collect or send usage data.
- **No network calls by the app or service** for monitoring or fan control. The one exception is the update check below.
- **Update check.** Only when you press "Check now", or if you turn on "Check for updates when Ember starts" (off by default), the app asks `api.github.com` for the list of Ember releases. Nothing about your PC is sent; GitHub sees an ordinary request from your IP address, as for any web page. Nothing is downloaded or installed. Sensor data moves over a local named pipe.
- **Lighting.** Govee LAN control talks to lamps on your local network (UDP). Govee cloud control is used only if you enter your own API key; it then calls Govee's servers for your devices. OpenRGB is reached on localhost only.
- **Music effects** listen to the sound your PC is playing (not a microphone) while a music layer is on, only to measure how loud each pitch is. Nothing is recorded, saved or sent.
- **Reactive keys** (an effect you add yourself) notices when a key goes down, only while that layer is on, to flash the keyboard lights where the key is. It keeps no text and no history: each press is reduced to a position on the keyboard and forgotten after a few seconds. Nothing is saved or sent.
- **Screen sync** reads a very small, low-resolution copy of your screen in memory to work out colours. Nothing is saved or sent.
- **Stored data.** Fan settings are in `%ProgramData%\Ember`. App preferences and lighting scenes are in `%AppData%\Ember`. Your Govee key is in Windows Credential Manager. Delete these to remove everything.
- **Downloads.** The Ember installer carries everything it needs except the PawnIO driver, which it downloads from the publisher's official GitHub release and checks against a pinned SHA-256 and signature before running it. Only the build scripts download OpenRGB and the Microsoft runtimes, for the people who make a release. A setup that is run without internet access simply skips the PawnIO step and says so.
- **Your agreement.** Accepting the Terms of Use is recorded on your PC only (a registry value, and a setting in your app preferences).
