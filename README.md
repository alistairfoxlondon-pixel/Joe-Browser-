# Joe Browser 1.1.0

A local-first anti-detect browser workspace for Windows, built by **Joe Goldberg**
(Telegram: [@joegoldberg2025](https://t.me/joegoldberg2025)).

Unlimited profiles, one-click browsing, per-profile proxies, coherent
auto-generated fingerprints, and an original built-in extension system.
Everything runs and stays on your own PC.

## Highlights

- **Unlimited profiles** — every profile is an isolated browser with its own cookies,
  history, logins and storage in its own `--user-data-dir`.
- **Coherent fingerprints by default** — a full identity is generated locally and
  applied consistently: user agent and client hints, platform, language and
  Accept-Language, screen, timezone, device memory/cores, WebGL vendor/renderer,
  fonts, canvas & audio noise, and a WebRTC leak guard. Locale, timezone, proxy
  country and user agent are kept in agreement, and each profile gets a unique
  noise seed. No configuration is required — but every part can be customized.
- **Browser choice** — Google Chrome, Chromium, Brave, Microsoft Edge and Firefox.
  Chrome/Chromium/Brave/Edge are auto-detected; if none is installed, Joe Browser can
  download an official Chromium snapshot in-app.
- **Per-profile proxy** — HTTP, HTTPS and SOCKS5 with authentication. A local relay
  forwards the browser's traffic and applies the credentials, so proxy passwords
  never appear in the browser command line. Timezone and language can auto-match
  the proxy's country.
- **Original self-made extensions** — three unpacked Manifest V3 extensions are
  generated locally by Joe Browser itself. No store downloads, no third-party code.
- **Competitor-grade profile management** — groups/folders, tags, account status,
  notes, search, sorting, multi-select, batch create, recycle bin, profile and
  cookie import/export, batch proxy testing, and cache clearing that keeps logins.
- **Proper language picker** — circular vector flags and full language names, with
  search and keyboard support. Raw codes such as `en`/`de`/`ru` are never shown.
- **100% local, no cloud, no registration** — profile data lives in
  `%APPDATA%\JoeBrowser`, proxy passwords are encrypted with Windows DPAPI, and
  there is no telemetry.
- **Free and open source** — MIT license. No limits, no accounts.

## Built-in extensions (written by Joe Browser)

The extension files are generated from source strings inside the application at
startup, under `%APPDATA%\JoeBrowser\extensions`. Each has a scoped MV3 manifest,
a service worker and a content script. Toggle them per profile on the editor's
**Tools** tab, or set the defaults in **Settings → Profile tools**.

| Extension | What it does |
| --- | --- |
| **Joe Human Paste** | Types clipboard text one character at a time with natural, configurable per-key delays (38–135 ms by default). Useful wherever pasted text is treated as suspicious. |
| **Joe Proxy Watch** | Watches the public IP through the browser's own network path. Shows a sticky in-page banner and a notification when the proxy rotates, goes offline, or comes back — helping to prevent account bans caused by a changed IP. Check interval is configurable (every minute by default). |
| **Joe Context Guard** | Disables the right-click context menu, with an optional "hold Shift to restore the menu" escape hatch so normal browsing stays usable. |

Chrome 137+ can ignore `--load-extension` on branded builds. Joe Browser therefore
tries the documented command-line flags *and* injects an equivalent first-party
runtime via CDP into every document, including new tabs and popups. Either path
working is enough for the features to be usable. You can also add your own
unpacked extension folders from the Tools tab — one absolute path per line.

## Tools & data

- Batch create profiles (with optional per-profile proxy list)
- Batch proxy test
- Cookie import / export
- Profile import / export
- Recycle bin with restore and permanent delete
- Clear cache without losing cookies or logins
- Open the built-in extensions folder

## Requirements

- Windows 10 or 11 (Windows 7/8 also works, x64).
- .NET Framework 4.8 runtime — preinstalled on updated machines.
- A Chromium-based browser is **optional**; Joe Browser can download Chromium itself.
- Firefox is optional and only needs to be installed to be used.
- Proxies are optional.

## Getting started

1. Run `JoeBrowser.exe`.
2. Click **New profile** — a full fingerprint is generated automatically.
3. Optionally use the **Proxy** tab and **Test connection**. With locale matching
   enabled, timezone and language follow the IP.
4. Press **Open** on the card. A private browser window launches with that
   profile's fingerprint and proxy. Close it whenever you like — state is kept.
5. Use the **⋯** menu on a card for Edit / Duplicate / Delete, or open the
   **Tools & data** page for bulk operations.

Folders:

- Profiles & settings: `%APPDATA%\JoeBrowser\profiles` (small JSON files)
- Browser data per profile: `%APPDATA%\JoeBrowser\data\<profile id>`
- Built-in extensions: `%APPDATA%\JoeBrowser\extensions`
- Exports / recycle bin: `%APPDATA%\JoeBrowser\exports`, `...\trash`
- Crash diagnostics: `%APPDATA%\JoeBrowser\logs`

**Portable mode:** create an empty `portable.txt` next to `JoeBrowser.exe` and all
data will be stored in a `userdata` folder next to the executable instead.

## Build

From a PowerShell terminal:

```powershell
powershell -ExecutionPolicy Bypass -File build\build.ps1
```

The script compiles all `src/*.cs` files against the .NET Framework 4.8 reference
assemblies and produces `dist\JoeBrowser.exe` (~638 KB, no runtime dependencies
beyond .NET Framework 4.8, which ships with Windows). A modern C# compiler is
located automatically; install the .NET Framework 4.8 Developer Pack if none is
found, or pass `-Csc <path to csc.exe>`. New `.cs` files are picked up
automatically. `build\make-icon.ps1` regenerates the application icon.

## How spoofing works (and its limits)

For Chromium-based browsers, Joe Browser launches your installed browser with an
isolated user-data dir and a per-profile configuration (local proxy relay,
language flag, extension folders). The fingerprint is applied through the
DevTools protocol at the browser target level so it also covers new tabs and
popups: timezone, locale, Accept-Language, user-agent headers and client hints
are overridden, and a script injected before every page overrides
navigator/screen/WebGL/canvas/audio APIs.

Firefox does not expose the same CDP surface, so it is launched with `-no-remote`,
`-new-instance` and a generated `prefs.js` that sets proxy, locale, language,
user agent, WebRTC policy, startup page and telemetry preferences. WebGL,
canvas and timezone spoofing are more limited on Firefox.

This injection-based approach handles the checks real sites perform in
JavaScript. It is **not** a patched browser binary, so deep checks that compare
OS-level network fingerprints (WebRTC UDP source packets, TCP/TLS stack details)
outside the browser sandbox may still see the real machine. For the great
majority of sites it behaves like a distinct device, especially with a matching
proxy. No anti-detect tool can guarantee a specific detection outcome.

## License

MIT — see [LICENSE](LICENSE). Use responsibly and comply with the law and with
websites' terms of service.
