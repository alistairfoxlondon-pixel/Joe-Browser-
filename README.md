# Joe Browser 1.3.0

> **Joe Browser** — a free, local-first **anti-detect browser** for Windows.
> Unlimited multi-profile browser management with per-profile proxies,
> automatic browser-fingerprint spoofing, kernel pinning, and leak testing.
> No cloud, no accounts, no telemetry — everything runs on your own PC.

## ⬇️ Download Joe Browser (EXE, Windows)

**No build, no cloning, no coding needed.** Download the ready-to-run EXE file
and start using Joe Browser right away:

| Download | Use |
|---|---|
| **[⬇ Download JoeBrowserSetup.exe](../../releases/latest/download/JoeBrowserSetup.exe)** (recommended) | Full installer — installs Joe Browser on your PC |
| **[⬇ Download JoeBrowser.exe](../../releases/latest/download/JoeBrowser.exe)** | Portable — run it directly, no installation |

Requirements: Windows 10/11 · 64-bit · any installed Chromium-based browser or
Firefox (the Browsers page can install Chrome, Brave, Edge, Chromium for you).

> To get the latest version at any time, go to the
> [Releases page](../../releases) and download `JoeBrowserSetup.exe`.

## 🚫 No copying, no forking, no clones

Joe Browser is **proprietary software, all rights reserved**. You may use the
downloaded EXE freely, but you may **not** copy, fork, redistribute, rebrand,
or use this source code to build your own anti-detect browser. See
[LICENSE](LICENSE). Anyone who finds a clone of Joe Browser — it is not
official, the only official distribution is this repository.

