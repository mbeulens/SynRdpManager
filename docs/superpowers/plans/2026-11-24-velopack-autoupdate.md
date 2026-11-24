# Velopack Install & Auto-update Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Colleagues install SynRdpManager once with `SynRdpManager-win-Setup.exe` (per-user, no admin) and get every new GitHub Release automatically.

**Architecture:** Velopack's `VelopackApp.Build().Run()` runs first in a custom `Main`; an `UpdateService` in the App checks `GithubSource` in the background after the window shows, downloads updates and offers "Restart now" via the existing banner. CI packs the published folder with the `vpk` CLI on every push (artifact) and uploads a GitHub Release on `v*` tags. Team settings move to a location that survives updates.

**Tech Stack:** .NET 10 WPF, Velopack (NuGet `Velopack` + `vpk` dotnet tool, same version), GitHub Actions, xUnit.

**Spec:** `docs/superpowers/specs/2026-11-24-velopack-autoupdate-design.md` (and the main spec `docs/superpowers/specs/2026-11-24-mstsc-launcher-design.md`)

## Global Constraints

- Repo URL for updates: `https://github.com/mbeulens/SynRdpManager` (public, no token at runtime). Velopack `packId` = `SynRdpManager`, main exe `SynRdpManager.exe`.
- Velopack NuGet package and `vpk` tool pinned to the **same** latest stable version.
- Update check never blocks startup or the UI; any update failure (offline, GitHub down, rate limit) is silent except when the user explicitly chose "Check for updates" (then a banner says it failed / is up to date).
- Not installed via Velopack (`UpdateManager.IsInstalled == false`, e.g. dev build or portable zip) → skip update checks entirely.
- Before applying an update and restarting: `RdpLauncher.CleanupPendingNow()` runs.
- Team settings lookup: `<exe dir>\settings.json`, then — only when the exe dir is named `current` (Velopack layout) — `<exe dir>\..\settings.json` (i.e. `%LocalAppData%\SynRdpManager\settings.json`). First existing file wins.
- Releases only from tags `vX.Y.Z` on `master` (created at "bump minor/major"); `dev` pushes only produce artifacts.
- Git rules unchanged: work on `dev`; every commit bumps the patch in `VERSION` + README `Version:` line and is pushed; skip any component equal to 13 with subject `To be sure to be sure!`; Co-Authored-By trailer naming the model that wrote the commit.
- No secrets in update logs/banners (there are none involved, keep it that way).

## Review Focus

