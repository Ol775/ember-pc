# Privacy

Ember keeps everything on your PC.

- **No telemetry, no accounts, no ads.** Ember does not collect or send usage data.
- **No network calls by the app or service** for monitoring or fan control. The one exception is the update check below.
- **Update check.** Only when you press "Check now", or if you turn on "Check for updates when Ember starts" (off by default), the app asks `api.github.com` for the list of Ember releases. Nothing about your PC is sent; GitHub sees an ordinary request from your IP address, as for any web page. Nothing is downloaded or installed. Sensor data moves over a local named pipe.
- **Lighting.** Govee LAN control talks to lamps on your local network (UDP). Govee cloud control is used only if you enter your own API key; it then calls Govee's servers for your devices. OpenRGB is reached on localhost only.
- **Music effects** listen to the sound your PC is playing (not a microphone) while a music layer is on, only to measure how loud each pitch is. Nothing is recorded, saved or sent.
- **Screen sync** reads a very small, low-resolution copy of your screen in memory to work out colours. Nothing is saved or sent.
- **Stored data.** Fan settings are in `%ProgramData%\Ember`. App preferences and lighting scenes are in `%AppData%\Ember`. Your Govee key is in Windows Credential Manager. Delete these to remove everything.
- **Downloads.** Only the install scripts download anything (PawnIO and OpenRGB, from their official GitHub releases, verified by SHA-256).
