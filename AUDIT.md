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

---

# Second pass — whole-app UI/UX audit

The first pass fixed what was outright broken. This pass walked every window, page and
control looking for the things that make an app feel unfinished: layers in the wrong
order, affordances that only exist for the mouse, focus that cannot be seen, text that
is clipped, state that lies, and gestures that silently do nothing.

## Findings and fixes

### Layout and painting

| Symptom | Cause | Fix |
|---|---|---|
| Toasts appeared on Profiles and on no other page | `SetPage` raised the page panel **after** the toast, so the toast sat under an invisible-but-opaque page | `Restack()` re-raises toast → min/max/close after every page change, every toast and every bulk-bar show |
| The "no profiles" panel could sit on top of a card after a filter emptied and refilled the list | z-order was set before the grid was measured, and `_host` was raised only when `shown > 0` | `ApplyFilter` sets visibility and z-order first, then lays out, then restacks |
| Bulk-action bar covered by later-raised surfaces | it raised itself but nothing above it | `LayoutBulk` calls `Restack()` |
| Tools scrollbar drifted a pixel per notch and could leave the page | the bar was drawn inside the scroll transform with the offset added back | drawn after `ResetTransform`, sized from the viewport |
| Multiline field hints ran past the field and over the neighbouring control | the hint label was unclipped and had no ellipsis | `AutoEllipsis`, first line only, re-synced on resize |
| Editor inline error hid its last characters under Cancel | the label width was measured from the panel edge, not from the text start | width derived from `_cancel.Left` and the glyph inset |
| Toggle's off-track hairline over a filled accent track read as a dirty edge | a 1px `TextDim` stroke drawn in both states | the stroke is drawn only in the off state |
| `JBScrollHost.OnPaint` had two statements on one line | copy/paste damage | reformatted |

### Keyboard and focus

| Symptom | Cause | Fix |
|---|---|---|
| No focus ring anywhere on some machines | every control used `Focused && ShowFocusCues`; Windows reports `ShowFocusCues == false` in tablet posture, with a touch keyboard, and in some remote sessions | new `Ui.FocusCues()` + `Ui.KeyboardCuesForced`, set by `Ui.NoteKey` from `JBForm.ProcessCmdKey` / `MainForm.ProcessCmdKey`, cleared on mouse-down — the Windows "focus visible" rule, independent of that flag |
| `JBSegment` had no focus indicator at all | never drew one | ring around the whole strip |
| Focus ring on the Primary button was invisible | it was drawn in the text colour over a filled accent surface | outer 1.5px ring in a blend of accent and text |
| Window caption buttons drew a focus ring inside a 32px hit area | wrong place for a cue | removed |
| Links announced focus only through the OS | no painted state | text colour + underline change |
| Page Up/Down/Home/End did nothing in any scroll page | they are command keys, so `OnKeyDown` never ran; and `JBScrollHost` was not `Selectable`, so it could not take focus | `ProcessCmdKey` on the host handles all six plus Ctrl+Home/End; the host is selectable, takes focus on click |
| The wheel did nothing over cards, empty states and chips | WinForms does not bubble `MouseWheel`; the message goes to whatever is under the pointer and is dropped | `JBScrollHost.WndProc` claims `WM_MOUSEWHEEL` for any descendant that did not handle it |
| Tools page was completely unreachable by keyboard | hand-painted tiles, `TabStop = false`, no key handling | the page is a tab stop with an arrow-key cursor, Home/End, Enter/Space, and auto-scroll to the focused tile |
| Extension rows, recommended cards, coin/network chips were mouse-only | `Control` with no `Selectable`/`TabStop` and no key handling | selectable, Enter/Space activate, focus ring drawn |
| `JBSelect` ignored Home/End and F4 | not input keys / not handled | Home/End move the selection, F4 and Space toggle the popup |
| A 47-item country list had to be scrolled by hand | no type-ahead | letters jump to the next matching label |
| Tab order in the sign-in form skipped the password box | the display-name field was created last but sits between them | explicit `TabIndex` in visual order; the hidden field self-collapses in sign-in mode |
| Switching an editor tab left focus on the old page | nothing focused the new page | the first field of each tab takes focus |
| Esc on the sign-in gate quit the app | `KeyPreview` dialog-key default | Esc clears the focused credential field instead |

### State, data safety and honesty

