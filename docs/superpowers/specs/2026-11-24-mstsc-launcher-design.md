# SynRdpManager — design

Date: 2026-11-24 · Status: approved 2026-11-24

## Why

At a large client, every colleague works inside an Azure Virtual Desktop (AVD). From there
they connect with `mstsc.exe` to 20+ servers at the client's branches. Today each colleague builds
20+ RDP shortcuts by hand, and every new colleague has to do it all over again. Server
logins already live in 1Password shared vaults (one vault per customer, e.g.
"Customer A"), next to unrelated items (FTP, mail, DB, API keys).

SynRdpManager is a small Windows app that shows every RDP server the user can see in
1Password and connects with one double-click, logging in automatically.

## Goals

- One list of all RDP servers, grouped per 1Password vault (= per customer).
- Which items are listed is decided by a **1Password tag**; default `MstscDealer`,
  configurable per installation so another AVD/team can use a different tag.
- Double-click → `mstsc` opens and logs in without typing a password.
- Access control = 1Password vault access. Onboarding a colleague = grant vault access,
  give them the app. No per-user server configuration.
- Linux servers (tag `PuttyDealer`, configurable) in the same list; each becomes a real,
  user-editable PuTTY saved session built from a user-made PuTTY template; double-click
  opens it. Auth via Pageant (key already loaded) — no passwords involved.
- Works when 1Password/`op` is unavailable: show the last known list from an encrypted
  local cache.

## Non-goals

- Editing or creating 1Password items from the app.
- An embedded RDP client; we launch the stock `mstsc.exe`.
- RDP gateway / per-server display settings (can come later via extra item fields).
- macOS/Linux support.

## Anti-goals (would be a failure even if it "works")

- Passwords written to disk, logs, or process command lines.
- Needing a separate download server, config file host or bearer token.
- A colleague having to type server details by hand.

## Constraints (verified)

- 1Password desktop app is installed on the AVD; every colleague has their own account.
- `cmdkey /generic:TERMSRV/<host>` + `mstsc /v:<host>` logs in without a password prompt
  on the AVD (tested by the user 2026-11-24) → Credential Manager approach is allowed.
- Developer works on Linux; app must build there (`EnableWindowsTargeting`) and via
  GitHub Actions. Runtime testing happens on Windows/AVD.

## Approaches considered

| | Approach | Trade-off | |
|---|---|---|---|
| A | C# .NET 8 WPF + 1Password CLI (`op.exe`) with desktop-app integration | Native, polished, single exe; needs `op.exe` + one 1Password setting | **chosen** |
| B | PowerShell + WinForms script | No build step, but clunky and often blocked by execution policy on AVDs | rejected |
| C | Electron / web app | Heavy, adds nothing | rejected |
| — | Encrypted config file downloaded with a bearer token | Extra hosting + token management; 1Password vaults already do this | rejected in favour of tags |

## Design

