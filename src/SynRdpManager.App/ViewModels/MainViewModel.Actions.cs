using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using SynRdpManager.App.Platform;
using SynRdpManager.Core.Models;
using SynRdpManager.Core.OnePassword;
using SynRdpManager.Core.Putty;
using SynRdpManager.Core.Settings;

namespace SynRdpManager.App.ViewModels;

/// <summary>Server actions: connect (RDP / SSH), copy hostname / username, PuTTY reload and edit.</summary>
public sealed partial class MainViewModel
{
    private async Task ConnectAsync(EntryViewModel? vm)
    {
        if (vm is null) return;
        var e = vm.Entry;
        if (e.Address is null)
        {
            ShowNoHostname(e);
            return;
        }
        if (e.Kind == ConnectionKind.Rdp) await ConnectRdpAsync(e);
        else await ConnectSshAsync(e);
    }

    private async Task ConnectRdpAsync(ServerEntry e)
    {
        var s = _services.Settings;
        RdpCredentials? creds = null;
        string? notice = null;

        var (item, error) = await FetchLiveAsync(e);
        if (item is not null)
        {
            var user = ItemMapper.ComposeUserName(item);
            var password = ItemMapper.GetPassword(item);
            if (user is not null && password is not null) creds = new RdpCredentials(user, password);
            else notice = $"\"{e.Title}\" has no username or password in 1Password — log in manually.";
        }
        else
        {
            notice = error + " Connecting without saved credentials — log in manually.";
        }

        try
        {
            await _services.Rdp.ConnectAsync(e, creds, s.LaunchMode,
                TimeSpan.FromSeconds(s.CredentialCleanupSeconds), CancellationToken.None);
        }
        catch (Exception ex)
        {
            ShowBanner("Could not start Remote Desktop: " + ex.Message, warning: true);
            return;
        }

        if (notice is not null) ShowBanner(notice, warning: true);
        else if (s.LaunchMode == LaunchMode.Clipboard)
            ShowBanner("The password is on the clipboard for 30 seconds — paste it into the login prompt.", warning: false);
    }

    private async Task ConnectSshAsync(ServerEntry e)
    {
        if (!TemplateReady()) return;
        var name = await EnsureSessionAsync(e);
        if (name is null) return;
        if (!StartPutty(["-load", name])) return;
        if (!PageantDetector.IsRunning())
            ShowBanner("Pageant is not running — PuTTY may ask for a password or key.", warning: true);
    }

    private async Task EditInPuttyAsync(EntryViewModel? vm)
    {
        if (vm is null) return;
        var e = vm.Entry;
        if (e.Address is null)
        {
            ShowNoHostname(e);
            return;
        }
        if (!TemplateReady()) return;
        var name = await EnsureSessionAsync(e);
        if (name is null) return;
        // "-load" would connect straight away; without arguments PuTTY opens its configuration dialog.
        if (StartPutty([]))
            ShowBanner($"In PuTTY, select the saved session \"{name}\", click Load, make your changes and click Save.", warning: false);
    }

    private Task ReloadFromTemplateAsync(EntryViewModel? vm)
    {
        if (vm is null) return Task.CompletedTask;
        if (vm.Entry.Address is null)
        {
            ShowNoHostname(vm.Entry);
            return Task.CompletedTask;
        }
        return ReloadEntriesAsync([vm.Entry]);
    }

    private Task ReloadAllAsync(GroupViewModel? group)
    {
        if (group is null) return Task.CompletedTask;
        var ssh = group.Entries.Select(x => x.Entry)
            .Where(e => e.Kind == ConnectionKind.Ssh && e.Address is not null)
            .ToList();
        if (ssh.Count == 0)
        {
            ShowBanner($"\"{group.DisplayName}\" has no SSH servers with a hostname.", warning: false);
            return Task.CompletedTask;
        }
        return ReloadEntriesAsync(ssh);
    }

    private async Task ReloadEntriesAsync(IReadOnlyList<ServerEntry> entries)
    {
        var done = new List<string>();
        foreach (var e in entries)
        {
            if (!TemplateReady()) return;
            var (item, error) = await FetchLiveAsync(e);
            if (item is null)
            {
                ShowBanner(error!, warning: true);
                return;
            }
            var user = ItemMapper.ComposeUserName(item);
            var putty = _services.Putty;
            if (!TemplateReady()) return; // ReloadFromTemplate requires the template; re-check after the await
            try
            {
                putty.ReloadFromTemplate(e, user);
                done.Add(putty.SessionNameFor(e));
            }
            catch (Exception ex)
            {
                ShowBanner("Could not reload the PuTTY session: " + ex.Message, warning: true);
                return;
            }
        }
        ShowBanner(done.Count == 1
            ? $"Reloaded \"{done[0]}\" from the template."
            : $"Reloaded {done.Count} sessions from the template.", warning: false);
    }

