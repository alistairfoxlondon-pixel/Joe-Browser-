# Joe Browser

**A local first anti detect browser workspace for Windows.**
Unlimited isolated profiles, per profile proxies, a coherent fingerprint for every
profile, and pinned browser builds. Nothing leaves your PC.

**[Download JoeBrowserSetup.exe](../../releases/latest/download/JoeBrowserSetup.exe)**
· **[Portable JoeBrowser.exe](../../releases/latest/download/JoeBrowser.exe)**
· [All releases](../../releases) · [What changed](../../releases/latest)

Windows 10 or 11, 64 bit. The installer is per user: no administrator prompt, nothing
written outside your profile. A browser of your own is optional: the Browsers page can
fetch Chromium, Chrome, Brave, Firefox and Edge builds for you and keep them inside
this application.

---

## What it does

| | |
|---|---|
| **Unlimited profiles** | Every profile is its own browser with its own cookies, storage, logins and cache, in its own `--user-data-dir`. Free means free: no profile cap and no one-at-a-time limit. |
| **A real identity per profile** | User agent and client hints, platform, language, screen, timezone, memory, cores, WebGL, canvas and audio noise, and a WebRTC guard. Locale, clock and proxy country are kept in agreement. |
| **No two profiles alike** | A new identity is compared against every profile you already have across twelve surfaces and re rolled until it is unique. Every profile also gets its own browser build - a real version from a real release, never the one installed on your PC - and the user agent, the client hints and the version on the card all report it. |
| **A real browser manager** | The Browsers page manages **Chromium, Chrome, Brave, Firefox and Edge** the way AdsPower and GoLogin do: every published build, every release channel, installed side by side, removable, and pinnable to a profile. |
| **Nothing downloads itself** | A build is fetched the first time something needs it - when you ask for it, or when you open a profile pinned to a build this machine does not have yet. Opening the application never starts a download. |
| **Pinned builds** | Pin a profile to an exact version, so a vendor update never moves it. Chrome reaches back over the last eight majors, so 157, 156, 155 and 154 are all available. |
| **Any browser you have** | Chrome, Chromium, Brave, Edge and Firefox already on the PC are detected with their version and path, and can be used as they are. |
| **Proxies** | HTTP, HTTPS and SOCKS5 with authentication. Credentials are applied by a local relay, so a password never appears in a browser command line. Swap the proxy of a running profile without closing its tabs. |
| **Fingerprint check** | A fifteen point probe inside the profile's own session, plus one click buttons that open the well known checkers in that same session. |
| **History per profile** | Every page a profile visits, including the redirect hops, isolated per account and clearable. |
| **Extensions** | Three first party tools written by this app, two extensions included with every Chromium profile, and anything you add from a Chrome Web Store link, a `.crx` or a folder. The included pair show their state, and removing one keeps it removed. |
| **Built to be read** | 16 px body text, one measured layout at every window width, and no control that cuts its own label. |
| **Private by default** | Profile data stays in `%APPDATA%\JoeBrowser`. Proxy passwords and the account store are encrypted with Windows DPAPI. No telemetry, no server, no cloud. |

## What changed in 1.11.0

**The About page stops shaking.** The page measured its own height while it painted, and
the scroll host put it straight back to the height of the window, so the two fought over
it for as long as the page was open. The height is measured in the paint and applied from
outside it, and the page now sits still.

**The theme change is one step, and it is instant.** The dissolve between a light theme
and a dark one has to pass through the grey in the middle, where the text and the
background are the same colour and the interface reads as broken - and it took long enough
to be felt as a delay. The palette now moves in one step and the whole tree is repainted
as one piece: no frame ever holds two themes, and no frame is unreadable.

**The page title has a row of its own.** On a narrow window the toolbar took the whole
header band and the title was drawn underneath the search field, so the page had no name
at all. The toolbar drops to a second row instead, and the title keeps the first one.

