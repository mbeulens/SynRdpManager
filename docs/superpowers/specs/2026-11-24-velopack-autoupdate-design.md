# SynRdpManager — install & auto-update (Velopack) design

Date: 2026-11-24 · Status: approved 2026-11-24

## Why
Colleagues should install SynRdpManager once and then always run the latest version without
anyone copying files around. New features ship by the user's existing "bump minor/major" flow.

## Decisions (by the user)
- Velopack, per-user install (no admin), updates from **GitHub Releases** of the public repo
  `mbeulens/SynRdpManager`.
- AVD is **pooled with FSLogix**: the per-user install lives in `%LocalAppData%`, which is in the
  FSLogix profile container, so it follows the user to any session host.

## Approaches considered
| | Approach | Trade-off | |
|---|---|---|---|
| A | Velopack + GitHub Releases | No admin, delta updates, Start-menu shortcut, maintained; one package + CI step | **chosen** |
| B | Own self-updater (download zip, swap exe) | No dependency, but we maintain swap logic and download 140 MB each time | rejected |
| C | Admin installs centrally in Program Files | Simple, but manual updates per session host | rejected |

## Design
- **Install:** colleague downloads `SynRdpManager-win-Setup.exe` from the latest GitHub Release
  and runs it → installs to `%LocalAppData%\SynRdpManager\` (Velopack layout: `current\`,
  `packages\`, `Update.exe`) and creates a Start-menu + desktop shortcut. No admin.
- **Update check:** on startup (after the window shows, in the background) and via a
  "Check for updates" item in the ⚙ menu. Uses `UpdateManager(new GithubSource(repoUrl, null, false))`.
  If an update exists: download in the background, then show a non-blocking banner
  "Version X is ready — restart to update" with a **Restart now** button
  (`ApplyUpdatesAndRestart`); otherwise it is applied automatically on the next start.
  No network / GitHub down → silent (no banner), logged nowhere.
- **Running from the zip / dev build** (not installed via Velopack): update check is skipped
  (`IsInstalled == false`); the app keeps working as today.
- **Entry point:** custom `Main` that calls `VelopackApp.Build().Run()` before WPF starts
  (Velopack requirement).
- **Team settings must survive updates:** `current\` is replaced on every update, so a team
  `settings.json` next to the exe would be wiped. Team settings lookup becomes: next to the exe,
  then the Velopack root `%LocalAppData%\SynRdpManager\settings.json`. README tells admins/teams
  to put team settings in the root. User settings (`%APPDATA%`) and cache (`%LOCALAPPDATA%\SynRdpManager\cache.bin`,
  in the root, untouched by updates) are unaffected.
- **Before an update restarts the app**, `RdpLauncher.CleanupPendingNow()` runs (same as exit).
- **Publishing (CI):** on tag `vX.Y.Z` on `master` (created by "bump minor/major"):
  `dotnet publish` (self-contained win-x64, **not** single-file — Velopack packages the folder
  and produces delta updates), `vpk download github` (previous release, for deltas),
  `vpk pack --packId SynRdpManager --packVersion X.Y.Z --packDir out --mainExe SynRdpManager.exe`,
  `vpk upload github --publish --tag vX.Y.Z` with the workflow's `GITHUB_TOKEN`.
  Push builds on `dev` keep producing the plain zip artifact for testing.
- **Versioning:** Velopack needs SemVer → our `X.Y.Z` already is. Skip-13 rule unchanged.

## Risks / to verify on the AVD
- Unsigned `Setup.exe` → SmartScreen "Windows protected your PC" (More info → Run anyway).
  A code-signing certificate can come later.
- AppLocker/WDAC may block executables under `%LocalAppData%` on some AVDs — check on first install.
- FSLogix must not exclude `AppData\Local\SynRdpManager` (default config includes it).

## Non-goals
Code signing, MSIX, staged/channel rollouts, auto-update of `op.exe` or PuTTY.
