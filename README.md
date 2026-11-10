# Joe Browser

**A local first anti detect browser workspace for Windows.**
Unlimited isolated profiles, per profile proxies, a coherent fingerprint for every
profile, and pinned browser builds. Nothing leaves your PC.

**[Download JoeBrowserSetup.exe](../../releases/latest/download/JoeBrowserSetup.exe)**
· **[Portable JoeBrowser.exe](../../releases/latest/download/JoeBrowser.exe)**
· [All releases](../../releases) · [What changed](../../releases/latest)

Windows 10 or 11, 64 bit. The installer is per user: no administrator prompt, nothing
written outside your profile. Any Chromium based browser or Firefox is optional, because
the Browsers page can fetch Chromium for you.

---

## What it does

| | |
|---|---|
| **Unlimited profiles** | Every profile is its own browser with its own cookies, storage, logins and cache, in its own `--user-data-dir`. Free means free: no profile cap and no one-at-a-time limit. |
| **A real identity per profile** | User agent and client hints, platform, language, screen, timezone, memory, cores, WebGL, canvas and audio noise, and a WebRTC guard. Locale, clock and proxy country are kept in agreement. |
| **No two profiles alike** | A new identity is compared against every profile you already have across twelve surfaces and re rolled until it is unique. Every profile also gets its own browser build - a real version from a real release, never the one installed on your PC - and the user agent, the client hints and the version on the card all report it. |
| **Pinned builds** | Download whole Chrome, Brave or Chromium builds once and pin a profile to an exact version, so a vendor update never moves it. |
| **Any browser you have** | Chrome, Chromium, Brave, Edge and Firefox. The Browsers page shows what is installed, with its version and path, and installs what is missing. |
| **Proxies** | HTTP, HTTPS and SOCKS5 with authentication. Credentials are applied by a local relay, so a password never appears in a browser command line. Swap the proxy of a running profile without closing its tabs. |
| **Fingerprint check** | A fifteen point probe inside the profile's own session, plus one click buttons that open the well known checkers in that same session. |
| **History per profile** | Every page a profile visits, including the redirect hops, isolated per account and clearable. |
| **Extensions** | Three first party tools written by this app, two store extensions included on Chrome and Brave, and anything you add from a Chrome Web Store link, a `.crx` or a folder. The included pair show their state, so a download that failed says why instead of vanishing. |
| **Built to be read** | 16 px body text, square corners, one measured layout at every window width, and no control that cuts its own label. |
| **Private by default** | Profile data stays in `%APPDATA%\JoeBrowser`. Proxy passwords and the account store are encrypted with Windows DPAPI. No telemetry, no server, no cloud. |

## What changed in 1.8.1

- The included store pair now loads on **Chrome and Brave only** - not Chromium, not Edge, never Firefox - exactly as specified.
- Every profile creation fetches the missing pair, so a profile made in the first seconds after launch (or after a failed first fetch) no longer starts without them.
- A removal mark now beats a stale library entry: a shipped extension the user removed can no longer ride back onto the command line.
- "Add extension by store link" leaves through the proxy library when one is configured; on proxied machines the bare request it used was what failed.
- Proxy add/edit and store-link dialogs show their validation error **inside the dialog** (the toast used to sit behind the modal and read as a dead button), and the parser accepts a full-width `：` colon.
- An outage where neither the proxy nor a direct route works now says so in the window, once per outage; a relay accept loop that dies is logged instead of going silent.
- The "This PC (direct)" proxy baseline really bypasses proxies now (an empty `WebProxy`, not `null`, which meant the system proxy).
- Tool-script registration failures (human paste, right-click, proxy banner) are logged instead of vanishing.
- The Account page gained a **usage card**: profile slots bar, days the subscription still runs, and the invite code with the days it has earned, one click to copy.

## Getting started

1. Run the installer and create an account. It is verified on this PC only, so there is no
   email to confirm.
2. Press **New profile**. A complete identity is generated for you.
3. Optionally open the **Proxy** tab on that profile. With locale matching on, the clock and
   language follow the proxy's country.