A local-first anti-detect browser workspace for Windows, built by
**Joe Goldberg** (Telegram: [@joegoldberg2025](https://t.me/joegoldberg2025)).

Unlimited profiles, one-click browsing, per-profile proxies, coherent
auto-generated fingerprints, and an original built-in extension system.
Everything runs and stays on your own PC.

## Highlights

- **Unlimited profiles** — every profile is an isolated browser with its own cookies,
  history, logins and storage in its own `--user-data-dir`. The free plan holds 3
  profiles and runs one at a time; Pro removes both caps.
- **Coherent fingerprints by default** — a full identity is generated locally and
  applied consistently: user agent and client hints, platform, language and
  Accept-Language, screen, timezone, device memory/cores, WebGL vendor/renderer,
  fonts, canvas & audio noise, and a WebRTC leak guard. Locale, timezone, proxy
  country and user agent are kept in agreement, and each profile gets a unique
  noise seed. No configuration is required — but every part can be customized.
- **Browser choice** — Google Chrome, Chromium, Brave, Microsoft Edge and Firefox.
  The **Browsers** page shows what is installed on this PC, with the version and the
  exact file, and installs the missing ones from the vendor's official download
  without leaving the app (Chromium as a portable build inside the app folder, the
  others through their own signed installer).
- **Pinned kernels, like AdsPower** — download whole browser builds and keep them
  side by side under `%APPDATA%\JoeBrowser\browsers\kernels`, then pin a profile to
  an exact version so a vendor update never moves it. Chrome (Stable/Beta/Dev/Canary),
  Brave and Chromium snapshots are fetched from the publishers' own endpoints; each
  build is a portable folder, nothing is registered with Windows.
- **Fingerprint check inside the session** — a 15-point probe (webdriver, driver
  artefacts, `window.chrome`, plugins, UA, platform, cores, memory, timezone, screen,
  DPR, touch, languages, WebGL vendor/renderer, canvas, WebRTC candidates) run in the
  profile's own tab, with one-click buttons that open browserscan.net and
  ipfighter.com in that same session.
- **Network diagnostics** — exit IP, country, city, timezone, provider, DNS resolver,
  latency and clock skew for this PC or any saved proxy, cross-checked against three
  lookup services; a disagreement or a resolver in another country is called out.
- **Clear data per profile** — cache, cookies, site data or everything, either on a
  closed profile (on disk) or in a running one (through the DevTools protocol), with
  the size shown before anything is deleted.
- **Report a problem** — one box, one button: **Tools & data → Report a problem**
  (also in Settings, in the fingerprint-check dialog, and on the crash dialog) opens
  Telegram on a chat with the developer with your message pre-typed, while the app
  version, plan, detected browser, kernels and the last log lines are copied to the
  clipboard for you to paste. Nothing is sent anywhere by itself.
- **Small-disk safety net** — when the drive holding the profile store falls below
  1.5 GB, the app quietly clears the HTTP/GPU/code caches of closed profiles (never
  cookies or logins), on startup, after a profile closes, and when a launch fails
  because the disk is full.
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
- **Local account gate** — sign up with an email address and a password (no
  verification email, no server round-trip), or continue with Google. Optional
  two-factor authentication through any authenticator app (QR code) and an
  optional PIN lock for shared machines. Everything is verified on this PC.
- **100% local, no cloud** — profile data lives in `%APPDATA%\JoeBrowser`, proxy
  passwords and the account store are encrypted with Windows DPAPI, and there is
  no telemetry and no server.
- **$1 a month, paid in crypto** — no card, no account, no checkout in the middle.
  The app reads the chain itself (see below).
- **Free to download and use** — grab the EXE from the Releases page. The free plan is a real product, not a trial.

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

## Kernels (pinned browser builds)

A profile normally runs whatever version of Chrome, Brave or Chromium is installed,
which means a vendor update silently changes the fingerprint. A **kernel** fixes that:
the build is downloaded once into its own folder and the profile always opens that
exact version.

- Open **Browsers → Kernels** (or the **Kernels** button next to the engine picker in
  a profile's General tab) and pick an engine: Chrome, Brave or Chromium.
- Every build on offer shows its version, channel and size, and whether it is already
  on disk. Download and Remove are the only two actions.
- Pin one per profile under **General → Engine build**. If the build is later removed,
  the profile falls back to the installed browser instead of refusing to open.

Sources are the publishers' own endpoints: Chrome for Testing
(`last-known-good-versions-with-downloads.json`), Brave's GitHub releases, and the
Chromium snapshot archive. All three publish a plain Windows zip, so a build is
unpacked into `%APPDATA%\JoeBrowser\browsers\kernels\<engine>\<version>` and never
touches the registry or the user's own browser.

**Firefox is deliberately not offered.** Mozilla ships installers only for Windows, and
the MSI carries its payload as an embedded 7z that Windows Installer expands only
during a real install — an administrative install copies the MSI alone. Supporting it
would mean registering a second Firefox with Windows (which can replace the user's
own) or bundling a 7z decoder. Firefox profiles keep using the installed Firefox,
which is also what AdsPower does. A pinned build also disables the Chromium sandbox
in an elevated session, because a copy under `AppData` cannot start one there.

## Tools & data

- Batch create profiles (with optional per-profile proxy list)
- Batch proxy test
- Cookie import / export
- Profile import / export
- Recycle bin with restore and permanent delete
- Clear cache without losing cookies or logins
- Fingerprint check in the profile's own session (browserscan.net / ipfighter.com)
- Network diagnostics for this PC or any saved proxy
- Open the built-in extensions folder

## Requirements

- Windows 10 or 11 (Windows 7/8 also works, x64).
- .NET Framework 4.8 runtime — preinstalled on updated machines.
- A Chromium-based browser is **optional**; Joe Browser can download Chromium itself.
- Firefox is optional and only needs to be installed to be used.
- Proxies are optional.

## Accounts & sign-in

The first launch asks for an account. It is created and verified on this PC only —
nothing is sent anywhere and no confirmation email is involved.

- **Email + password** — 6 characters minimum. Stored as a PBKDF2-SHA1 hash
  (100,000 iterations, per-account salt), never in plain text.
- **Continue with Google** — a real OAuth 2.0 loopback flow with PKCE, opened in
  your default browser. Google never sees a password from this app and the app
  never sees one from Google. The OAuth client id belongs to a Google Cloud
  project, so it is the one value the binary cannot invent.

  **Signing in for the first time:** press **Continue with Google**. If no client
  id is configured yet, Joe Browser opens the guided setup, which explains the
  three console clicks, opens the console for you, accepts whatever form the id
  was copied in (bare, quoted, or `id: secret`), and saves it. No separate trip to
  Settings is needed.

  Or create it yourself (about a minute, once):

  1. Open <https://console.cloud.google.com/apis/credentials> and create a project
     (any name).
  2. **OAuth consent screen** → *External* → add your own address as a **test user**.
  3. **Create credentials** → **OAuth client ID** → application type **Desktop app**.
  4. Copy the **client ID** (ends with `.apps.googleusercontent.com`) and the
     **client secret**, then paste both into **Settings → Account & security**
     (or into the guided setup).

  No redirect URI has to be registered: a *Desktop app* client accepts
  `http://127.0.0.1` on any port. The app binds IPv4 loopback explicitly and sends
  Google the matching literal address, because `localhost` resolves to `::1`
  first on Windows and a `127.0.0.1`-only listener never saw the redirect — that
  was the single most common cause of a sign-in that hung until it timed out.

  Press **Test** next to the field — it runs the real flow and reports exactly
  what Google answered (`redirect_uri_mismatch`, `invalid_client`,
  `access_denied`, `org_internal`, …) instead of failing silently.
- **Two-factor authentication** — turn it on in **Settings → Account & security**.
  Scan the QR code with any authenticator app (or type the key by hand) and
  confirm a 6-digit code. Standard RFC 6238 TOTP, SHA-1, 30-second steps.
- **PIN lock** — an optional short code asked for on every launch, on top of the
  remembered sign-in. Useful when several people share the machine.
- **Stay signed in** — after a successful sign-in the session is remembered for
  this Windows user through a DPAPI-protected marker, so normal use is a
  double-click. **Sign out** in Settings clears it.

## Plan and payment

The free plan holds **3 profiles** and runs **one browser at a time**. Everything
else in the app is available on it. **$1 a month** removes both caps.

Payment is a plain transfer of crypto to a fixed address. There is no third-party
checkout, no API key and no server: the app reads the chain itself.

- **Coins** — USDC, USDT and DAI on BNB Chain (BEP20), USDC on Polygon, USDT on
  Tron (TRC20), USDC/USDT/SOL on Solana, BNB on BNB Chain, and Litecoin. Every coin
  and network is shown with its official mark.
- **Network first, then coin** — the same USDT exists on three chains, so the network
  is a separate, explicit choice and the coin list is rebuilt from it. Sending on the
  wrong chain destroys the funds; the app never guesses which one you meant. The chain
  you picked is restated in an amber warning strip under the grid, because that is the
  one decision on the page that cannot be undone.
- **Numbered steps** — the page is a three-step flow (network, coin, payment) with a
  numbered header on each step, so the order is visible rather than implied.
- **Terms before the first payment** — a short, concrete agreement covering the
  network, the locked amount, how confirmation works and that crypto is final. It is
  stored with a version, so changed wording asks again.
- **Live rate, locked for the session** — CoinGecko is read once a minute, the exact
  amount is fixed when the session opens, and a 30-minute timer runs on the page. The
  QR code is a wallet URI (`EIP-681`, `Solana Pay`, `tron:`, `litecoin:`). The address
  is set in a tabular face: `0` and `O` must not look alike when money is being typed.
- **Confirmed automatically** — while the page is open the chain is polled every
  8 seconds; **I have paid** asks immediately. Confirmation means: a matching
  transfer to the address, at least the invoiced amount (1 % tolerance for rounding
  and fees), with enough confirmations (BSC 3, Polygon 2, Solana finalised, Tron and
  Litecoin 1). Only then is Pro activated.
- **A payment is never lost or reused** — the block time must be later than the
  session start, so a transfer from last week cannot unlock anything; every redeemed
  transaction id is stored in the licence file, so one hash can never pay twice; and
  the open session is remembered, so a payment made just before closing the app is
  still confirmed on the next start instead of being rejected as too old.
- **30-day subscription timer** — the plan card is a status card: an icon chip, the
  plan name, an `ACTIVE` / `FREE` pill, the days left and the end date, and a countdown
  bar showing how much of the current 30-day period remains. It turns red in the last
  five days.
- **Referrals, verified on-chain** — every account has a code. A friend enters it
  before paying; after their payment confirms they get a short receipt
  (`JBR1.CODE.CHAIN.TXID`) to send you, and your copy of the app looks that
  transaction up before adding 7 days. A receipt that names a transaction which did
  not pay the publisher's address is refused, and one transaction can only be
  claimed once.

The licence lives in `%APPDATA%\JoeBrowser\license.dat`, DPAPI-encrypted for the
current Windows user. Pro is therefore tied to this Windows account; reinstalling
on the same account restores it. That is the honest limit of a local app — someone
who controls the machine can always patch a binary — but it stops casual editing
and it stops a payment being spent twice.

## Getting started

1. Run `JoeBrowser.exe` and create an account (or continue with Google).
2. Click **New profile** — a full fingerprint is generated automatically.
3. Optionally use the **Proxy** tab and **Test connection**. With locale matching
   enabled, timezone and language follow the IP.
4. Press **Open** on the card. A private browser window launches with that
   profile's fingerprint and proxy. Close it whenever you like — state is kept.
5. Use the **⋯** menu on a card for Edit / Duplicate / Delete, or open the
   **Tools & data** page for bulk operations.

Folders:

- Account store: `%APPDATA%\JoeBrowser\accounts.json` (DPAPI-encrypted)
- Profiles & settings: `%APPDATA%\JoeBrowser\profiles` (small JSON files)
- Browser data per profile: `%APPDATA%\JoeBrowser\data\<profile id>`
- Built-in extensions: `%APPDATA%\JoeBrowser\extensions`
- Exports / recycle bin: `%APPDATA%\JoeBrowser\exports`, `...\trash`
- Crash diagnostics: `%APPDATA%\JoeBrowser\logs`

**Portable mode:** create an empty `portable.txt` next to `JoeBrowser.exe` and all
data will be stored in a `userdata` folder next to the executable instead.

## Build

Releases are built by CI: push a tag such as `v1.3.0` and
`.github/workflows/release.yml` builds both binaries on a Windows runner, runs the
checks below, and attaches `JoeBrowser.exe`, `JoeBrowserSetup.exe` and `SHA256.txt` to
the release. The tag has to match the version in `src/AssemblyInfo.cs`, because every
other version in the build is derived from that file - a mismatch fails the build
rather than shipping two files that disagree. A push to `master` runs the same build
and tests but publishes nothing.

To build by hand, from a PowerShell terminal:

```powershell
powershell -ExecutionPolicy Bypass -File build\build.ps1
powershell -ExecutionPolicy Bypass -File build\setup.ps1
```

Both scripts take `-Csc <path to a C# compiler>`, so either can be built with the
Roslyn compiler that ships inside the .NET SDK - which is how CI builds them on a
runner with no Developer Pack. `setup.ps1` embeds
`dist\JoeBrowser.exe` and its `.config` into `dist\JoeBrowserSetup.exe`, so build the
application first. The installer writes both files, because .NET reads
`JoeBrowser.exe.config` from beside the executable and an installed copy without it
lost its DPI handling.

Before a release, CI checks that both binaries carry the expected file version and
start (`JoeBrowser.exe --selftest` reads its own configuration,
`JoeBrowserSetup.exe --selftest` reads its embedded payload), that the shipped XML is
well formed, and - on a tag - that the installer really installs: it is installed
silently, the installed copy is run, the Apps and Features entry and Start menu
shortcut are verified, and uninstalling is required to remove all of it.

The script compiles all `src/*.cs` files against the .NET Framework 4.8 reference
assemblies and produces `dist\JoeBrowser.exe` (~4 MB, most of it embedded artwork and
type faces; no runtime dependencies beyond .NET Framework 4.8, which ships with
Windows). A compiler is located automatically; install the .NET Framework 4.8
Developer Pack if none is found, or pass `-Csc` an explicit path - the csc.dll inside
the .NET SDK works, but it ships no `csc.rsp`, so the reference assemblies have to be
named on the command line. New `.cs` files are picked up automatically. `build\make-icon.ps1` regenerates the application icon.

The UI type and the Material icon face are **embedded in the executable** and
extracted to `%APPDATA%\JoeBrowser\fonts` on first run. That matters because the
installer ships a single file: an installed copy used to have no `fonts` folder
beside it and therefore drew no icons at all, while the same build run straight
from `dist\` (which does have the folder) looked perfect. If the icon face cannot
be loaded for any reason, `Theme` logs it and `Icons.Draw` paints a neutral
placeholder instead of an invisible gap.

`build\audit.ps1` builds the test harness, which compiles the same sources:

```powershell
tools\audit\audit.exe --ui            # layout overflow on every page, two window sizes
tools\audit\audit.exe --ui --shot     # ...and one PNG per page, painted off-screen
tools\audit\audit.exe --ui --shot --dark  # the same pages through the dark palette
tools\audit\audit.exe --glyphs glyphs.png  # every glyph, both faces, every size in use
tools\audit\audit.exe --mainform      # account creation and the main window
tools\audit\audit.exe --pay           # live rates, invoice maths, wallet URIs, replay guard,
                                      #   and one real chain round-trip per coin
tools\audit\audit.exe --pay --detect  # takes a transfer that really happened on-chain and
                                      #   requires the gateway to report it
tools\audit\audit.exe --pay --entitlements  # free caps hold, Pro is genuinely unlimited
tools\audit\audit.exe --dialogs       # renders the kernel, network and fingerprint dialogs
tools\audit\audit.exe --ui --shot --editor  # every tab of the profile editor, desktop + Android
tools\audit\audit.exe --kernels --catalog   # every publisher answers with a usable build
tools\audit\audit.exe --kernels --install chrome  # downloads a build, unpacks it, launches a
                                      #   profile pinned to it and reads the version back
                                      #   out of the page
tools\audit\audit.exe --sites         # opens browserscan, ipfighter, pixelscan, iphey and
                                      #   whoer in one live session, reads each verdict back,
                                      #   screenshots every page and checks WebRTC for leaks
                                      #   (--raw runs the same pages with the spoofing off)
```

`--ui --shot` renders each page through the window's own paint path rather than
grabbing the desktop, so it works on a locked or headless session. It also loads the
bundled type and icon faces, which the harness does not get from `Program.Main`;
without that the audit would measure a fallback font and every icon would silently
disappear from the screenshot. `--dark` flips the palette after the window is built
and re-runs the same restyle pass the in-app switch uses, so the dark screenshots go
through the real code path.

`--glyphs` writes a contact sheet of every icon at 16, 18, 20, 22, 24 and 28 px, on
both the solid and the outlined face. Material ships two faces and they do not scale
alike: at 16-25 px the solid face collapses multi-part glyphs (layers, copy, gift,
crown, terminal) into a filled blob, while the outlined face keeps the structure.
`Icons.Draw` therefore uses the outline up to 25 px and the solid face above it, and
keeps the solid face for the handful of single-stroke glyphs (check, plus, play,
close, chevrons) at the very smallest sizes, where an outline thins to a grey hairline.
The sheet is the evidence for that rule; regenerate it before changing it.

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


## License & Copyright

Joe Browser is proprietary software - **all rights reserved** by Joe Goldberg.
You may download and use the EXE freely on your own computers, but copying,
forking, redistributing or creating derivative/competing products from this
source code is **not permitted**. See [LICENSE](LICENSE). Use responsibly and
comply with the law and with websites' terms of service.

## Keywords

Joe Browser download, download Joe Browser exe, Joe Browser free download,
anti-detect browser Windows, antidetect browser free, multi-profile browser,
browser fingerprint spoofing, fingerprint switcher, per-profile proxy,
SOCKS5 proxy browser, cookie per profile, Chrome profile manager, Chromium
portable, kernel pinning, WebRTC leak guard, account farming browser,
AdsPower alternative, GoLogin alternative, Multilogin alternative,
Dolphin Anty alternative, JoeBrowser.exe, JoeBrowserSetup.exe.
