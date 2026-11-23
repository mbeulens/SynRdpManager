# SynRdpManager Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A Windows desktop app that lists every RDP (tag `MstscDealer`) and SSH (tag `PuttyDealer`) server the user can see in 1Password, grouped per vault, and connects with one double-click: `mstsc` with automatic login, or a generated PuTTY saved session.

**Architecture:** A platform-neutral `SynRdpManager.Core` library holds all logic (op JSON parsing, hostname rules, settings layering, cache codec, PuTTY session rules, RDP launch orchestration) behind small interfaces and is unit-tested on Linux. The WPF app `SynRdpManager.App` (net10.0-windows) supplies Windows implementations (op process runner, DPAPI, `CredWrite`, registry, clipboard) and the UI. GitHub Actions builds, tests and publishes a single-file exe.

**Tech Stack:** .NET 10 (LTS), C# 14, WPF, CommunityToolkit.Mvvm, System.Security.Cryptography.ProtectedData, xUnit, Microsoft.Extensions.TimeProvider.Testing, 1Password CLI 2.x (`op.exe`), PuTTY, GitHub Actions.

**Spec:** `docs/superpowers/specs/2026-11-24-mstsc-launcher-design.md`

## Global Constraints

- **.NET 10, not .NET 8:** .NET 8 left support on 2026-11-10. Use `net10.0` for Core/Tests and `net10.0-windows` + `<UseWPF>true</UseWPF>` + `<EnableWindowsTargeting>true</EnableWindowsTargeting>` for the App. Task 1 also corrects the spec's ".NET 8" mentions.
- Publish target: `win-x64`, self-contained, `PublishSingleFile=true`, `IncludeNativeLibrariesForSelfExtract=true`.
- Version single source of truth: `VERSION` file at repo root, read by `Directory.Build.props`.
- **Git rules:** work on `dev` only; **every commit bumps the patch number** in `VERSION` + the `Version:` line in `README.md` and is pushed to `origin dev`. Any version component equal to 13 is skipped (0.1.12 → 0.1.14), and that commit's description is `To be sure to be sure!`. Never merge to `master` unless the user says "bump minor/major". Every commit message ends with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- Defaults (exact): `Tag = "MstscDealer"`, `PuttyTag = "PuttyDealer"`, `PuttyTemplateSession = "SynRdpManager-Template"`, `LaunchMode = Auto`, `CredentialCleanupSeconds = 15`, clipboard clear after 30 s, credential comment `"SynRdpManager"`, default SSH port 22.
- Paths (exact): cache `%LOCALAPPDATA%\SynRdpManager\cache.bin`; user settings `%APPDATA%\SynRdpManager\settings.json`; team defaults `settings.json` next to the exe; PuTTY sessions `HKCU\Software\SimonTatham\PuTTY\Sessions\<escaped name>`.
- `op.exe` lookup order: setting `OpPath` → app folder → `%ProgramFiles%\1Password CLI\op.exe` → `PATH`. `putty.exe`: `PuttyPath` → `%ProgramFiles%\PuTTY\putty.exe` → `PATH`.
- **Secrets:** never write passwords to disk, logs, exceptions or any process command line. Passwords travel only `op` stdout → memory → `CredWrite`/clipboard. The cache never contains usernames or passwords.
- The app never deletes PuTTY sessions and never edits 1Password items.

## Review Focus