| Symptom | Cause | Fix |
|---|---|---|
| One Esc discarded five tabs of profile edits | `ProcessCmdKey` closed the editor outright | first Esc asks, second Esc (within 4s) discards |
| A typed port like `4758q` reported "must be between 1 and 65535" and left the caret nowhere useful | validation lost the input | the message quotes the value, and the field is focused and selected |
| Re-opening a `JBSelect` popup disposed the strip that was still on screen | `ShowMenu` disposed unconditionally | the open popup owns the gesture and closes first; the `Closed` handler ignores replaced menus |
| Toast vanished while the user was reading it | fixed 3.4s/6.5s timer, no hover state | the countdown pauses while hovered and resumes shortly after the pointer leaves |
| A search with no results looked like the profiles had been deleted | the empty state offered only "Create profile" | it now offers a primary "Clear search", and the secondary button keeps creating |
| Search could only be undone with Esc or by clearing the box | no visible control | `ClearSearch()` is shared by Esc, the empty state and the filter path |
| Payment page needed the amount and address retyped | only "Copy address" existed | "Copy amount" added; the address text itself is clickable; "I have paid" drops to its own row when space is tight |
| `NetworkForm` could throw if a slow probe finished after the window closed | continuation used the disposed form | `IsDisposed`/`IsHandleCreated` guard after the await |
| Language popup looked broken when a search matched nothing | blank list | explicit "No language matches …" row |
| Esc in the language popup left focus wherever Windows put it | no focus restore | focus returns to the picker, and dismissing no longer re-enters the popup |

## Deliberately not changed

* `JBScrollHost` is **not** a tab stop. A focusable scroll container costs every field inside
  it an extra Tab, and the page-level keyboard paths above already cover scrolling.
* Hand-painted decorative surfaces (`PromoCard`, `StepHeader`, `NoteBar`, `LeakRow`,
  `KernelRow`, `EngineRow`) stay out of the tab order: they are labels or already contain a
  real focusable button.
* `ProfileCard` keeps its own `ShowFocusCues`-independent ring via `Ui.FocusCues`, but its
  Tab behaviour (a card grid, not a form) is unchanged.

## Verification of this pass

* Every edited method re-read against its callers; `Ui` helpers (`S`, `Blend`, `Measure`,
  `Clip`, `FocusRing`, `FocusCues`, `NoteKey`) confirmed to live in `Theme.cs`.
* Per-file brace/paren/bracket balance check over all of `src/*.cs`: clean.
* Icon-name audit: every literal passed to `Icons.Draw`/`Icon =` exists in the glyph table
  (`google` and `telegram` are `BrandIcons`, by design).
* Still requires a Windows run: the keyboard walk (Tab through every page, arrows in the
  tools grid, F4/Space in a select), hovering a toast for 10s, and the wheel over a card
  grid and over an empty state.

---

# Third pass — the surfaces behind the buttons

The second pass fixed the visible chrome. This pass followed each action to what it actually
does, because the worst UX defect is a screen that confidently states something false.

## Truthfulness

| Symptom | Cause | Fix |
|---|---|---|
| The Browsers page offered **Download** for a browser already installed | `BrowserLocator.FindAll` dropped any executable whose version resource could not be read, and `BrowsersPage.Rescan` turned a thrown detection into an empty list | unreadable version keeps the entry; a failed probe marks the engine *unknown* ("Could not check — re-scan") instead of *missing*, and the row's button re-scans |
| "Not installed" for an engine whose detection never ran | `Installed` collapsed "absent" and "unknown" into one bool | `EngineInfo.Checked` added; the status line and the action both follow it |
| The Start page accepted `https://` with no host and saved it | validation only prefixed a scheme | the URL is parsed and rejected at the point it was typed, and the field then shows what was stored |
| A failed download left a progress window that ignored its own X | the cancel token was only wired to the Esc key | closing the window cancels too, and every close on the success path is guarded |
| Cancelling a download could crash on the second close | `dlg.Close()` on an already-disposed form inside an awaited continuation | `CloseQuietly` |
| The network check could throw if the probe outlived the window | continuation used the disposed form | disposed/handle guard after the await |
| Pressing **Close** at the end of setup re-ran the whole install | the Close handler was added on top of the Install handler, which was never removed | one handler, a `_done` flag; and the install now runs off the UI thread so setup never says "not responding" |

## Keyboard and dialog behaviour