**The Browsers page is shorter, and it lists more.** The rows said what they did not need
to say. "HTTP Tracker Plus" and "Absolute Enable Right Click and Copy" are written by the
application itself rather than fetched from the Chrome Web Store, so they are there on the
first run, behind a proxy that blocks the store, and offline. Chrome now reaches back
twelve majors with four builds of each, Chromium offers eight to forty builds per channel,
and Brave and Edge carry their real catalogues - Brave from its release feed, Edge from
the enterprise download page Microsoft publishes.

**The Account page fills the window.** It used to centre a 980 px column in whatever space
there was, which left a wide empty margin on both sides of every card. "No other accounts
on this PC" is gone: the absence of a list already says it.

**The Extensions page does not overlap.** The button that restores the included pair and
the heading above the recommended cards were laid out on top of each other. Both headings
are lifted above the rows that follow them, and the button has room of its own.

**The included pair can be removed and stays removed.** Removing one writes a mark beside
the library that outlives the folder, survives a re-download, and is what the loader and
the library both consult. The self test used to leave the first of the pair marked as
removed on every machine that ran it - and the release build runs it.

**Proxy rotation is announced by the application.** The rotation notice used to live only
in a banner inside a page, which is not a notification when the user is looking somewhere
else. The window now watches the address the world sees the profile from and says so in
the window and in the notification area. The offline alert was already there.

**Paste as human, the proxy watch and the right-click tool are built in.** They are
written by the application and injected through the browser's own debugging channel, so
they work on every Chromium engine - Edge included - without the user installing
anything.

## What changed in 1.10.1

**The theme change no longer leaves the old theme behind.** A control that keeps its own
surface - a label, a button, a card - held on to the background colour it was built with,
so after a theme change a title sat on a grey stripe of the previous theme and the sign out
button became a black rectangle. Every surface now follows the theme it is painted in.

**The theme change is a dissolve.** Switching between dark and light moves the whole
palette a step at a time and repaints the window as one piece, so no frame ever contains
two themes. Only the top level window used to be frozen during a theme change, which let
every child control repaint on its own - that was the flash.

**The first profile you create appears.** The grid was shown and hidden only when the page
changed, so the count in the header said one and the page stayed empty until you visited
another page and came back.

**The installer's version can no longer drift from the build.** The version was typed into
two files and only one of them had been edited for the release, so the uninstall entry in
Apps and Features still said 1.9.2. Both binaries now read the version out of the
assembly they were built as.

## What changed in 1.10.0

**The Browsers page is a browser manager.** Chromium, Chrome, Brave, Firefox and Edge
each get a tab; every release channel (Stable, Beta, Dev, Canary, ESR, Nightly) gets a
filter; every published build is listed with its size and can be installed, removed and
pinned to a profile. The install progress is shown in the row it started from. A build
that is already installed is never downloaded twice.

**Versions and channels are real.** Chromium builds now carry their real version numbers
and channels - 159.0.8154.0 Canary, 159.0.8144.0 Dev, 158.0.8127.18 Beta and so on - and
Chrome reaches back over the last eight majors, so 157, 156, 155 and 154 are all one
click away. The kernel window opened on the Chromium tab and listed Chrome builds; it
now lists the engine whose tab is selected.

**Nothing downloads itself.** Opening the application fetches no browser. A build is
fetched when it is asked for, and when a profile pinned to a build that is not here yet
is opened: that build is downloaded, unpacked and launched, with the progress on screen.

**The theme change is a dissolve.** Switching between dark and light moves the whole
palette a step at a time and repaints the window as one piece, so no frame ever contains
two themes. The window used to be frozen only at the top level, which let every child
control repaint on its own and produced the flash.

**The interface moves.** Pages arrive from the side instead of appearing, buttons and
cards ease into their hover state, and the app answers a click the way a desktop
application should.

**Less, and better.** The keyboard shortcut wall is gone, tooltips no longer carry
keyboard hints, the engine picker no longer repeats "not installed" next to a line that
already says so, and the first profile you create now appears on the grid instead of
waiting for you to visit another page and come back.

**Faster.** The browser search, the engine detection and the Browsers catalogue all run
in the background, so the window opens without waiting for a disk read or a publisher.

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