### Data source: 1Password via `op.exe`
- `op.exe` uses 1Password desktop-app integration (Settings → Developer → "Integrate with
  1Password CLI"); unlock via Windows Hello/PIN, no tokens.
- `op.exe` is looked up in: setting `OpPath` → app folder →
  `%ProgramFiles%\1Password CLI\op.exe` → `PATH`.
- **Verified on the AVD (2026-11-24):** `op` 2.42.0 at `C:\Program Files\1Password CLI\op.exe`
  + "Integrate with 1Password CLI" → `op vault list` works for a normal user (unlock popup,
  then vault list). Gotchas for the README setup guide: `winget install --scope machine`
  puts the real exe under `C:\Program Files\WinGet\Packages`, which normal users get
  "Access is denied" on — copy `op.exe` to `C:\Program Files\1Password CLI\` instead and add
  that folder to the system PATH; the Developer settings in 1Password are greyed out while
  the app is locked; a PATH change needs a sign-out/sign-in. The app does **not** ship `op.exe` (licence); the README tells users where to
  get it.
- The user may be signed in to **multiple 1Password accounts** (the screenshot shows
  "All Accounts"). Refresh iterates `op account list` and queries each account, unless
  setting `Accounts` restricts it.
- Refresh = list items with the configured tag across all vaults of each account, then
  read each item's details (fields + URLs). No passwords are kept in memory after refresh.

### Item conventions (documented in README)
- Item carries the tag (default `MstscDealer`). Category: any (Server, Login, …).
- **Hostname**: custom field labelled `hostname` (case-insensitive) if present; otherwise
  the item's first URL / website, with scheme and path stripped (`rdp://h:3390/` → `h:3390`).
  `host:port` is supported and passed to `mstsc /v:` as-is. Items with no hostname are
  shown greyed out with "no hostname".
- **Username / password**: the fields 1Password marks as username / password. A custom
  `domain` field, if present, is prefixed as `DOMAIN\user` unless the username already
  contains `\` or `@`.
- Display name = item title; group = vault name; groups and items sorted alphabetically.

### Connecting
1. Fetch the item fresh from 1Password (`op item get`) → username + password.
2. Write a Windows credential `TERMSRV/<host>` (host without port) via the Win32
   `CredWrite` API (P/Invoke — never via a `cmdkey` command line). If a credential for that
   target already exists, back it up first (in memory only; accepted risk: lost if the app
   crashes mid-connect). *[builder call]* Use `CRED_PERSIST_SESSION` (not written to the
   roaming profile); only if the AVD smoke test shows `mstsc` ignores session credentials,
   fall back to `CRED_PERSIST_LOCAL_MACHINE`. Every credential we write carries the comment
   `SynRdpManager`; on startup the app deletes any leftover ones (crash recovery).
3. Start `mstsc.exe /v:<host[:port]>`.
4. After a delay (setting `CredentialCleanupSeconds`, default 15) delete our credential
   and restore any backed-up one. Cleanup also runs on app exit.
- If credentials cannot be fetched (offline/locked), start `mstsc /v:<host>` without
  credentials and show a non-blocking notice; the user logs in manually.
- Setting `LaunchMode`: `Auto` (default, above) or `Clipboard` (start mstsc, put the
  password on the clipboard, clear it after 30 s) for environments that block saved creds.
  *[builder call]* The clipboard data is marked `ExcludeClipboardContentFromMonitorProcessing`
  and `CanIncludeInClipboardHistory = 0` / `CanUploadToCloudClipboard = 0`, so it stays out
  of Windows clipboard history and cloud sync.

### PuTTY (Linux servers)
- Items tagged with `PuttyTag` (default `PuttyDealer`) are SSH servers. Same hostname rules
  as above (default port 22); username = the item's username field (optional).
- PuTTY is installed for all users on the AVD (`C:\Program Files\PuTTY\`, on system PATH); `PuttyPath` setting, default lookup:
  `%ProgramFiles%\PuTTY\putty.exe` → `PATH`. Authentication is via **Pageant** with the key
  already loaded; the app does not handle keys. *[builder call]* If no Pageant process is
  running, show a non-blocking warning.
- PuTTY sessions live in `HKCU\Software\SimonTatham\PuTTY\Sessions\<url-encoded name>`.
- **Template first.** Setting `PuttyTemplateSession` (default `SynRdpManager-Template`).
  If PuTTY items exist and the template session does not, the app asks the user to create
  it before creating any session: it copies PuTTY's `Default Settings` key to the template
  name and opens `putty.exe -load "<template>"` so the user can adjust keyboard, colours,
  SSH options etc. and save. Until the template exists, PuTTY items are shown but no
  sessions are generated (double-click explains why).
- **One saved session per server**, named `<Vault> - <Item title>` (characters PuTTY can't
  store are escaped by PuTTY's own URL-encoding). Created on refresh when missing:
  all values copied from the template, then `HostName`, `PortNumber`, `Protocol=ssh` and
  `UserName` (Connection → Data → "Auto-login username") set from 1Password.
- **User edits are preserved.** Existing sessions are not overwritten on refresh; only
  `HostName`, `PortNumber` and `UserName` are updated if they changed in 1Password.
  The app never deletes PuTTY sessions (a server removed from 1Password keeps its session).
- **Reload from template** button (selected server; right-click on a vault = all its
  servers): copy all template values over the session again, keeping the session name,
  `HostName` and `PortNumber`, and re-setting `UserName` from 1Password.
- Double-click = `putty.exe -load "<session>"`.

### Offline cache
- After each successful refresh, save the list (account, vault, title, hostname, item id —
  **no usernames or passwords**) to `%LOCALAPPDATA%\SynRdpManager\cache.bin`, encrypted with
  DPAPI (CurrentUser scope).
- On start: show the cache immediately, then refresh in the background. Status bar shows
  `Online · refreshed 09:12` or `Offline · cached list from 23 Nov 14:05`.

### Settings (per installation, per Windows user)
- Defaults may ship in `settings.json` **next to the exe** (lets a team hand out a
  pre-configured folder with its own tag); user overrides are stored in
  `%APPDATA%\SynRdpManager\settings.json`. User file wins.
- Keys: `Tag` (default `MstscDealer`), `PuttyTag` (default `PuttyDealer`), `OpPath`,
  `PuttyPath`, `PuttyTemplateSession` (default `SynRdpManager-Template`), `Accounts`
  (empty = all), `LaunchMode`, `CredentialCleanupSeconds`.
- Settings window in the app edits the user file; changing a tag triggers a refresh.

### UI (WPF)
```
SynRdpManager                [search…]   [⟳]  [⚙]
─────────────────────────────────────────────────
▼ Customer A
    🖥 Customer A - AL server     server01.customer-a.local
    🐧 Customer A - Linux core    linux01.customer-a.local
▼ Customer B
    🖥 Customer B - RDS           rds.customer-b.local
▶ Customer C (2)
─────────────────────────────────────────────────
● Online · refreshed 09:12            v0.2.0
```
- Search filters on title, vault and hostname. Double-click / Enter = connect.
- 🖥 = RDP (mstsc), 🐧 = SSH (PuTTY).
- Right-click: Connect, Copy hostname, Copy username; on PuTTY items also
  "Reload from template" and "Edit in PuTTY"; on a vault "Reload all from template".
- Toolbar: "Edit PuTTY template" (opens the template session in PuTTY).
- Friendly first-run errors with fix instructions: `op.exe` not found, CLI integration
  disabled, 1Password locked / not signed in, zero tagged items found ("tag `X` has no
  items — tag your RDP items in 1Password").

### Delivery
- .NET 8, `win-x64`, self-contained single-file exe.
- GitHub Actions builds on push and attaches a zip to a GitHub Release on version tags.
- Version kept in `VERSION` and the csproj (csproj reads `VERSION`, single source of truth).
  Versioning rules: all work on `dev`; every commit bumps the patch number (done by the
  developer/Claude, not CI) and is pushed to `dev`. "Bump minor/major" = update
  `CHANGELOG.md` + `README.md`, merge `dev` → `master`, push both. Any version **component**
  equal to 13 is skipped (x.x.12 → x.x.14; 0.12.x → 0.14.0), with a commit described
  "To be sure to be sure!".
- CI builds every push; a tag `vX.Y.Z` on `master` (created at each minor/major bump)
  creates a GitHub Release with the zip.

### Testing
- Unit tests (run on Linux) for: hostname extraction, domain/username composition,
  `op` JSON parsing (fixtures), grouping/sorting/search, cache round-trip (encryption
  abstracted behind an interface), settings layering, PuTTY session create / update /
  reload-from-template rules (registry behind an interface, in-memory fake in tests).
- `op`, Credential Manager, registry and process launch behind interfaces so logic is testable;
  manual smoke test on the AVD per release.

## Left to the builder

Project/folder layout, MVVM details, exact `op` command flags and batching, logging
(must never log secrets), icons and styling.
