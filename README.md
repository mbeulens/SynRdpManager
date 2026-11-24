# SynRdpManager

Version: 0.1.28

## What it does

SynRdpManager is a small Windows app (WPF) that lists every RDP and SSH server you can see in
1Password, grouped per vault (= per customer), and connects with one double-click:

- **RDP** servers (1Password tag `MstscDealer`): the app writes a temporary Windows credential, starts
  `mstsc.exe` and you are logged in without typing a password. The credential is removed again after 15 seconds.
- **SSH** servers (tag `PuttyDealer`): the app creates a real PuTTY saved session per server, based on a
  template you maintain, and opens it with PuTTY. Authentication is via Pageant (no passwords involved).
- Access control is 1Password vault access. Onboarding a colleague = grant vault access and hand out the app.
- When 1Password is unavailable the last known list is shown from an encrypted local cache (no usernames or passwords in it).

The app never edits 1Password items, never deletes PuTTY sessions and never writes passwords to disk, logs or command lines.

## Download and install

Download `SynRdpManager-win-Setup.exe` from the latest release on
https://github.com/mbeulens/SynRdpManager/releases and run it. It installs per user (no admin rights needed) and
creates a Start menu and desktop shortcut. Windows SmartScreen may warn about an unknown publisher: click **More info -> Run anyway**.
`SynRdpManager-win-Portable.zip` is also available for use without installing. Builds of every push are available as the
`SynRdpManager-<version>` artifact of the workflow run on the Actions tab (GitHub sign-in required).

**Updates are automatic.** The app checks the GitHub Releases in the background. When a new version has been downloaded a
banner says "Version X is ready" with a **Restart now** button; otherwise it is applied the next time you start the app.
You can check manually via the gear menu (⚙ ▾) -> **Check for updates**.

On FSLogix profile setups the profile container must not exclude `AppData\Local`: the app installs there and keeps its updates there.

## AVD admin setup (one-time)