    private void CopyHostname(EntryViewModel? vm)
    {
        if (vm is null) return;
        var e = vm.Entry;
        if (e.Address is null)
        {
            ShowNoHostname(e);
            return;
        }
        SetClipboardText(e.Kind == ConnectionKind.Rdp ? e.Address.MstscTarget : e.Address.Host, "Hostname copied.");
    }

    private async Task CopyUsernameAsync(EntryViewModel? vm)
    {
        if (vm is null) return;
        var (item, error) = await FetchLiveAsync(vm.Entry);
        if (item is null)
        {
            ShowBanner(error!, warning: true);
            return;
        }
        var user = ItemMapper.ComposeUserName(item);
        if (user is null) ShowBanner($"\"{vm.Entry.Title}\" has no username in 1Password.", warning: true);
        else SetClipboardText(user, "Username copied.");
    }

    // ---------- helpers ----------

    /// <summary>Fetches the item fresh from 1Password off the UI thread. Exactly one of the results is non-null.</summary>
    private async Task<(OpItem? Item, string? Error)> FetchLiveAsync(ServerEntry e)
    {
        var catalog = _services.Catalog;
        if (catalog is null) return (null, OpNotFoundMessage);

        _liveFetches++;
        NotifyStatus();
        try
        {
            var (_, item) = await Task.Run(() => catalog.GetLiveItemAsync(e, CancellationToken.None));
            return (item, null);
        }
        catch (OpException ex)
        {
            return (null, ex.UserMessage);
        }
        catch (JsonException)
        {
            return (null, OpUnreadableMessage);
        }
        catch (Exception ex)
        {
            return (null, "1Password request failed: " + ex.Message);
        }
        finally
        {
            _liveFetches--;
            NotifyStatus();
        }
    }

    /// <summary>Explains (dialog) and returns false when the PuTTY template session does not exist.</summary>
    private bool TemplateReady()
    {
        try
        {
            if (_services.Putty.TemplateExists()) return true;
        }
        catch (Exception ex)
        {
            ShowBanner("Could not read the PuTTY sessions: " + ex.Message, warning: true);
            return false;
        }
        _dialogs.Inform("PuTTY template missing", TemplateMissingText());
        return false;
    }

    /// <summary>Returns the saved-session name for the entry, creating the session from the template if it is missing.</summary>
    private async Task<string?> EnsureSessionAsync(ServerEntry e)
    {
        try
        {
            var name = _services.Putty.SessionNameFor(e);
            if (_services.PuttyStore.Exists(name)) return name;
        }
        catch (Exception ex)
        {
            ShowBanner("Could not read the PuTTY sessions: " + ex.Message, warning: true);
            return null;
        }

        var (item, error) = await FetchLiveAsync(e);
        var user = item is null ? null : ItemMapper.ComposeUserName(item);
        if (error is not null) ShowBanner(error + " The PuTTY session was created without a username.", warning: true);

        try
        {
            // A separate manager, so the full-list names of the main manager stay intact.
            var single = new PuttySessionManager(_services.PuttyStore, _services.Settings.PuttyTemplateSession);
            if (single.Sync([e], new Dictionary<string, string?> { [e.ItemId] = user }).TemplateMissing)
            {
                _dialogs.Inform("PuTTY template missing", TemplateMissingText());
                return null;
            }
            return single.SessionNameFor(e);
        }
        catch (Exception ex)
        {
            ShowBanner("Could not create the PuTTY session: " + ex.Message, warning: true);
            return null;
        }
    }

    private void SetClipboardText(string text, string confirmation)
    {
        try
        {
            Clipboard.SetText(text);
            ShowBanner(confirmation, warning: false);
        }
        catch (ExternalException)
        {
            ShowBanner("The clipboard is in use by another program — try again.", warning: true);
        }
    }

    private void ShowNoHostname(ServerEntry e) =>
        ShowBanner($"\"{e.Title}\" has no hostname — add a \"hostname\" field or a website to the item in 1Password.", warning: true);
}
