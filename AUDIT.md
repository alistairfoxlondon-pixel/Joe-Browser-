# Joe Browser — audit and fixes (1.2.1)

A full read of the application source with the three reported problems as the starting
point: Google sign-in / sign-up failing, missing icons and UI/UX defects, and "unexpected
error" while deleting a profile. Everything below was found by reading the code; the
repo has no build toolchain in this environment, so each item names the file and line
that had to change and what the new behaviour is.

The three reported faults were all real, and none of them was a rendering glitch:

| Report | Actual cause | Where |
|---|---|---|
| Google sign-in / sign-up does nothing or fails | No OAuth client id is configured, and the only way to add one was a Settings card the first-run window cannot reach. The flow itself also had four separate defects (see §1). | `GoogleSignIn.cs`, `AccountGate.cs`, `MainForm.cs` |
| Icons are not visible | The installer copies **only** `JoeBrowser.exe`; the `fonts` folder never reached an installed copy, so the Material icon face never loaded. | `Installation.cs`, `Theme.cs`, `build/build.ps1` |
| "Unexpected error" when deleting a profile | `ApplyFilter` indexed `_cards` with a stale id from `_picks` → `KeyNotFoundException` → global crash dialog. | `MainForm.cs` |

---

## 1. Google sign-in and sign-up

### 1.1 The blocking defect — no client id, no reachable way to add one

`GoogleSignIn.DefaultClientId` and `DefaultClientSecret` are empty, which is correct: the
OAuth client belongs to a Google Cloud project. But the first-run sign-in window reacted
to that with

> "Add a Google client id in Settings › Account."

…on a window that has no Settings page and no way to get to one (the main window does not
exist until an account does). So for every new user, "Continue with Google" was a dead
button that told them to go somewhere they could not go. That is the reported sign-up
failure.

**Fix.** New `src/GoogleSetup.cs`, a guided setup dialog reachable straight from the
sign-in button:

* explains the three console clicks in order, with a button that opens the console;
* accepts the id in whatever form it was copied — bare, quoted, `"id": "…"`,
  `id: secret`, or a whole block of console text (`GoogleSignIn.NormalizeClientId`);
* pre-fills from the clipboard when a valid id is already there;
* validates it properly (`LooksValid`: numeric project prefix, `-`, client part, suffix);
* saves it and returns to the flow, which then continues without a restart.

`Settings → Account → Set up` now opens the same dialog, so the instructions can never
drift apart again, and the fields are refreshed from what was saved.

### 1.2 `localhost` vs `127.0.0.1` — the sign-in that hung for three minutes

`SignIn` bound the listener to `IPAddress.Loopback` but told Google
`redirect_uri=http://localhost:<port>`. On Windows, `localhost` resolves to `::1` (IPv6)
first; a listener that owns only the IPv4 loopback never sees that request. Chrome shows
"connection refused" and the app sits in `AcceptTcpClient` until the fixed 180-second
timeout. This is the most common *"the Google page opens, I approve, and nothing
happens"* report.

**Fix.** Bind an explicit IPv4 loopback and send Google the matching literal address
(`http://127.0.0.1:<port>`), which a *Desktop app* OAuth client accepts on any port.

### 1.3 The callback accepted the first connection at face value

A single `AcceptTcpClient()` was treated as *the* redirect. Browsers routinely open a
speculative pre-connect, and a favicon request or a local health probe will happily
arrive first — the app then answered "Google did not return an authorization code" and
gave up while the real redirect was still in flight.

**Fix.** The listener now runs a loop until the deadline: any request without `code` or
`error` is answered with a small "waiting for the redirect" page and skipped, the request
head is read until the blank line (a single `Read` can return half of it), `state` is
verified, and the whole exchange is reported with Google's own error code.

### 1.4 Silent and unhelpful failures

`Process.Start(url)` was unguarded, every failure was collapsed into "cancelled", and the
error text was shown in a single 22 px line that clipped most of it.

**Fix.**
* `OpenUrl` is guarded and returns false instead of throwing.
* `GoogleResult` now carries `Code` + a human `Message`; `GoogleSignIn.Describe` maps
  `redirect_uri_mismatch`, `invalid_client`, `access_denied`, `invalid_grant`,
  `org_internal`, `admin_policy_enforced`, `deleted_client`, … to a sentence that says
  what to do next.
* PKCE (RFC 7636) is now implemented — verifier, S256 challenge, `code_verifier` on the
  exchange — so a *Web application* client works too and the secret is not the only thing
  protecting the exchange.