1. **App installed in the Velopack layout reads team settings after an update** (`current\` replaced) → Task 1 test `TeamSettingsCandidates_includes_velopack_root_when_exe_dir_is_current`.
2. **Offline / GitHub unreachable at startup** must not show errors or delay the window → Task 1 (UpdateService swallows on automatic checks; reviewed by reading).
3. **Running from a non-installed build** (dev/portable) must not throw from `UpdateManager` → Task 1 (`IsInstalled` guard).
4. **CI on a `dev` push must not try to publish a release** → Task 2 (upload step gated on `startsWith(github.ref, 'refs/tags/v')`).
5. **First release has no previous version for deltas** → Task 2 (`vpk download github` must tolerate "no releases" — `continue-on-error` or equivalent).

---

### Task 1: Velopack in the app (entry point, update service, team settings location)

**Files:**
- Create: `src/SynRdpManager.App/Program.cs` (custom `[STAThread] Main`), `src/SynRdpManager.App/Services/UpdateService.cs`
- Modify: `src/SynRdpManager.App/SynRdpManager.App.csproj` (package `Velopack`; `StartupObject`; `App.xaml` as `Page` instead of `ApplicationDefinition` if needed for custom Main), `src/SynRdpManager.App/App.xaml(.cs)`, `src/SynRdpManager.App/Platform/AppPaths.cs`, `src/SynRdpManager.App/Services/SettingsFiles.cs` (or wherever the team file is read), `src/SynRdpManager.App/ViewModels/MainViewModel*.cs`, `src/SynRdpManager.App/Views/MainWindow.xaml`
- Modify: `src/SynRdpManager.Core/Settings/SettingsLoader.cs` (add candidates helper)
- Test: `tests/SynRdpManager.Core.Tests/Settings/TeamSettingsCandidatesTests.cs`

**Interfaces:**
- Produces: `static IReadOnlyList<string> SettingsLoader.TeamSettingsCandidates(string exeDir)` — returns `[exeDir\settings.json]` plus `[parent\settings.json]` when `Path.GetFileName(exeDir.TrimEnd('\\','/'))` equals `current` (case-insensitive); uses `Path.Combine`/`Path.GetDirectoryName` (tests use the host's separator).
- Produces: `sealed class UpdateService(string repoUrl, Action<string> showBanner, Action beforeRestart)` with `Task CheckInBackgroundAsync()` (automatic, silent on failure) and `Task CheckNowAsync()` (manual; banners "You're on the latest version (vX)." / "Update check failed — try again later." / "Version X is ready — restart to update."), `bool IsUpdateReady`, `void RestartToUpdate()` (calls `beforeRestart` then `ApplyUpdatesAndRestart`).

- [ ] **Step 1: Failing tests** `TeamSettingsCandidates_returns_exe_dir_file` (`/x/app` → `[/x/app/settings.json]`), `TeamSettingsCandidates_includes_velopack_root_when_exe_dir_is_current` (`/x/SynRdpManager/current` → `[/x/SynRdpManager/current/settings.json, /x/SynRdpManager/settings.json]`), `..._is_case_insensitive_and_ignores_trailing_separator` (`/x/S/Current/`).
- [ ] **Step 2: Run** `dotnet test --filter TeamSettingsCandidates` → FAIL. **Step 3: Implement**, run → PASS.
- [ ] **Step 4: App wiring.**
  - `Program.Main`: `VelopackApp.Build().Run();` then create `App`, `InitializeComponent()`, `Run()`.
  - Team settings: first existing of `TeamSettingsCandidates(AppContext.BaseDirectory)`.
  - `UpdateService` created in composition root with `beforeRestart = RdpLauncher.CleanupPendingNow`; `CheckInBackgroundAsync()` started after the main window is shown (fire-and-forget with all exceptions caught).
  - Banner gets an optional action button; when an update is ready show "Version X is ready — restart to update" with button **Restart now** → `RestartToUpdate()`.
  - Replace the ⚙ button with a small menu (⚙ ▾): **Settings…** (existing) and **Check for updates** → `CheckNowAsync()`; when not installed via Velopack, "Check for updates" shows "Updates work only when installed with Setup.exe."
  - Status bar version unchanged.
- [ ] **Step 5: Verify** `dotnet build src/SynRdpManager.App -c Release` 0 warnings; full `dotnet test` green.
- [ ] **Step 6: Commit + push** (bump patch).

---

### Task 2: CI packaging/release, README, smoke test

**Files:**
- Modify: `.github/workflows/build.yml`, `README.md`, `docs/SMOKE-TEST.md`, `src/SynRdpManager.App/SynRdpManager.App.csproj` (stop `PublishSingleFile` for Velopack packaging — Velopack packs a folder; drop `PublishSingleFile`/`IncludeNativeLibrariesForSelfExtract` or override on the CLI, your call, documented in the report)

- [ ] **Step 1: Workflow** (runs-on `windows-latest`):
  - `dotnet test -c Release`.
  - `dotnet publish src/SynRdpManager.App -c Release -r win-x64 --self-contained -o publish` (folder, not single-file).
  - `dotnet tool install -g vpk --version <same as NuGet Velopack>`.
  - On tags only: `vpk download github --repoUrl https://github.com/mbeulens/SynRdpManager --token ${{ secrets.GITHUB_TOKEN }} -o releases` (must not fail the job when no release exists yet).
  - Every push: `vpk pack --packId SynRdpManager --packVersion <VERSION file> --packDir publish --mainExe SynRdpManager.exe -o releases`.
  - Every push: upload artifact `SynRdpManager-<version>` containing `releases/*Setup.exe`, `releases/*Portable.zip`, and `settings.sample.json`.
  - Tags only: `vpk upload github --repoUrl https://github.com/mbeulens/SynRdpManager --publish --releaseName "SynRdpManager <version>" --tag ${{ github.ref_name }} --token ${{ secrets.GITHUB_TOKEN }} -o releases`; also attach `settings.sample.json` to that release. Remove the old `softprops/action-gh-release` step. `permissions: contents: write` on the job.
- [ ] **Step 2: README:** Download/Install section → run `SynRdpManager-win-Setup.exe` from the latest Release (per-user, no admin, SmartScreen "More info → Run anyway"), updates are automatic (banner "Restart now", or applied on next start, ⚙ ▾ → Check for updates). Team settings now go in `%LocalAppData%\SynRdpManager\settings.json` (survives updates); next-to-exe still works for portable use. FSLogix note (AppData\Local must not be excluded). Release process: "bump minor/major" → tag on master → CI publishes. Keep `Version:` line.
- [ ] **Step 3: SMOKE-TEST:** new first steps: download Setup.exe from the Release, install (note SmartScreen / AppLocker messages), app starts from Start menu; later step: after the *next* release, start app → banner "Version X is ready" → Restart now → status bar shows new version; team settings in `%LocalAppData%\SynRdpManager\settings.json` survive the update.
- [ ] **Step 4: Push, watch CI** (`gh run watch --exit-status`), download the artifact and confirm it contains `SynRdpManager-win-Setup.exe`, a `*Portable.zip`, and `settings.sample.json`. **Commit + push** (bump patch).
