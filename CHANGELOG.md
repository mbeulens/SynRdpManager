# Changelog

All notable changes to SynRdpManager. Versions follow `MAJOR.MINOR.PATCH`; any version
component equal to 13 is skipped.

## [0.2.0] — 2026-11-24

First release.

### Added
- **Server list from 1Password.** Shows every item tagged `MstscDealer` (RDP) and
  `PuttyDealer` (SSH) across all signed-in 1Password accounts, grouped per vault, with search.
  Uses the 1Password CLI (`op.exe`) with desktop-app integration; no tokens or config files.
- **One-click RDP with automatic login.** Writes a temporary Windows credential
  `TERMSRV/<host>` (session-only), starts `mstsc /v:<host[:port]>`, removes it after 15 s
  and restores any credential you already had (including its original persistence).
  Optional clipboard mode, kept out of clipboard history and cloud sync.
- **PuTTY sessions from a template.** Create a template session once; every SSH server gets
  a real PuTTY saved session with host, port and auto-login username from 1Password. Your
  own edits are preserved; "Reload from template" per server or per customer.
- **Offline mode.** The last list is cached encrypted (DPAPI, no usernames or passwords) and
  shown when 1Password is unavailable.
- **Settings** per installation: tags, PuTTY template name, accounts filter, launch mode,
  cleanup delay — team defaults in `%LocalAppData%\SynRdpManager\settings.json`, personal
  overrides in `%APPDATA%\SynRdpManager\settings.json`.
- **Install & auto-update** with Velopack: per-user `SynRdpManager-win-Setup.exe` (no admin),
  updates from GitHub Releases in the background with a "Restart now" banner, and
  ⚙ ▾ → Check for updates.
- Friendly errors for the common setup problems (op.exe missing or blocked, CLI integration
  off, 1Password locked, approval dismissed, no tagged items, Pageant not running).
- README with AVD admin/user setup and `docs/SMOKE-TEST.md` checklist.