1. **Odd characters in vault/item titles** (`\`, `*`, `?`, `%`, `/`, leading `.`, non-ASCII) must produce a PuTTY session name that PuTTY itself shows and loads → tested in Task 6 (`Escape_matches_putty_rules`).
2. **Two items that map to the same session name** (same vault + title, or a title equal to the template / `Default Settings`) must not overwrite each other → Task 6 (`Duplicate_names_get_suffix`).
3. **`op` waiting on the unlock popup, dismissed, or returning errors** must give a clear status, not a hang or crash; a 120 s timeout applies → Task 3 (`Classify_*`, `Timeout_maps_to_TimedOut`).
4. **Hostname forms people actually type**: `server01`, `server01:3390`, `rdp://server01/`, `https://x/y`, padded spaces, `[fe80::1]:3389`, empty → Task 2 (`HostParser_cases`).
5. **Unreadable cache** (corrupt file, other Windows user, old schema, different tag) must give an empty list plus a status, never a crash or stale list from another tag → Task 5 (`Decode_returns_null_on_garbage`, `Snapshot_for_other_tag_is_ignored`).

---

## File Structure

```
SynRdpManager.sln
Directory.Build.props                 version from VERSION, common settings
VERSION
global.json                           pins SDK 10.0.x
.github/workflows/build.yml           build + test + publish; release on tag v*
src/SynRdpManager.Core/
  Models/ServerEntry.cs               ServerEntry, ConnectionKind, HostAddress, RdpCredentials
  Hosts/HostParser.cs                 hostname/port extraction
  OnePassword/OpModels.cs             OpAccount, OpItemSummary, OpItem, OpField
  OnePassword/OpJson.cs               parse op JSON (arrays + concatenated objects)
  OnePassword/ItemMapper.cs           OpItem -> ServerEntry, username/password extraction
  OnePassword/OpClient.cs             runs op via IProcessRunner; OpException/OpErrorKind/classifier
  Platform/Interfaces.cs              IProcessRunner, IProcessLauncher, IDataProtector,
                                      ICredentialStore, IClipboardService, IPuttySessionStore
  Platform/ExeLocator.cs              lookup chain for op.exe / putty.exe
  Settings/AppSettings.cs             record + LaunchMode
  Settings/SettingsLoader.cs          layering + save-diff
  Catalog/CatalogService.cs           refresh across accounts/tags -> RefreshResult
  Catalog/CatalogView.cs              grouping, sorting, search
  Catalog/CacheCodec.cs               CacheSnapshot <-> encrypted bytes
  Putty/PuttyNames.cs                 escape + session naming
  Putty/PuttySessionManager.cs        template, sync, reload-from-template
  Rdp/RdpLauncher.cs                  credential stash/cleanup, clipboard mode, mstsc start
src/SynRdpManager.App/                WPF (net10.0-windows)
  Platform/*.cs                       Windows implementations of Core interfaces
  ViewModels/*.cs, Views/*.xaml       UI
tests/SynRdpManager.Core.Tests/       xUnit, mirrors Core folders, Fixtures/*.json
```

---

### Task 1: Toolchain, solution scaffold, versioning, CI

**Files:**
- Create: `global.json`, `Directory.Build.props`, `SynRdpManager.sln`, `src/SynRdpManager.Core/SynRdpManager.Core.csproj`, `src/SynRdpManager.App/SynRdpManager.App.csproj`, `src/SynRdpManager.App/App.xaml(.cs)`, `src/SynRdpManager.App/MainWindow.xaml(.cs)` (empty window), `tests/SynRdpManager.Core.Tests/SynRdpManager.Core.Tests.csproj`, `tests/SynRdpManager.Core.Tests/VersionTests.cs`, `.github/workflows/build.yml`, `.gitignore` (add `bin/`, `obj/`, `*.user`, `.vs/`)
- Modify: `docs/superpowers/specs/2026-11-24-mstsc-launcher-design.md` (".NET 8" → ".NET 10")

**Interfaces:**
- Produces: assembly version = contents of `VERSION`; projects `SynRdpManager.Core` (namespace root `SynRdpManager.Core`), `SynRdpManager.App`, `SynRdpManager.Core.Tests`.

- [ ] **Step 1: Install the .NET 10 SDK user-locally (no sudo)**
  Run: `curl -sSL https://dot.net/v1/dotnet-install.sh -o "$SCRATCH/dotnet-install.sh" && bash "$SCRATCH/dotnet-install.sh" --channel 10.0 --install-dir "$HOME/.dotnet"` then `export PATH="$HOME/.dotnet:$PATH"; dotnet --version`
  Expected: `10.0.x`. Pin it in `global.json` with `"rollForward": "latestFeature"`.
- [ ] **Step 2: Create solution and projects** (`dotnet new sln`, `classlib`, `wpf`, `xunit`), references Tests→Core, App→Core. App csproj: `net10.0-windows`, `UseWPF`, `EnableWindowsTargeting`, `RuntimeIdentifier win-x64`, `SelfContained`, `PublishSingleFile`, `IncludeNativeLibrariesForSelfExtract`, `AssemblyName SynRdpManager`. `Directory.Build.props`: `<Version>$([System.IO.File]::ReadAllText('$(MSBuildThisFileDirectory)VERSION').Trim())</Version>`, `Nullable enable`, `TreatWarningsAsErrors true`, `LangVersion latest`.
- [ ] **Step 3: Write failing test** `VersionTests.Assembly_version_matches_VERSION_file`: reads `VERSION` (walk up from `AppContext.BaseDirectory` until found) and asserts `typeof(SynRdpManager.Core.CoreInfo).Assembly.GetName().Version!.ToString(3) == fileVersion`.
- [ ] **Step 4: Run** `dotnet test` → FAIL (`CoreInfo` missing). Add `public static class CoreInfo { }` in Core. Run → PASS.
- [ ] **Step 5: Build the App on Linux** `dotnet build src/SynRdpManager.App -c Release` → `Build succeeded`, 0 warnings.
- [ ] **Step 6: CI** `.github/workflows/build.yml`: on push (all branches) and tags `v*`; `runs-on: windows-latest`; `actions/setup-dotnet@v4` with `global-json-file`; `dotnet test -c Release`; `dotnet publish src/SynRdpManager.App -c Release -o out`; zip `out/SynRdpManager.exe` + `settings.sample.json` (created in Task 10; use `if-no-files-found: warn` until then) to `SynRdpManager-<version>.zip`; upload artifact; on `refs/tags/v*` attach the zip to a GitHub Release (`softprops/action-gh-release@v2`, `permissions: contents: write`).
- [ ] **Step 7: Commit + push** (bump patch). Then `gh run watch` the run → green.

---

### Task 2: Core models and hostname rules

**Files:**
- Create: `src/SynRdpManager.Core/Models/ServerEntry.cs`, `src/SynRdpManager.Core/Hosts/HostParser.cs`
- Test: `tests/SynRdpManager.Core.Tests/Hosts/HostParserTests.cs`

**Interfaces:**
- Produces:
  - `enum ConnectionKind { Rdp, Ssh }`
  - `record HostAddress(string Host, int? Port)` with `string MstscTarget` (`Host` or `Host:Port`; IPv6 host re-bracketed when a port is present), `string CredentialTarget` (`"TERMSRV/" + Host`, never the port), `int SshPort` (`Port ?? 22`)
  - `record ServerEntry(string AccountId, string AccountName, string VaultId, string VaultName, string ItemId, string Title, ConnectionKind Kind, HostAddress? Address)`
  - `record RdpCredentials(string UserName, string Password)` — `ToString()` overridden to `"RdpCredentials(<user>, ***)"`
  - `static HostAddress? HostParser.Parse(string? raw)`

- [ ] **Step 1: Failing theory** `HostParser_cases`:

| input | Host | Port |
|---|---|---|
| `server01` | `server01` | null |
| `  server01.customer-a.local  ` | `server01.customer-a.local` | null |
| `server01:3390` | `server01` | 3390 |
| `rdp://server01:3390/` | `server01` | 3390 |
| `https://server01/path?q=1` | `server01` | null |
| `ssh://user@linux01:2222` | `linux01` | 2222 |
| `[fe80::1]:3389` | `fe80::1` | 3389 |
| `10.0.0.5` | `10.0.0.5` | null |
| `""`, `"   "`, `null`, `"server01:abc"`, `"server01:70000"` | → `null` | |

  Plus `MstscTarget_and_CredentialTarget`: `new HostAddress("h",3390).MstscTarget == "h:3390"`, `.CredentialTarget == "TERMSRV/h"`; `new HostAddress("fe80::1",3389).MstscTarget == "[fe80::1]:3389"`; `RdpCredentials_ToString_hides_password`.
- [ ] **Step 2: Run** `dotnet test --filter HostParser` → FAIL (types missing).
- [ ] **Step 3: Implement** — strip scheme (`://`), userinfo (`@`), path/query; split port on last `:` unless bracketed IPv6; port must be 1–65535.
- [ ] **Step 4: Run** → PASS. **Step 5: Commit + push** (bump patch).

---

### Task 3: 1Password CLI client

**Files:**
- Create: `src/SynRdpManager.Core/OnePassword/OpModels.cs`, `OpJson.cs`, `ItemMapper.cs`, `OpClient.cs`, `src/SynRdpManager.Core/Platform/Interfaces.cs`, `src/SynRdpManager.Core/Platform/ExeLocator.cs`
- Test: `tests/SynRdpManager.Core.Tests/OnePassword/OpJsonTests.cs`, `ItemMapperTests.cs`, `OpClientTests.cs`, `Platform/ExeLocatorTests.cs`, `Fixtures/account-list.json`, `item-list.json`, `item-get-batch.json` (3 concatenated objects as `op item get -` prints them), `item-get-server.json`

**Interfaces:**
- Consumes: `HostParser`, `ServerEntry`, `ConnectionKind`, `RdpCredentials` (Task 2)
- Produces:
  - `record OpAccount(string AccountUuid, string Email, string Url)`
  - `record OpItemSummary(string Id, string Title, string VaultId, string VaultName)`
  - `record OpField(string Label, string? Purpose, string? Value)`; `record OpItem(string Id, string Title, string VaultId, string VaultName, IReadOnlyList<OpField> Fields, IReadOnlyList<string> Urls)`
  - `static class OpJson { IReadOnlyList<OpAccount> ParseAccounts(string); IReadOnlyList<OpItemSummary> ParseItemList(string); IReadOnlyList<OpItem> ParseItems(string) /* one object, an array, or concatenated objects */ }`
  - `static class ItemMapper { ServerEntry ToEntry(OpAccount acc, OpItem item, ConnectionKind kind); string? ComposeUserName(OpItem item); string? GetPassword(OpItem item) }`
  - `interface IProcessRunner { Task<ProcessResult> RunAsync(string exe, IReadOnlyList<string> args, string? stdin, TimeSpan timeout, CancellationToken ct); }`, `record ProcessResult(int ExitCode, string StdOut, string StdErr, bool TimedOut)` (`ToString` must not include StdOut)
  - `enum OpErrorKind { NotFound, IntegrationDisabled, Locked, NotSignedIn, TimedOut, Other }`, `class OpException(OpErrorKind Kind, string UserMessage) : Exception`, `static OpErrorKind OpErrorClassifier.Classify(ProcessResult r)`
  - `class OpClient(string opPath, IProcessRunner runner)` with `Task<IReadOnlyList<OpAccount>> ListAccountsAsync(CancellationToken)`, `Task<IReadOnlyList<OpItem>> GetItemsByTagAsync(OpAccount account, string tag, CancellationToken)` (list + one batched `item get -` call via stdin), `Task<OpItem> GetItemAsync(OpAccount account, string vaultId, string itemId, CancellationToken)`; timeout `TimeSpan.FromSeconds(120)` per call; always `--format json` and `--account <AccountUuid>`
  - `static string? ExeLocator.Find(string exeName, string? configured, IEnumerable<string> candidateDirs, string? pathEnv, Func<string,bool> exists)`

- [ ] **Step 1: Fixtures.** Write them in op 2.x shape (`id`, `title`, `vault: {id,name}`, `urls: [{href, primary}]`, `fields: [{id,type,purpose,label,value}]`, purposes `USERNAME`/`PASSWORD`). Use fake data only (Customer A, `server01.customer-a.local`, password `fake-pass-1`).
- [ ] **Step 2: Failing tests**
  - `ParseItems_handles_concatenated_objects` → 3 items from `item-get-batch.json`; `ParseItems_handles_single_object_and_array`.
  - `ToEntry_prefers_hostname_field_over_url` (custom field label `Hostname` beats url) ; `ToEntry_falls_back_to_primary_url_then_first_url`; `ToEntry_without_host_has_null_Address`.
  - `ComposeUserName_prefixes_domain` (`domain`=`CORP`, user `svc` → `CORP\svc`); `..._keeps_backslash_or_at` (`a\b`, `a@b` unchanged); `GetPassword_reads_PASSWORD_purpose`.
  - `Classify_*` with a fake `IProcessRunner`: stderr containing `No accounts configured` → `NotSignedIn`; `authorization prompt dismissed` → `Locked`; `connecting to desktop app` → `IntegrationDisabled`; `TimedOut=true` → `TimedOut`; else `Other`. `Timeout_maps_to_TimedOut` via `OpClient` throws `OpException{Kind=TimedOut}`.
  - `GetItemsByTag_uses_single_batched_get` — fake runner records calls: exactly 2 calls, second has args `item get - --format json --account <uuid>` and stdin = the list JSON.
  - `Exception_messages_never_contain_stdout` — runner returns stdout containing `fake-pass-1` and exit 1; `OpException.ToString()` does not contain it.
  - `ExeLocator_order`: configured (exists) wins; then candidate dirs in order; then each `PATH` entry; returns null when none exist.
- [ ] **Step 3: Run** `dotnet test --filter OnePassword|ExeLocator` → FAIL.
- [ ] **Step 4: Implement.** Concatenated-object parsing: `Utf8JsonReader` with `SupportMultipleContent = true`. `OpException.UserMessage` per kind (Dutch-free plain English; e.g. Locked → "1Password is locked or the request was not approved. Unlock 1Password and press Refresh."). Missing exe → throw `OpException(NotFound, …)` before running.
- [ ] **Step 5: Run** → PASS. **Step 6: Commit + push** (bump patch).

---

### Task 4: Settings layering

**Files:**
- Create: `src/SynRdpManager.Core/Settings/AppSettings.cs`, `SettingsLoader.cs`
- Test: `tests/SynRdpManager.Core.Tests/Settings/SettingsLoaderTests.cs`

**Interfaces:**
- Produces:
  - `enum LaunchMode { Auto, Clipboard }`
  - `record AppSettings { string Tag; string PuttyTag; string? OpPath; string? PuttyPath; string PuttyTemplateSession; IReadOnlyList<string> Accounts; LaunchMode LaunchMode; int CredentialCleanupSeconds; }` with the Global Constraints defaults; `Accounts` matches account UUID or email, case-insensitive; empty = all.
  - `static class SettingsLoader { SettingsLoadResult Load(string? teamJson, string? userJson); string SerializeUserOverrides(AppSettings edited, AppSettings teamDefaults); }`, `record SettingsLoadResult(AppSettings Effective, AppSettings TeamDefaults, IReadOnlyList<string> Warnings)`

- [ ] **Step 1: Failing tests:** `Defaults_when_no_files`; `Team_file_overrides_defaults`; `User_file_overrides_team_per_key` (team sets `Tag`+`PuttyTag`, user sets only `Tag` → PuttyTag from team); `Invalid_json_is_ignored_with_warning`; `Unknown_keys_are_ignored`; `CredentialCleanupSeconds_is_clamped_to_5_120`; `Blank_tag_falls_back_to_default`; `Serialize_writes_only_keys_differing_from_team` (edited == team except Tag → JSON has only `Tag`); enum serialized as string (`"Clipboard"`).
- [ ] **Step 2: Run** → FAIL. **Step 3: Implement** with `System.Text.Json` (`JsonNode` merge, case-insensitive property names). **Step 4: Run** → PASS. **Step 5: Commit + push** (bump patch).

---

### Task 5: Catalog refresh, grouping/search, encrypted cache

**Files:**
- Create: `src/SynRdpManager.Core/Catalog/CatalogService.cs`, `CatalogView.cs`, `CacheCodec.cs`
- Modify: `src/SynRdpManager.Core/Platform/Interfaces.cs` (add `IDataProtector`)
- Test: `tests/SynRdpManager.Core.Tests/Catalog/CatalogServiceTests.cs`, `CatalogViewTests.cs`, `CacheCodecTests.cs`

**Interfaces:**
- Consumes: `OpClient`, `OpAccount`, `ItemMapper` (Task 3), `AppSettings` (Task 4)
- Produces:
  - `record RefreshResult(IReadOnlyList<ServerEntry> Entries, IReadOnlyDictionary<string, string?> SshUserNames /* key ItemId; transient, never cached */, IReadOnlyList<string> Warnings)`
  - `class CatalogService(OpClient op)` → `Task<RefreshResult> RefreshAsync(AppSettings s, CancellationToken ct)`; also `Task<(OpAccount, OpItem)> GetLiveItemAsync(ServerEntry e, CancellationToken ct)` for connect-time reads
  - `record VaultGroup(string DisplayName, string AccountId, string VaultId, IReadOnlyList<ServerEntry> Entries)`; `static IReadOnlyList<VaultGroup> CatalogView.Group(IEnumerable<ServerEntry> entries, string? search)`
  - `interface IDataProtector { byte[] Protect(byte[] data); byte[] Unprotect(byte[] data); }` (in `Platform/Interfaces.cs`)
  - `record CacheSnapshot(int SchemaVersion, DateTimeOffset SavedAt, string Tag, string PuttyTag, IReadOnlyList<ServerEntry> Entries)`; `static class CacheCodec { const int SchemaVersion = 1; byte[] Encode(CacheSnapshot s, IDataProtector p); CacheSnapshot? Decode(byte[] data, IDataProtector p); CacheSnapshot? DecodeFor(byte[] data, IDataProtector p, AppSettings s) /* null if tags differ */ }`

- [ ] **Step 1: Failing tests**
  - `Refresh_queries_each_account_for_both_tags` (2 accounts × 2 tags → entries with right `Kind`, `AccountName` = email).
  - `Refresh_respects_Accounts_filter` (by email, case-insensitive).
  - `Refresh_one_account_failing_adds_warning_keeps_others`; `Refresh_all_failing_throws_first_OpException`.
  - `Refresh_collects_ssh_usernames_only_for_ssh_items`.
  - `Group_sorts_vaults_and_titles_case_insensitively`; `Group_search_matches_title_vault_or_host_case_insensitive`; `Group_same_vault_name_in_two_accounts_gets_account_suffix` (`"Servers (a@x.nl)"`); `Group_drops_empty_groups_after_search`.
  - `Cache_roundtrip` with a fake XOR protector; `Cache_contains_no_usernames_or_passwords` (encode with identity protector, assert bytes as UTF-8 don't contain the fixture username/password); `Decode_returns_null_on_garbage`; `Decode_returns_null_on_other_schema`; `Snapshot_for_other_tag_is_ignored`; `Decode_returns_null_when_Unprotect_throws`.
- [ ] **Step 2: Run** → FAIL. **Step 3: Implement** (accounts refreshed in parallel with `Task.WhenAll`; JSON for the snapshot). **Step 4: Run** → PASS. **Step 5: Commit + push** (bump patch).

---

### Task 6: PuTTY session rules

**Files:**
- Create: `src/SynRdpManager.Core/Putty/PuttyNames.cs`, `PuttySessionManager.cs`
- Test: `tests/SynRdpManager.Core.Tests/Putty/PuttyNamesTests.cs`, `PuttySessionManagerTests.cs`, `Putty/InMemoryPuttyStore.cs`

**Interfaces:**
- Consumes: `ServerEntry`, `HostAddress.SshPort` (Task 2)
- Produces:
  - `interface IPuttySessionStore { bool Exists(string name); IReadOnlyDictionary<string, object> Read(string name); void Write(string name, IReadOnlyDictionary<string, object> values); }` — values are `string` (REG_SZ) or `int` (REG_DWORD); `Write` upserts and never deletes other values; names are *unescaped* (the store escapes).
  - `static class PuttyNames { string Escape(string name); string BaseNameFor(ServerEntry e) /* "<VaultName> - <Title>" */; IReadOnlyDictionary<string,string> AssignNames(IEnumerable<ServerEntry> entries, string templateName) /* ItemId -> unique name */ }`
  - `record PuttySyncResult(bool TemplateMissing, int Created, int Updated)`
  - `class PuttySessionManager(IPuttySessionStore store, string templateName)` with `bool TemplateExists()`, `void CreateTemplateFromDefaults()` (copies `"Default Settings"`; if absent writes `{ Protocol="ssh", PortNumber=22 }`), `PuttySyncResult Sync(IReadOnlyList<ServerEntry> sshEntries, IReadOnlyDictionary<string,string?> userNames)`, `void ReloadFromTemplate(ServerEntry e, string? userName)`, `string SessionNameFor(ServerEntry e)` (uses the last `AssignNames` result; falls back to `BaseNameFor`)

- [ ] **Step 1: Failing tests**
  - `Escape_matches_putty_rules`: PuTTY escapes `*`, `?`, `%`, `\`, chars `< 0x20` or `> 0x7E`, and a leading `.`, as `%XX` uppercase hex of each UTF-8 byte; space and `/` are kept. Cases: `"A - B"`→`"A - B"`; `"a\\b*c"`→`"a%5Cb%2Ac"`; `".x"`→`"%2Ex"`; `"50%"`→`"50%25"`; `"é"`→`"%C3%A9"`.
  - `Duplicate_names_get_suffix`: two entries with same vault+title → `"V - T"` and `"V - T (2)"` (order by ItemId); entry titled so base name equals the template name or `Default Settings` → gets `" (2)"`.
  - `Sync_does_nothing_without_template` → `TemplateMissing=true`, store unchanged.
  - `Sync_creates_missing_session_from_template_plus_host_port_user_protocol` (template has `TerminalType="xterm-256color"`, `FontHeight=12` → copied; `HostName`, `PortNumber`, `Protocol="ssh"`, `UserName` set).
  - `Sync_existing_session_only_updates_host_port_user` (session has user-edited `FontHeight=14` → stays 14; changed host is updated); `Sync_null_username_writes_empty_UserName`.
  - `ReloadFromTemplate_overwrites_all_but_keeps_host_and_port_and_sets_user`.
  - `CreateTemplateFromDefaults_copies_Default_Settings` and `..._without_Default_Settings_writes_minimal`.
  - `Entries_without_address_are_skipped`.
- [ ] **Step 2: Run** → FAIL. **Step 3: Implement.** **Step 4: Run** → PASS. **Step 5: Commit + push** (bump patch).

---

### Task 7: RDP launcher

**Files:**
- Create: `src/SynRdpManager.Core/Rdp/RdpLauncher.cs`
- Modify: `src/SynRdpManager.Core/Platform/Interfaces.cs`
- Test: `tests/SynRdpManager.Core.Tests/Rdp/RdpLauncherTests.cs` (uses `FakeTimeProvider` from `Microsoft.Extensions.TimeProvider.Testing`)

**Interfaces:**
- Consumes: `ServerEntry`, `HostAddress`, `RdpCredentials` (Task 2), `LaunchMode` (Task 4)
- Produces (interfaces in `Platform/Interfaces.cs`):
  - `record StoredCredential(string Target, string UserName, string Secret, string? Comment)` (`ToString` hides `Secret`)
  - `interface ICredentialStore { StoredCredential? Read(string target); void Write(StoredCredential c); void Delete(string target); IReadOnlyList<string> TargetsWithComment(string comment); }`
  - `interface IProcessLauncher { void Start(string exe, IReadOnlyList<string> args); }`
  - `interface IClipboardService { void SetSecret(string text); void ClearIfEquals(string text); }`
  - `class RdpLauncher(ICredentialStore creds, IProcessLauncher launcher, IClipboardService clipboard, TimeProvider time, string mstscExe = "mstsc.exe")` with `const string Comment = "SynRdpManager"`, `Task ConnectAsync(ServerEntry e, RdpCredentials? c, LaunchMode mode, TimeSpan cleanupDelay, CancellationToken ct)`, `void CleanupLeftovers()`, `void CleanupPendingNow()`

- [ ] **Step 1: Failing tests**
  - `Auto_writes_credential_then_starts_mstsc_then_deletes_after_delay`: target `TERMSRV/server01`, comment `SynRdpManager`, `mstsc.exe /v:server01:3390`; advance fake time 14 s → still present; 15 s → deleted.
  - `Auto_backs_up_and_restores_existing_foreign_credential`; `Existing_credential_with_our_comment_is_not_backed_up`.
  - `Null_credentials_starts_mstsc_without_writing`.
  - `Clipboard_mode_sets_clipboard_and_clears_after_30s`, writes no credential.
  - `Password_never_in_process_args` (assert no launcher arg contains the password, both modes).
  - `CleanupLeftovers_deletes_all_targets_with_our_comment`; `CleanupPendingNow_deletes_and_restores_immediately`.
  - `Entry_without_address_throws_InvalidOperationException`.
- [ ] **Step 2: Run** → FAIL. **Step 3: Implement.** **Step 4: Run** → PASS. **Step 5: Commit + push** (bump patch).

---

### Task 8: Windows platform implementations

**Files:**
- Create in `src/SynRdpManager.App/Platform/`: `ProcessRunner.cs` (`IProcessRunner`), `ProcessLauncher.cs` (`IProcessLauncher`), `DpapiProtector.cs` (`IDataProtector`), `WindowsCredentialStore.cs` (`ICredentialStore`), `RegistryPuttySessionStore.cs` (`IPuttySessionStore`), `WindowsClipboardService.cs` (`IClipboardService`), `PageantDetector.cs` (`static bool IsRunning()`), `AppPaths.cs` (cache/settings/team paths)

**Interfaces:**
- Consumes: all interfaces from Tasks 3, 5, 6, 7; `PuttyNames.Escape` (Task 6)

- [ ] **Step 1: Implement each** (compile-only on Linux; behaviour verified in Task 10's AVD smoke test):
  - `ProcessRunner`: `ProcessStartInfo` with `ArgumentList`, redirected stdin/stdout/stderr, `CreateNoWindow`, UTF-8; kill process tree on timeout → `TimedOut=true`.
  - `DpapiProtector`: `ProtectedData.Protect/Unprotect`, `DataProtectionScope.CurrentUser`, entropy `"SynRdpManager.cache.v1"` bytes.
  - `WindowsCredentialStore`: P/Invoke `CredWriteW`, `CredReadW`, `CredDeleteW`, `CredEnumerateW("TERMSRV/*")`, `CredFree`; `CRED_TYPE_GENERIC`; `Persist = CRED_PERSIST_SESSION (1)`; secret as UTF-16 bytes, zeroed after the call.
  - `RegistryPuttySessionStore`: `HKCU\Software\SimonTatham\PuTTY\Sessions\` + `PuttyNames.Escape(name)`; `string`→`REG_SZ`, `int`→`REG_DWORD`.
  - `WindowsClipboardService`: WPF `DataObject` with `UnicodeText` plus formats `ExcludeClipboardContentFromMonitorProcessing` (empty), `CanIncludeInClipboardHistory` and `CanUploadToCloudClipboard` (4-byte `0`).
  - `PageantDetector`: `Process.GetProcessesByName("pageant").Length > 0`.
- [ ] **Step 2: Build** `dotnet build src/SynRdpManager.App -c Release` → 0 warnings. **Step 3: Commit + push** (bump patch).

---

### Task 9: WPF UI

**Files:**
- Create/Modify in `src/SynRdpManager.App/`: `App.xaml(.cs)` (composition root, startup `CleanupLeftovers`, exit `CleanupPendingNow`), `ViewModels/MainViewModel.cs`, `ViewModels/GroupViewModel.cs`, `ViewModels/EntryViewModel.cs`, `ViewModels/SettingsViewModel.cs`, `Views/MainWindow.xaml(.cs)`, `Views/SettingsWindow.xaml(.cs)`, `Resources/app.ico`
- Add package: `CommunityToolkit.Mvvm`

**Interfaces:**
- Consumes: everything above.

- [ ] **Step 1: Main window** as the spec's sketch: search box, Refresh, Settings, "Edit PuTTY template" toolbar button; `TreeView` of `VaultGroup` → entries (🖥 Rdp / 🐧 Ssh, title, host; no-address entries greyed with "no hostname"); status bar `Online · refreshed HH:mm` / `Offline · cached list from d MMM HH:mm` / "Waiting for 1Password approval…" and version from assembly.
- [ ] **Step 2: Startup flow:** load settings → decode cache via `CacheCodec.DecodeFor` → show → background `RefreshAsync` → on success save cache, run `PuttySessionManager.Sync`; if PuTTY entries exist and `TemplateMissing`, show a one-time dialog "Create PuTTY template now?" → `CreateTemplateFromDefaults` + `putty.exe -load "<template>"`; on `OpException` show `UserMessage` in a dismissible banner with the fix (CLI integration setting, unlock, install path). Zero entries after refresh → banner `No items tagged "<Tag>" or "<PuttyTag>" found — tag your server items in 1Password.`
- [ ] **Step 3: Actions:** double-click/Enter → Rdp: `GetLiveItemAsync` → `ItemMapper.ComposeUserName/GetPassword` → `RdpLauncher.ConnectAsync` (on `OpException` connect without credentials + banner); Ssh: if template missing explain, else `putty.exe -load "<SessionNameFor>"`; warn (banner) if `!PageantDetector.IsRunning()`. Context menu: Connect, Copy hostname, Copy username (live fetch), and for Ssh "Reload from template", "Edit in PuTTY"; on vault node "Reload all from template".
- [ ] **Step 4: Settings window** edits all `AppSettings` keys; Save → `SettingsLoader.SerializeUserOverrides` → user file; tag change triggers refresh.
- [ ] **Step 5: Build** → 0 warnings; `dotnet test` still green. **Step 6: Commit + push** (bump patch).

---

### Task 10: README setup guide, release package, AVD smoke test

**Files:**
- Modify: `README.md`
- Create: `settings.sample.json` (all keys with defaults, comments-free JSON), `docs/SMOKE-TEST.md`
- Modify: `.github/workflows/build.yml` (include `settings.sample.json` in zip, now required)

- [ ] **Step 1: README** (English): what it does; **AVD admin setup** (download op ZIP from developer.1password.com → copy `op.exe` to `C:\Program Files\1Password CLI\` → add to system PATH → users sign out/in; *don't* use `winget --scope machine`, normal users get "Access is denied"); **per-user setup** (unlock 1Password → Settings → Developer → "Integrate with 1Password CLI"; Pageant running with key); **tagging items** (tags, hostname rules incl. `hostname`/`domain` custom fields, username); **settings** table; **PuTTY template flow**; troubleshooting table (the errors from Task 3's `UserMessage`s); download link to GitHub Releases.
- [ ] **Step 2: `docs/SMOKE-TEST.md`** checklist for the AVD: app starts (single-file extraction not blocked); unlock prompt appears once; list matches tagged items; RDP double-click logs in **with `CRED_PERSIST_SESSION`** (if it prompts for a password → switch `Persist` to `CRED_PERSIST_LOCAL_MACHINE (2)` and note it in the spec); credential gone after 15 s (`cmdkey /list | findstr TERMSRV`); clipboard mode excluded from Win+V history; template dialog → PuTTY opens template; session created with host/user; user edit survives refresh; reload-from-template keeps host; offline (quit 1Password) shows cached list.
- [ ] **Step 3: Push, confirm CI artifact zip contains `SynRdpManager.exe` + `settings.sample.json`.** **Step 4: Commit + push** (bump patch). Then hand the zip to the user for the smoke test.
