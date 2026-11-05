# Joe Browser

A local-first anti-detect browser workspace for Windows, built by **Joe Goldberg**
(Telegram: [@joegoldberg2025](https://t.me/joegoldberg2025)).

Unlimited profiles, one-click browsing, per-profile proxies, and coherent,
auto-generated browser fingerprints. Everything runs and stays on your own PC.

## Highlights

- **Unlimited profiles** — each one is a separate Chromium browser with its own
  cookies, history, logins and storage, kept in its own data folder.
- **Coherent fingerprints by default** — a full identity is generated locally and
  applied consistently: user agent, platform, languages, screen, timezone,
  device memory/cores, WebGL vendor/renderer, plus canvas & audio noise and a
  WebRTC leak guard. No configuration required — but every part can be customized.
- **Per-profile proxy** — HTTP, HTTPS and SOCKS5 (with authentication). Before a
  profile is saved you can test the connection; a local relay forwards the
  browser's traffic and applies the proxy credentials, so passwords never appear
  in the browser command line. Timezone and language can auto-match the proxy's country.
- **100% local, no cloud, no registration** — profiles live in
  `%APPDATA%\JoeBrowser`, proxy passwords are encrypted with Windows DPAPI.
  Nothing is ever uploaded anywhere.
- **Free and open source** — MIT license. No limits, no accounts, no telemetry.

## Requirements

- Windows 10 or 11 (Windows 7/8 also works, x64).
- .NET Framework 4.8 runtime — preinstalled on updated machines; nothing to install.
- A Chromium-based browser is **optional** — Google Chrome, Microsoft Edge, Brave or
  any Chromium build will be used automatically. If none is installed, Joe Browser
  downloads the official Chromium snapshot itself (in-app, ~180 MB) before the
  first launch, then uses it.
- Proxies are optional.

## Getting started

1. Run `JoeBrowser.exe`.
2. Click **Create profile** — a full fingerprint is generated automatically
   (indentity: OS, screen, timezone, language, GPU…).
3. Optionally open the **Proxy** tab, enter your proxy, and press **Test connection**.
   With "Match locale to proxy" enabled the timezone and language follow the IP.
4. Press **Open** on the card. A private browser window launches with that profile's
   fingerprint and proxy. Close it whenever you like — the profile keeps its state.

> **No browser installed?** When you open a profile and Joe Browser cannot find
> Chrome, Edge, Brave or any Chromium binary, it shows a one-click dialog to
> download the official Chromium browser for Windows (progress bar included), then
> automatically launches the new profile with it. You can also download it manually
> from the **Settings** page (**Download Chromium** button).
5. Right-click (or the ⋯ menu) for Edit / Duplicate / Delete.

Folders:
- Profiles & settings: `%APPDATA%\JoeBrowser\profiles` (each profile is a small JSON file)
- Browser data per profile: `%APPDATA%\JoeBrowser\data\<profile id>`
- Crash diagnostics: `%APPDATA%\JoeBrowser\logs`

**Portable mode:** create an empty `portable.txt` next to `JoeBrowser.exe` and all
data will be stored in a `userdata` folder next to the executable instead.

## Build

From a PowerShell terminal:

```powershell
powershell -ExecutionPolicy Bypass -File build\build.ps1
```

The script compiles the C# sources against the .NET Framework 4.8 reference
assemblies and produces `dist\JoeBrowser.exe` (~190 KB, no runtime dependencies
beyond .NET Framework 4.8, which ships with Windows). A modern C# compiler is
located automatically; install the .NET Framework 4.8 Developer Pack if none is
found, or pass `-Csc <path to csc.exe>`.

`build\make-icon.ps1` regenerates the application icon.

## How spoofing works (and its limits)

Joe Browser launches your installed Chrome/Edge with an isolated user-data dir
and a per-profile configuration (proxy relay, language flag). The chosen
fingerprint is applied through the DevTools protocol: timezone, locale,
Accept-Language and user-agent headers are overridden, and a script injected
before every page overrides navigator/screen/WebGL/canvas/audio APIs.

This injection-based approach handles the checks that real sites (browserl,
fingerprint scanners, e-commerce/KYC flows) perform in JavaScript. It is **not** a
patched Chromium binary, so a small number of deep checks — e.g. those comparing
OS-level network fingerprints (WebRTC UDP source packets, TCP/TLS TCP stack
details) outside the browser sandbox — may still see the real machine.
For the great majority of sites and workflows it behaves like a distinct device,
especially with a matching proxy.

## License

MIT — see [LICENSE](LICENSE). Use responsibly and comply with the law and with
websites' terms of service.