| Symptom | Cause | Fix |
|---|---|---|
| No dialog could be completed from the keyboard | `JBTextBox` swallows Enter by design, so `AcceptButton` never fired | `ToolDialog` and `SetupForm` submit from Enter in a single-line field; `ConfirmForm` too |
| A destructive confirmation was armed the instant it appeared | focus/`Select()` sat on the danger button, and KeyPreview let the opening Enter or Space land on it | the safe button takes focus, and Enter on a danger prompt answers Cancel unless the danger button was deliberately focused |
| Esc on a dialog threw away everything typed | "has text" was treated as "was edited" | the dialog records the values it opened with; Esc reverts the focused field, a second Esc closes |
| Two copies of the same commit from one gesture | Enter could reach both the command-key pass and KeyPreview; a double-click on Save ran the validator twice | single-submit guard in `ToolDialog` |
| The setup window ignored the keyboard | plain `Form` with no accept/cancel wiring | `AcceptButton`/`CancelButton` |
| The install gate could only be dismissed with the mouse | no default button | Enter installs, Esc still leaves |
| `Shift+F10` did nothing on a profile card | only the Apps key was handled | `MainForm` routes it to the card menu |
| Focus landed on the wrong field after switching an editor tab, and the caret was set twice on open | hard-coded focus calls in the constructor | one owner: `SwitchTab`/`OnLoad` focus the first field of the page that is actually visible |
| The scrollbar sat on top of the last 17 px of every field in a scrolling dialog | `AutoScroll` was not accounted for in the row width | width subtracts the visible vertical scrollbar |

## Text and information

| Symptom | Cause | Fix |
|---|---|---|
| The shortcut reference listed "Right click" and "On-page alert" as keyboard shortcuts, and the "IN THE BROWSER" heading was buried in the middle of the list | the table was one flat list with a heading smuggled into a third column | grouped table with a single folded view shared by the help dialog and the About page, and only real shortcuts in it |
| The proxy rows had four unlabelled icon buttons and an invisible row-wide action | the tooltip strings existed but were never attached | one shared tooltip that follows the hovered action, plus a row cursor that changes only over a real target |
| A tool's compact button still rendered as the ghost kind | the kind was never updated when the proxy menu was routed | routed through the same helper |

## Verification of this pass

* Brace/paren/bracket balance clean across `src/*.cs` after every edit.
* No duplicate private field declarations (script-checked per file).
* `ConfirmForm`/`ToolDialog`/`SetupForm`/`InstallGate` `ProcessCmdKey` paths re-read against
  their own Close/Dispose ordering.
* Needs a Windows run: cancel a Chromium download mid-way and close the window; install from
  setup and press Close; open a proxy dropdown and read the tooltips.

---

# Fourth pass — menus, cards and the language table

## Menus

| Symptom | Cause | Fix |
|---|---|---|
| A glyph sat on top of the first two letters of every menu item | the icon was painted twice: once from `OnRenderItemText` at the text rectangle, once from `OnRenderItemImage` at a fixed x — and `ShowImageMargin` was on, so the second lane landed over the label | one owner (the text renderer), positioned from the text rectangle; the image renderer only draws real bitmaps |
| Every menu had an empty gutter on the left | the framework's image lane was reserved although items carry glyph names, not bitmaps | `ShowImageMargin` off by default, on for the country picker that does insert flag chips |
| A long dropdown could be taller than the screen | rows were paged by a hard-coded 34 px row height that ignores DPI and the real item padding | rows measured from the menu font, so the page always fits |
| Closed dropdowns leaked their flag bitmaps | `CloseMenu` closed the strip but never disposed it | closed, then disposed on a posted message; the control disposes whatever is still alive when it goes |

## Cards and surfaces

| Symptom | Cause | Fix |
|---|---|---|
| Four bright pixels in the rounded corners of every card | `JBPanel` cleared its buffer with `Theme.Bg` regardless of what was behind it | clears with `Ui.SurfaceColor(this)` |
| A tool's compact button kept the wrong visual weight | the kind was set once, not when the row was reconfigured | routed through the same helper as the other buttons |

## Language

| Symptom | Cause | Fix |
|---|---|---|
| Choosing Hindi, Indonesian or Vietnamese changed nothing at all | those three were offered by the picker but had no translation table | only languages with a table are offered |
| A partially translated language looked broken rather than unfinished | nothing said how complete it was | the picker shows "N% translated" and choosing one says so once |
| A saved language with no table made every label show raw keys (`nav.profiles`) | `Set` accepted any known language, and `T` falls back per key — but the table lookup failed wholesale | `Set` falls back to English unless a table exists |

## Command line

| Symptom | Cause | Fix |
|---|---|---|
| `--help` started the app; an unknown switch was silently ignored | only three arguments were parsed | `--help` prints the options; an unrecognised switch is logged rather than swallowed |

## Verification of this pass

* Every translated table is a strict subset of the English keys — no orphans (checked by script).
* Every `L10n.T` key used in the source resolves in the English table (208 keys, 0 missing).
* Every icon name passed to `Icons.Draw`/`Icon =` exists in the glyph table; `google` and
  `telegram` are vector brand marks by design.
* Brace/paren/bracket balance clean; no duplicate member declarations.
* Needs a Windows run: open a country dropdown and read the flags, open a profile card menu,
  switch the interface language to German and back.