4. Press **Open**. A separate browser window starts with that profile's identity and proxy.
   Closing it keeps the session.
5. The **⋯** menu on a card has edit, duplicate, history, leak check and delete. The card's
   checkbox, or Ctrl click, selects several for a batch action.

## Where your data lives

| | |
|---|---|
| Account store | `%APPDATA%\JoeBrowser\accounts.json` (DPAPI encrypted) |
| Profiles and settings | `%APPDATA%\JoeBrowser\profiles` |
| Browser data | `%APPDATA%\JoeBrowser\data\<profile>` |
| Extensions | `%APPDATA%\JoeBrowser\extensions` |
| Logs | `%APPDATA%\JoeBrowser\logs` |

**Portable mode:** put an empty file called `portable.txt` next to `JoeBrowser.exe` and
everything is stored in a `userdata` folder beside it instead.

## Free, entirely

Joe Browser is free to use. There is no plan, no account tier and no payment anywhere in the
app: unlimited profiles, unlimited open browsers, every tool enabled. The old crypto-payment
and referral system has been removed from the code completely, not switched off.

There is no Google sign-in either. The optional local account is an email and a password
stored encrypted on this PC, and it exists only so profiles and history can be kept together
on one machine.

## How the fingerprint works, and its limits

For Chromium based browsers the app starts your browser with an isolated profile directory
and applies the identity through the DevTools protocol, so it also covers new tabs and
popups. For Firefox it generates a `prefs.js` with the same settings.

This is injection at the JavaScript layer, not a patched browser binary. Checks that compare
the operating system's own network behaviour, such as the TCP stack or WebRTC packets outside
the browser sandbox, can still see the real machine. Against the checks sites actually run in
JavaScript it behaves like a distinct device, especially with a matching proxy. No anti detect
tool can promise a particular detection outcome, and this one does not.

## Windows may show a warning the first time

The binaries are not code signed. A code signing certificate is the only thing that changes
the "unknown publisher" line, and it costs a few hundred dollars a year for an individual
developer. Two practical notes:

- If Windows Defender flags `JoeBrowserSetup.exe`, it is reported as a false positive. It is a
  self contained installer that carries the application inside itself, which is a shape
  malware also uses, and Defender's reputation engine reacts to that shape for any new
  publisher. Download from the Releases page of this repository only, compare the file against
  the attached `SHA256.txt`, and if you prefer, use the portable `JoeBrowser.exe` instead,
  which does not unpack anything.
- Both files carry the same signature as every previous release. Nothing in the app writes to
  Windows startup, registers a service, or asks for administrator rights.

## Building it yourself

The build needs the .NET Framework 4.8 reference assemblies and a C# compiler. There is no
project file and no NuGet restore.

```powershell
powershell -ExecutionPolicy Bypass -File build\build.ps1
powershell -ExecutionPolicy Bypass -File build\setup.ps1
```

`setup.ps1` embeds `dist\JoeBrowser.exe` into `dist\JoeBrowserSetup.exe`, so build the
application first. Both take `-Csc <path>`, which is how CI builds them with the Roslyn
compiler inside the .NET SDK.

Checks:

```powershell
JoeBrowser.exe --selftest        # configuration, uniqueness, account rules, imports
python tools\check-extension-fetch.py   # the store endpoint, from a machine with a network
```

The interface type and the icon face travel **inside** the executable and are extracted on
first run, so an installed copy draws the same icons as a portable one.

Releases are cut from a tag whose name matches the version in `src\AssemblyInfo.cs`;
`.github\workflows\release.yml` builds both binaries on a Windows runner, runs the checks,
and attaches them to the release.

## Licence

Proprietary, all rights reserved. You may download and run the binaries on your own
computers. Copying, forking, redistributing or building a competing product from this source
is not permitted. See [LICENSE](LICENSE).

Use it lawfully and within the terms of the sites you visit.

---

Built by **Joe Goldberg** · Telegram [@joegoldberg2025](https://t.me/joegoldberg2025)
