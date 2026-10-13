# Ember Terms of Use

Version 1, 13 October 2026

These terms are between you and Ol775 ("the author") and cover the Ember software ("Ember"): the app, the Ember service and the Ember watchdog service. By installing or using Ember you agree to them. If you do not agree, do not install or use Ember, and uninstall it if it is already on your PC.

They are written to be read. The short version: Ember is free, it controls real hardware on your PC, it comes without a warranty, and you are responsible for how you use it.

## 1. What Ember does

Ember reads the sensors of your PC, controls fan speeds, and controls the lighting of devices such as RGB lights, keyboards and mice. To do this it installs two Windows services that run with high privilege (as the Local System account), uses a signed hardware driver (PawnIO) to reach your motherboard, and talks to other programs and devices on your PC and network (OpenRGB, Logitech G HUB, Govee lamps).

## 2. Licence

Ember's source code is published under the MIT Licence (see the LICENSE file). That licence lets you use, copy, modify and share the code. These terms add the rules about running the compiled program and about the risks of controlling hardware, and they do not take away any right the MIT Licence gives you over the code.

The name "Ember" and its logo may be used to refer to this software. Please do not present a modified version as the original.

## 3. You control real hardware: your responsibility

Fans keep your PC from overheating. Ember changes fan speeds on the instructions you give it (a mode, a curve, a manual speed). A wrong setting, a header Ember cannot control properly, a motherboard or firmware that behaves differently from what Ember expects, or a bug can make a PC run hotter or louder than you want, become unstable, lose unsaved work, or in the worst case be damaged.

Ember has safety measures: it forces fans to full speed at 90 °C, restores automatic control when it stops, runs a watchdog, and asks you to test each fan header before using it. These measures reduce the risk. They are not a guarantee, and they do not replace your own care. In particular:

- Check temperatures yourself after you change fan settings, and after every update.
- Do not rely on Ember where a failure could cause harm to people or serious loss. It is not designed or tested for that.
- Features marked Experimental or Beta may not work, and may behave in unexpected ways. Turn them on only if you accept that.
- Lighting effects can flash. If flashing light bothers you, keep effects gentle or turn them off.
- Do not run Ember on a PC that is not yours without the owner's permission.

## 4. No warranty

Ember is provided "as is" and "as available", without any warranty of any kind, express or implied, including any warranty of fitness for a particular purpose, merchantability, accuracy or non-infringement, to the extent the law allows. The author does not promise that Ember will work with your hardware, be free of errors, or run without interruption.

## 5. Limit of liability

To the extent the law allows, the author is not liable for any loss or damage that comes from installing or using Ember, including damage to hardware, loss of data, loss of profit, or any indirect or consequential loss, even if the author was told it could happen. If the law does not allow a limit to apply, the author's total liability is limited to the smallest amount the law permits (Ember is free of charge).

Nothing in these terms limits liability that cannot lawfully be limited, such as liability for fraud, or for death or personal injury caused by negligence, or any right you have as a consumer that cannot be waived.

## 6. Other people's software, services and trademarks

Ember works with software and services made by others. Each has its own licence or terms, which apply to your use of it. Ember does not change them, and the author is not responsible for them.

- OpenRGB (GPL-2.0) is included unmodified and run as a separate program.
- PawnIO is a driver published by its author. The Ember installer downloads it from its official release page and checks its checksum and signature before running it. Ember does not redistribute it.
- LibreHardwareMonitor (MPL-2.0), NAudio and Vortice (MIT) are libraries used inside Ember.
- The Microsoft .NET runtime and the Microsoft Visual C++ runtime are installed from Microsoft's own installers under Microsoft's licence terms, shown by those installers.
- Logitech G HUB is not part of Ember. If you have it, Ember asks it to light your Logitech keyboard and mouse.
- Govee lamps and the Govee cloud are used only if you set them up, under your own Govee account and Govee's terms.

All product names, logos and brands are the property of their owners. Ember is not made, endorsed or supported by Logitech, Govee, Corsair, Razer, NZXT, MSI, AMD, NVIDIA, Microsoft or any other company named in it.

The full list, with licences, is under Third-party notices in Settings, Legal, and in the file THIRD-PARTY.md.

## 7. Your data

Ember keeps its settings on your PC and sends nothing to the author. It contacts the internet only to check GitHub for new releases (you can switch this off), to download the PawnIO driver during installation, and to talk to Govee's cloud if you saved a Govee key. The Privacy notice says exactly what is kept and sent, and what the music and key-reactive lighting effects look at.

## 8. Changes to your PC

Installing Ember creates program files, two Windows services, a Start Menu entry (and optionally a desktop shortcut), registry entries and one Windows Firewall rule (it lets Govee lamps on your own network answer on UDP port 4002, on private networks only), and installs the PawnIO driver and, if they are missing, the Microsoft runtimes it needs. Uninstalling Ember from Windows Settings, Installed apps, removes the program, the services and the shortcuts. It leaves your settings and the PawnIO driver and Microsoft runtimes in place, because other software may use them.

## 9. Updates and changes to these terms

Ember can tell you when a new version is available. It never installs an update by itself. If these terms change in a way that matters, Ember asks you to accept the new version before you carry on using it.

## 10. Ending these terms

You can stop at any time by uninstalling Ember. The author can stop publishing or supporting Ember at any time. Sections 3, 4, 5 and 6 carry on after these terms end.

## 11. Contact and problems

Report bugs and ask questions at https://github.com/Ol775/ember-pc/issues. Report security problems as described in SECURITY.md.

## 12. General

These terms, the MIT Licence and the Privacy notice are the whole agreement about Ember. If a part of them cannot be enforced, the rest stays in force. Laws that protect you where you live, and that cannot be waived, still apply to you.
