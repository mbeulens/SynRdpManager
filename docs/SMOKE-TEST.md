# AVD smoke test

Run on the shared Azure Virtual Desktop as a normal (non-admin) user, with the installer from the latest GitHub Release.
Prerequisites: README "AVD admin setup" and "Per-user setup" done; at least one item tagged `MstscDealer`
and (for 14-16) one tagged `PuttyDealer`, Pageant running with your key.
Mark each box; on failure note the step number.

## Start and list

- [ ] 1. Download `SynRdpManager-win-Setup.exe` from the latest GitHub Release and run it.
  Expected: installs per user without admin rights. Note any SmartScreen ("More info -> Run anyway") or AppLocker message and report its exact text.
- [ ] 2. Start SynRdpManager from the Start menu.
  Expected: main window opens; no error dialog.
- [ ] 3. Status bar shows the version.
  Expected: right side shows `v0.1.x` matching the installer version.
- [ ] 4. 1Password unlock/approval prompt on first refresh.
  Expected: popup appears (status "Waiting for 1Password approval…"), then `Online · refreshed HH:mm`.
  Note how many approval popups appeared (ideally one) and report the number.
- [ ] 5. List matches the tagged items.
  Expected: groups = vault names, items = titles, hostnames correct; RDP and SSH icons right; untagged items absent; item without hostname greyed "no hostname".

## RDP

- [ ] 6. Double-click an RDP server.
  Expected: mstsc opens and logs in with no password prompt (credential persisted as `CRED_PERSIST_SESSION`).
  If it prompts for a password: report it; a new build is needed (credential persistence switch).
- [ ] 7. Credential is removed after about 15 s: in a terminal run `cmdkey /list | findstr TERMSRV`.
  Expected: no `TERMSRV/<host>` entry with comment `SynRdpManager` after 15-20 s (a pre-existing credential of your own is back).
  If you had a pre-existing credential for that host: it keeps its original persistence — `cmdkey /list` must NOT say
  "Saved for this logon only" for it if it did not say so before the connect.
- [ ] 8. Domain item (`domain` field) logs in as `DOMAIN\user`.
  Expected: logged in; username shown correctly in the session.
- [ ] 9. Settings -> `LaunchMode` = Clipboard, double-click an RDP server.
  Expected: mstsc opens, banner says the password is on the clipboard for 30 s; Ctrl+V pastes it (clipboard formats accepted); after 30 s the clipboard is empty.
- [ ] 10. Clipboard mode and Win+V.
  Expected: the password does NOT appear in the Win+V clipboard history. Set `LaunchMode` back to Auto afterwards.
- [ ] 11. Right-click -> Copy hostname / Copy username.
  Expected: correct values on the clipboard (`host:port` if a port is set).

## Settings

- [ ] 12. Open Settings, type a value (for example Tag) and press Enter.
  Expected: window saves and closes; reopening shows the typed value (not the old one); changing the tag triggers a refresh.
- [ ] 13. Check `%APPDATA%\SynRdpManager\settings.json`.
  Expected: only changed keys are present.

## PuTTY / SSH

- [ ] 14. First SSH refresh with no template: "Create PuTTY template now?" -> Yes.
  Expected: PuTTY opens with `SynRdpManager-Template` loaded; change something (e.g. colours), click Save.
- [ ] 15. Double-click an SSH server.
  Expected: PuTTY opens the session `<Vault> - <Title>`, connects to host/port with the 1Password username, logs in via Pageant with no password prompt. Pageant stopped: banner warns "Pageant is not running".
- [ ] 16. Open PuTTY itself and look at the saved-session list.
  Expected: sessions named `<Vault> - <Title>` appear with spaces displayed correctly (not `%20`). A name with non-ASCII characters (if any, e.g. accents) displays correctly, not garbled; if garbled, report the exact name.
- [ ] 17. Edit a generated session in PuTTY (e.g. font), save, press Refresh in the app.
  Expected: your edit survives the refresh.
- [ ] 18. Change the template, then right-click a server -> Reload from template.
  Expected: template changes applied; session name, host and port unchanged. Same for vault -> Reload all from template.

## Offline

- [ ] 19. Quit 1Password, press Refresh (or restart the app).
  Expected: list still shown, status `Offline · cached list from d MMM HH:mm` (the time of the last successful refresh;
  never `Online` after a failed refresh), a banner explains the 1Password problem. Double-click an RDP server: mstsc opens without credentials and a notice says so.
- [ ] 20. Start 1Password again, unlock, press Refresh.
  Expected: back to Online.

- [ ] 21. After the *next* release is published, start the app.
  Expected: a banner "Version X is ready" appears; **Restart now** restarts the app and the status bar shows the new version.
  Team settings in `%LocalAppData%\SynRdpManager\settings.json` and your own `%APPDATA%\SynRdpManager\settings.json` survive the update.

## Reporting failures

Send Claude a screenshot of the window (including the red/yellow banner) and the exact message text, the step number,
and (for connection problems) the output of `cmdkey /list | findstr TERMSRV` and `op vault list`. Never paste passwords.