* The token exchange reads Google's JSON error body instead of throwing a bare
  `WebException`; the userinfo call is a normal authenticated GET.
* The account gate shows a "Waiting for Google… finish in your browser" status, disables
  the form while it waits (it used to freeze with no explanation and could not be
  dismissed), and the flow runs on a worker thread so the window keeps painting.
* `SignInGoogle` returning null (which happens when the account file cannot be written)
  is now reported instead of closing the dialog into nothing.

### 1.5 Client id handling in Settings

`SaveSettings` only checked the `.apps.googleusercontent.com` suffix, and `Test` used
`Trim()` only. A paste with a trailing space, quotes, or the `id: secret` pair — all of
which the console invites — reached Google as an invalid client.

**Fix.** Both paths go through `NormalizeClientId` + `LooksValid`; the warning names the
expected shape (`123456789012-abc123.apps.googleusercontent.com`). The Settings row was
also widened: the id is 72+ characters and shared a row with two buttons.

### 1.6 Test Google sign-in

`Test` showed results in three unthemed `MessageBox` calls, including on the async path.
It now uses the themed notice dialog, saves the settings before the flow (as before) and
logs a save failure instead of discarding it.

---

## 2. Icons and UI/UX

### 2.1 Root cause of the missing icons — the installer

`Installation.SelfInstall` copies **one file**:

```csharp
File.Copy(source, target, true);   // JoeBrowser.exe and nothing else
```

