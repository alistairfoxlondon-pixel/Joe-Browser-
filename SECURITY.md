# Reporting, and the Windows warning

## Windows Defender reports the installer

It is a false positive. `JoeBrowserSetup.exe` is a self contained installer: it carries a copy
of `JoeBrowser.exe` inside itself as an embedded resource, writes it into the folder you
choose, and creates shortcuts. That is the same shape a malware dropper uses, and Defender's
reputation engine scores an unsigned binary by its shape and its publisher history rather
than by what it does. A new publisher with no download history gets flagged on the strength
of that shape alone.

What the installer actually touches, in full:

| | |
|---|---|
| Files written | The folder you choose, plus a Start Menu shortcut |
| Registry | One `Uninstall` key under `HKCU`, for Add or remove programs |
| Network | Nothing |
| Elevation | None. The manifest requests `asInvoker`; it runs as you |
| Startup | Nothing is registered. There is no service, no scheduled task, no Run key |

If you would rather not run the installer, use `JoeBrowser.exe` instead. It is the whole
application as one file, it writes nothing outside `%APPDATA%\JoeBrowser`, and it carries one
fewer heuristic than the installer does.

Verify what you downloaded against the `SHA256.txt` attached to the same release:

```powershell
Get-FileHash .\JoeBrowserSetup.exe -Algorithm SHA256
```

## Why the binaries are not signed

A code signing certificate is the only thing that removes the warning, and an individual
developer pays several hundred dollars a year for one. Until that is in place, the release
notes and this file say plainly what the warning means and how to check the file. Signing
changes nothing about the application, only the publisher line Windows prints.

## Reporting a problem

Open an issue with the version from **Settings**, what you did, and what happened. The app
writes a log to `%APPDATA%\JoeBrowser\logs`; **Report a problem** in Settings copies the
relevant part to the clipboard for you. Do not attach a profile folder: it contains your
cookies and your proxy credentials.
