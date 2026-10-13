# Privacy

Ember keeps everything on your PC.

- **No telemetry, no accounts, no ads.** Ember does not collect or send usage data.
- **No network calls by the app or service** for monitoring or fan control. Sensor data moves over a local named pipe.
- **Lighting.** Govee LAN control talks to lamps on your local network (UDP). Govee cloud control is used only if you enter your own API key; it then calls Govee's servers for your devices. OpenRGB is reached on localhost only.
- **Screen sync** reads a very small, low-resolution copy of your screen in memory to work out colours. Nothing is saved or sent.
- **Stored data.** Fan settings are in `%ProgramData%\Ember`. App preferences and lighting scenes are in `%AppData%\Ember`. Your Govee key is in Windows Credential Manager. Delete these to remove everything.
- **Downloads.** Only the install scripts download anything (PawnIO and OpenRGB, from their official GitHub releases, verified by SHA-256).