`Theme.FontsDir()` looks for a `fonts` folder beside the executable. In `dist\` that
folder exists (the build copies it), so the raw build looks perfect. After installing,
`%LOCALAPPDATA%\Programs\JoeBrowser` contains only the exe, `FontsDir()` returns null,
`_iconFamily` is null, and every icon is drawn from a null font — i.e. **not drawn at
all**. That is exactly the reported symptom, including why it "sometimes" works.

**Fixes, three layers:**

1. **The fonts are now embedded in the executable** (`build/build.ps1` adds every
   `src/fonts/*.ttf|otf` as a `JoeBrowser.fonts.*` resource) and extracted on first run to
   `%APPDATA%\JoeBrowser\fonts` by `Theme.InstallEmbeddedFonts` — written to a temp name
   and moved into place, so a crash can never leave a truncated font behind. An installed
   copy is now self-sufficient.
2. **The installer copies the folder too** when a package has one
   (`Installation.CopyFonts`, `Setup.CopyFonts`), so the portable layout keeps working.
3. **A missing face degrades instead of vanishing**: `Icons.Draw` falls back to the system
   UI font (glyph drawn as the literal character) and `Theme` logs a single clear error
   naming the folder it looked in.

`src/fonts/MaterialIcons-Regular.otf` was deleted: it is not a valid sfnt (`bad
sfntVersion`) and only existed as a stale duplicate of the `.ttf`. It is skipped by the
build as well, so a checkout that still has it cannot embed it.

### 2.2 Font families could be collected while in use

`LoadFace` created a `PrivateFontCollection` in a **local variable** and returned
`.Families[0]`. GDI+ keeps a family alive only while its collection is; once the local
went out of scope the family could be collected and later `Font` construction either
threw or painted nothing. The collections are now held in a static list for the life of
the process (`_fontCollections`).

`LoadFonts()` is also idempotent now (`_fontsLoaded`) and `UnloadFonts()` resets the
caches, so a second call reloads rather than silently reusing stale state.

### 2.3 The window icon

* `AppIcon.Bitmap` deleted the **cached** icon inside a `using` block, so any later call
  painted a dead handle. Fixed.
* `JBForm` now hands Windows the exact 16 px and 32 px frames (`WM_SETICON`), instead of
  letting it resample one bitmap for the taskbar and Alt-Tab.
* `Logo.Draw` gets a compact variant below 20 px: at 16 px the shield's shoulders and
  bottom point turn to mud, so the mark becomes a rounded tile with the letter — the same
  logo, tuned for the size it is actually drawn at.
* `SetupForm` had **no icon at all** — the installer window showed the generic WinForms
  icon while its own executable carried the real one. It now extracts the app icon.
* The Google button now carries a properly drawn four-colour Google mark
  (`BrandIcons.Draw("google", …)`, vectors at 16 px, not a resampled PNG).

### 2.4 First-run and setup windows

* `InstallGate` ran `Installation.SelfInstall` **on the UI thread** with a single
  `Application.DoEvents()` in the middle: the window could not repaint or be moved and
  showed "not responding" for the whole install, and a failure escaped to the generic
  crash dialog. It now does the copy on a worker thread, reports the real reason, shows a
  success line before handing over, and logs failures.
* Its desktop-shortcut option was a raw WinForms `CheckBox` (system font, system chrome)
  on the most-seen window of the product — it is a `JBToggle` now.
* `SetupForm` had **no `AutoScaleMode`** while the manifest declares `PerMonitorV2`, so
  WinForms rescaled on top of the process' own DPI handling; the layout double-scaled on
  125 %/150 % displays and clipped the buttons. It is `AutoScaleMode.None` now, matching
  `JBForm`. `Enter` installs, `Esc` cancels, and the "Close" button actually closes (it
  used to stay disabled while a timer closed the window for you).
* `Setup.Installer.Install` deleted the installed executable **before** writing the new
  one: a read failure or a crash mid-copy left the user with no application at all. It now
  stages the payload next to the target and moves it into place, retrying while Windows
  holds the previous image, and closes a running copy first.

### 2.5 Dialogs and error surfaces

* `ToolDialog` closed on `Esc` even while a text field had focus — one keystroke threw
  away everything already typed. Now `Esc` in a field with content moves focus out and
  relabels the cancel button "Discard and close"; a second `Esc` (or `Esc` anywhere else)
  still closes.
* The crash dialog (`Program.ShowFatal`) was an unthemed `MessageBox` that asked "report
  it on Telegram now?" before the user could read anything. It is now the app's own
  dialog, the details are copied to the clipboard *before* anything is asked, and the log
  location is named. If the themed dialog itself cannot open (a failure before the theme
  is ready) it falls back to `MessageBox` rather than dying silently.
* Every `MessageBox.Show` on a themed window was replaced with `ConfirmForm.Ask` /
  `ConfirmForm.Tell` — 14 sites across `MainForm`, `KernelsUi`, `Program` and `Setup`
  (`Setup` keeps the system dialogs on purpose: they run before any theme exists).
* The unhandled-exception handlers are registered **before** the first window is created.
  The install gate used to run with none at all, so a failure while installing produced
  the raw .NET crash dialog — an "unexpected error" on the very first thing a user does.

### 2.6 Async handlers could take the process down

Fifteen handlers were `async void` with no `try`/`catch`: `CheckAllProxies`,
`CheckProxy`, `BrowserAction`, `AddExtensionByUrl`, `TestGoogleSignIn`, `EditProfile`,
`ClearProfileData`, `RunLeakTest`, `OpenProfile` (`MainForm`), `DetectLocaleAsync`,
`FetchBrowser`, `TestProxy` (`EditorForm`), `LoadAsync`, `Download` (`KernelsUi`),
`RunCheck` (`NetworkForm`). An exception after an `await` goes to the UI-thread handler —
the user lost the window they were in for a network hiccup or a locked file.

They are now `Task`-returning cores driven by a guard (`MainForm.Guard`, new
`src/Guarded.cs` for the dialogs) that logs the failure and reports it inline: a toast in
the main window, a themed notice elsewhere. `DoGoogle` and `InstallGate.DoInstall` keep
`async void` — required for event handlers — but are fully wrapped.

---

## 3. The "unexpected error" on profile delete

```csharp
// MainForm.ApplyFilter, before
for (int i = _picks.Count - 1; i >= 0; i--)
    if (!_cards[_picks[i]].Visible) _picks.RemoveAt(i);   // KeyNotFoundException
```

`_picks` holds the ids of the selected profiles; `_cards` is rebuilt from scratch by
`ReloadCards`. Delete a profile that is part of the selection (or delete one while a bulk
selection exists) and the id stays in `_picks` after its card is gone — the very next
`ApplyFilter` indexes a missing key and the global handler shows *"Joe Browser hit an
unexpected error."*

**Fix.**

* `PrunePicks()` drops selection entries (and an anchor) whose card no longer exists, and
  every path that reads `_picks` calls it first: `ApplyFilter`, `TogglePick`, `SyncPicks`,
  `LayoutBulk`, `ReloadCards`.
* `ApplyFilter` uses `TryGetValue` rather than an indexer, so a stale id can never throw
  even if a future path forgets to prune.
* `DeleteProfile` removes the id from the selection, the anchor and the launch set before
  touching the store, and the delete itself is inside the existing `try` with a toast on
  failure.
* `BulkDelete` no longer rebuilds every card once per deleted profile: a single delete
  removes its card (`RemoveCard`) and the bulk path reloads once at the end.
* `p.Proxy.Enabled` in the confirmation text was a second, latent `NullReferenceException`
  for a profile whose `Proxy` was null; guarded.
* `ProfileStore.Save` now creates a missing `Fp`/`Proxy`/`Tools` instead of throwing a
  `NullReferenceException` on every save of such a profile (which made it permanently
  uneditable).

---

## 4. Everything else that was fixed

**Start-up and installation**

* Single-instance mutex: the installed copy is started while the copy that was just
  installed still owns the mutex, so the new process saw "already running", focused a
  window that was about to close and exited — the install looked like it did nothing. It
  now waits up to 6 s for the previous process to release it (`Program.AcquireInstance`).
* `AppPaths.EnsureAll` did not create `browsers` or `browsers\kernels`, which the file
  dialogs and kernel downloads expect.
* A non-writable data folder is detected at start-up and reported once, with the path,
  instead of the app silently losing profiles and settings on exit.

**Settings persistence**

* `SaveSettings` writes to a temp file and moves it into place, and **throws** on failure
  instead of logging and pretending: the Google setup must not report success and then
  sign in with nothing, and "Settings saved" must not appear over a failed write.
* A corrupt `settings.json` is copied to `settings.json.broken` before defaults are used,
  so a damaged file is not silently overwritten by the next save.
* `OnFormClosing` cannot throw out of the save anymore (that can stop a window from
  closing at all), and the client id/secret are trimmed on load.
* Every caller of `SaveSettings` that cannot handle the failure (theme flip, language,
  sort preference, exit) now catches and logs.

**Logging**

* Rotation **deleted** the day's log file when it passed 4 MB — at the exact moment a user
  hits the same error repeatedly, the evidence vanished. It now rotates to `…log.1`.
* `Log.CurrentFile()` exposes the current file for support.

---

## Files changed

```
build/build.ps1          embed the fonts as resources
src/GoogleSignIn.cs      rewritten: 127.0.0.1, callback loop, PKCE, error codes, guards
src/GoogleSetup.cs       NEW - guided OAuth client setup
src/Guarded.cs           NEW - guarded async for dialogs
src/AccountGate.cs       guided setup, busy state, async guard, two-line status
src/Icons.cs             Google brand mark, font fallback
src/Theme.cs             embedded-font extraction + retry, kept collections, diagnostics
src/AppIcon.cs           fixed disposed cache, exact size selection
src/Logo.cs              16 px compact mark
src/Controls.cs          exact 16/32 px window icons
src/Installation.cs      payload+fonts copy, locked-file retry
src/Setup.cs             staged payload, close running copy, DPI, icon, keyboard
src/InstallGate.cs       install off the UI thread, themed toggle, error reporting
src/MainForm.cs          delete crash, guarded async, themed dialogs, settings errors
src/EditorForm.cs        ConfirmForm.Tell, guarded async
src/KernelsUi.cs         themed errors, busy flag released on every path
src/NetworkForm.cs       guarded async
src/ToolsDialogs.cs      Esc no longer discards typed input
src/ProfileStore.cs      atomic settings write, corrupt backup, null guards
src/AppPaths.cs          missing folders, writability probe
src/Log.cs               real rotation, current-file accessor
src/Program.cs           early crash handlers, mutex wait, themed crash dialog
src/fonts/MaterialIcons-Regular.otf   deleted (invalid font)
README.md                Google setup and font packaging
```

Version bumped to 1.2.1 in `AssemblyInfo.cs`, `Installation.cs`, `Setup.cs`,
`app.manifest` and `Report.cs` so an in-place update is offered and installed.

---

## Verification

The sandbox has no `csc`/`dotnet`/`mono` and WinForms/DPAPI are Windows-only, so this was
a static audit: every changed method was re-read against its callers, all braces and
parentheses were re-checked per file, and every API used by the new code
(`Ui.Round`, `Ui.Measure`, `Log.Info`, `ToolDialog.AddField/AddAction/AddNote`,
`JBTextBox.Value/Box`, `ConfirmForm.Ask/Tell`, `BtnKind`, `Theme.Danger/TextDim`) was
checked against its definition. Sign-in, icon rendering and the delete path all need a
Windows run to be exercised end to end; the things to check on the first build are:

1. `dist\JoeBrowser.exe` → install from the gate → the installed copy still shows icons
   (this is the regression that started it all).
2. Sign-in window → **Continue with Google** with no client id → the guided setup opens,
   saves, and the flow continues into the browser.
3. Select two profiles, delete one → no dialog, the bulk bar drops to one, delete the
   second → the card grid and the empty state are correct.
4. `%APPDATA%\JoeBrowser\Logs` after any of the above: no `ERROR` lines.