1. **1Password** desktop app installed for all users (each colleague has their own account).
2. **1Password CLI (`op.exe`)**: download the ZIP for Windows from https://developer.1password.com/docs/cli/get-started/ ,
   then:
   - copy `op.exe` to `C:\Program Files\1Password CLI\`
   - add `C:\Program Files\1Password CLI` to the **system** `PATH`
   - users must sign out and in again for the PATH change to apply

   Do **not** use `winget install --scope machine`: the real exe then sits under
   `C:\Program Files\WinGet\Packages`, where normal users get "Access is denied".
3. **PuTTY** installed for all users at `C:\Program Files\PuTTY\` (only needed for SSH servers).
4. Colleagues install the app themselves with `SynRdpManager-win-Setup.exe` (see [Download and install](#download-and-install)).
   Optionally put a `settings.json` with team defaults in `%LocalAppData%\SynRdpManager\` **after** installing (see [Settings](#settings)); start from `settings.sample.json`.

## Per-user setup

1. Start 1Password and **unlock it first**: the Developer settings are greyed out while it is locked.
2. 1Password -> Settings -> Developer -> enable **Integrate with 1Password CLI**.
3. For SSH servers: make sure **Pageant** is running with your key loaded.
4. Start SynRdpManager from the Start menu. The first refresh shows a 1Password approval popup: approve it
   (Windows Hello / PIN). The list appears; double-click a server to connect.

## Tagging items in 1Password

Add the tag `MstscDealer` (RDP) or `PuttyDealer` (SSH) to an item. The category does not matter (Server, Login, ...).
The tag names are configurable, see Settings.

| What | Rule |
|---|---|
| Display name | The item title |
| Group | The vault name; groups and items are sorted alphabetically |
| Hostname | Custom field labelled `hostname` (case-insensitive) if present; otherwise the item's first URL / website with scheme and path stripped (`rdp://server01.customer-a.local:3390/` -> `server01.customer-a.local:3390`). `host:port` is supported. Items without a hostname are shown greyed out ("no hostname"). |
| Username / password | The fields 1Password marks as username and password |
| Domain | Optional custom field `domain`; the username becomes `DOMAIN\user` unless it already contains `\` or `@` |
| SSH port | Default 22, or `host:port` |

Example: item "Customer A - AL server" in vault "Customer A", tag `MstscDealer`, field `hostname` = `server01.customer-a.local`,
`domain` = `CUSTOMERA`, username `jdoe` -> logs in as `CUSTOMERA\jdoe`.

## Settings

Team defaults go in `%LocalAppData%\SynRdpManager\settings.json` (create it **after** installing; it survives updates, but uninstalling SynRdpManager removes that whole folder including this file, so keep a copy; a `settings.json` next to the exe still works for portable use). Each user's own overrides are stored in
`%APPDATA%\SynRdpManager\settings.json` (written by the Settings window). **The user file wins.** Both files are plain JSON;
see `settings.sample.json`. Changing a tag, `OpPath` or `Accounts` triggers a refresh.

| Key | Default | Meaning |
|---|---|---|
| `Tag` | `MstscDealer` | 1Password tag that marks RDP servers |
| `PuttyTag` | `PuttyDealer` | 1Password tag that marks SSH servers |
| `OpPath` | none | Full path to `op.exe`. Lookup order: `OpPath` -> app folder -> `%ProgramFiles%\1Password CLI\op.exe` -> `PATH` |
| `PuttyPath` | none | Full path to `putty.exe`. Lookup order: `PuttyPath` -> `%ProgramFiles%\PuTTY\putty.exe` -> `PATH` |
| `PuttyTemplateSession` | `SynRdpManager-Template` | Name of the PuTTY session used as template for generated sessions |
| `Accounts` | `[]` (all accounts) | Restrict to these 1Password account UUIDs or emails |
| `LaunchMode` | `Auto` | `Auto`: temporary credential + mstsc. `Clipboard`: start mstsc and put the password on the clipboard for 30 s (excluded from Windows clipboard history and cloud sync) for environments that block saved credentials |
| `CredentialCleanupSeconds` | `15` | Seconds before the temporary credential is removed (allowed 5-120) |

## PuTTY template

PuTTY servers are turned into real PuTTY saved sessions, copied from one template so they all share your keyboard,
colour and SSH settings.

1. The first time SSH servers are listed and the template session does not exist, the app asks
   "Create PuTTY template now?". Yes copies PuTTY's `Default Settings` to `SynRdpManager-Template` and opens it in PuTTY.
2. Adjust keyboard, colours, SSH options etc. and click **Save** in PuTTY. (Later: toolbar -> "Edit PuTTY template".)
3. The app creates one session per server named `<Vault> - <Item title>` (all values from the template, plus host, port,
   protocol ssh and auto-login username from 1Password). Until the template exists no sessions are generated.
4. Double-click opens `putty.exe -load "<session>"`.
5. Your edits to a session survive refreshes: only host, port and username are updated if they changed in 1Password.
   Sessions of servers removed from 1Password are kept.
6. Changed the template? Right-click a server -> **Reload from template** (or right-click a vault -> **Reload all from template**).
   This keeps the session name, host and port.

## Troubleshooting

| Message | Cause and fix |
|---|---|
| 1Password CLI (op.exe) was not found. Install it or set its path in Settings. | `op.exe` is not installed or not on PATH. See AVD admin setup, or set `OpPath`. After a PATH change sign out and in. |
| Cannot connect to the 1Password desktop app. Enable Settings > Developer > Integrate with 1Password CLI, then press Refresh. | Turn on the integration (unlock 1Password first, the setting is greyed out while locked). |
| 1Password is locked or the request was not approved. Unlock 1Password and press Refresh. | Unlock 1Password and approve the popup. |
| No 1Password accounts are configured for the CLI. Add or sign in to an account in 1Password, then press Refresh. | Sign in to your account in the desktop app. |
| 1Password did not respond in time. Unlock 1Password and press Refresh. | Approval popup was not answered, or 1Password is busy. Unlock and retry. |
| No 1Password accounts are available to the CLI. Unlock 1Password and enable Settings → Developer → "Integrate with 1Password CLI", then press Refresh. | The CLI sees no account: usually the CLI integration is off. Unlock 1Password and turn it on. |
| None of the accounts in the Accounts setting are signed in to 1Password. Check the Accounts setting. | The `Accounts` setting matches no signed-in account. Fix or clear it. |
| op.exe could not be started (…). Check that normal users can run it — see README AVD admin setup. | `op.exe` exists but cannot be run (e.g. access denied). See AVD admin setup. |
| 1Password CLI failed (exit code N): … | The text after the colon is the CLI's error. Run `op vault list` in a terminal for details. |
| No items tagged "MstscDealer" or "PuttyDealer" found | Tag your server items in 1Password (or check the tag names in Settings and that you have vault access). |
| "..." has no hostname | Add a `hostname` field or a website URL to the item. |
| mstsc asks for a password | Credentials could not be fetched (a notice says why), or `LaunchMode` is `Clipboard`. If it still prompts in `Auto`, report it. |
| PuTTY (putty.exe) was not found. | Install PuTTY for all users or set `PuttyPath`. |
| The PuTTY template session "..." does not exist yet | Use "Edit PuTTY template" in the toolbar and save it in PuTTY. |
| Pageant is not running | Start Pageant and load your key, then connect again. |
| Offline · cached list from … | 1Password could not be reached; the last known list is shown. Fix the cause above and press Refresh. |

## Development

Requires the .NET 10 SDK. The app builds on Linux too (`EnableWindowsTargeting`); running it needs Windows.

```
dotnet build
dotnet test
dotnet publish src/SynRdpManager.App -c Release -r win-x64 --self-contained -o publish
```

- Projects: `src/SynRdpManager.Core` (logic, testable), `src/SynRdpManager.App` (WPF), `tests/SynRdpManager.Core.Tests`.
- Design: `docs/superpowers/specs/2026-11-24-mstsc-launcher-design.md`. AVD checklist: `docs/SMOKE-TEST.md`.
- CI (`.github/workflows/build.yml`) tests, publishes and packs (Velopack) every push; a tag `vX.Y.Z` creates a GitHub Release.
- **Versioning:** the `VERSION` file is the single source of truth. All work happens on `dev`; every commit bumps the
  patch number in `VERSION` and the `Version:` line above, and is pushed to `dev`. "Bump minor/major" = update
  `CHANGELOG.md` and this README, merge `dev` into `master`, push both, tag `vX.Y.Z`.
- **Skip-13 rule:** any version component equal to 13 is skipped (0.1.12 -> 0.1.14, 0.12.x -> 0.14.0) and that commit's
  subject is exactly `To be sure to be sure!`.

### Releasing

Say "bump minor" or "bump major": `CHANGELOG.md` and this README are updated, `dev` is merged into `master`, and the tag
`vX.Y.Z` (must equal `VERSION`) is pushed. CI then publishes the GitHub Release with the installer and update packages,
and colleagues receive the update automatically.
