using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SynRdpManager.App.Platform;
using SynRdpManager.App.Services;
using SynRdpManager.Core.Catalog;
using SynRdpManager.Core.Models;
using SynRdpManager.Core.OnePassword;
using SynRdpManager.Core.Settings;

namespace SynRdpManager.App.ViewModels;

/// <summary>
/// Main window state: list, search, status, banner, refresh, offline cache, settings and the PuTTY template.
/// Server actions (connect, copy, reload) live in MainViewModel.Actions.cs.
/// All methods run on the UI thread; op calls are awaited off it via Task.Run.
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    private const string WaitingText = "Waiting for 1Password approval…";
    private const string OpNotFoundMessage =
        "1Password CLI (op.exe) was not found. Install it or set its path in Settings.";
    private const string OpUnreadableMessage = "1Password returned data SynRdpManager could not read.";
    private const string PuttyNotFoundMessage =
        "PuTTY (putty.exe) was not found. Install it or set its path in Settings.";

    private readonly AppServices _services;
    private readonly IDialogService _dialogs;
    private readonly UpdateService _updates;
    private readonly HashSet<(string AccountId, string VaultId)> _collapsed = [];

    private IReadOnlyList<ServerEntry> _entries = [];
    private DateTimeOffset? _refreshedAt; // last successful refresh (online)
    private DateTimeOffset? _cachedAt;    // saved-at of the cache currently shown (offline)
    private bool _refreshRunning;
    private bool _refreshAgain;
    private int _liveFetches;
    private bool _templatePromptShown;

    private string _searchText = "";
    private string? _bannerText;
    private bool _bannerIsWarning;
    private string? _bannerActionText;
    private IRelayCommand? _bannerActionCommand;

    public MainViewModel(AppServices services, IDialogService dialogs, UpdateService updates)
    {
        _services = services;
        _dialogs = dialogs;
        _updates = updates;
        VersionText = "v" + (typeof(MainViewModel).Assembly.GetName().Version?.ToString(3) ?? "?");

        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !_refreshRunning);
        SettingsCommand = new RelayCommand(OpenSettings);
        EditTemplateCommand = new RelayCommand(EditTemplate);
        DismissBannerCommand = new RelayCommand(() => ShowBanner(null, warning: false));
        CheckForUpdatesCommand = new AsyncRelayCommand(_updates.CheckNowAsync);
        RestartToUpdateCommand = new RelayCommand(RestartToUpdate);

        var concurrent = AsyncRelayCommandOptions.AllowConcurrentExecutions;
        ConnectCommand = new AsyncRelayCommand<EntryViewModel>(ConnectAsync, concurrent);
        CopyHostnameCommand = new RelayCommand<EntryViewModel>(CopyHostname);
        CopyUsernameCommand = new AsyncRelayCommand<EntryViewModel>(CopyUsernameAsync, concurrent);
        ReloadFromTemplateCommand = new AsyncRelayCommand<EntryViewModel>(ReloadFromTemplateAsync, concurrent);
        EditInPuttyCommand = new AsyncRelayCommand<EntryViewModel>(EditInPuttyAsync, concurrent);
        ReloadAllCommand = new AsyncRelayCommand<GroupViewModel>(ReloadAllAsync, concurrent);
    }

    public ObservableCollection<GroupViewModel> Groups { get; } = [];

    public IAsyncRelayCommand RefreshCommand { get; }
    public IRelayCommand SettingsCommand { get; }
    public IRelayCommand EditTemplateCommand { get; }
    public IRelayCommand DismissBannerCommand { get; }
    public IAsyncRelayCommand CheckForUpdatesCommand { get; }
    public IRelayCommand RestartToUpdateCommand { get; }
    public IAsyncRelayCommand<EntryViewModel> ConnectCommand { get; }
    public IRelayCommand<EntryViewModel> CopyHostnameCommand { get; }
    public IAsyncRelayCommand<EntryViewModel> CopyUsernameCommand { get; }
    public IAsyncRelayCommand<EntryViewModel> ReloadFromTemplateCommand { get; }
    public IAsyncRelayCommand<EntryViewModel> EditInPuttyCommand { get; }
    public IAsyncRelayCommand<GroupViewModel> ReloadAllCommand { get; }

    public string VersionText { get; }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value ?? "")) RebuildGroups();
        }
    }

    public string? BannerText
    {
        get => _bannerText;
        private set
        {
            if (SetProperty(ref _bannerText, value)) OnPropertyChanged(nameof(HasBanner));
        }
    }

    public bool BannerIsWarning
    {
        get => _bannerIsWarning;
        private set => SetProperty(ref _bannerIsWarning, value);
    }

    public bool HasBanner => !string.IsNullOrEmpty(BannerText);

    /// <summary>Label of the banner's optional action button; null = no button.</summary>
    public string? BannerActionText
    {
        get => _bannerActionText;
        private set
        {
            if (SetProperty(ref _bannerActionText, value)) OnPropertyChanged(nameof(HasBannerAction));
        }
    }

    public IRelayCommand? BannerActionCommand
    {
        get => _bannerActionCommand;
        private set => SetProperty(ref _bannerActionCommand, value);
    }

    public bool HasBannerAction => BannerActionText is not null;

    public bool IsOnline => _refreshedAt is not null;

    public string StatusText
    {
        get
        {
            if (_refreshRunning || _liveFetches > 0) return WaitingText;
            if (_refreshedAt is { } r)
                return "Online · refreshed " + r.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture);
            if (_cachedAt is { } c)
                return "Offline · cached list from " + c.ToLocalTime().ToString("d MMM HH:mm", CultureInfo.InvariantCulture);
            return "Offline";
        }
    }

    /// <summary>Called once after the window is shown: shows warnings and the cache, then refreshes in the background.</summary>
    public void Start(IReadOnlyList<string> startupWarnings)
    {
        var warnings = startupWarnings.Concat(_services.Load.Warnings).ToList();
        if (warnings.Count > 0) ShowBanner(string.Join(Environment.NewLine, warnings), warning: true);
        LoadCache();
        RebuildGroups();
        NotifyStatus();
        _ = RefreshAsync();
    }

    /// <summary>Replaces the banner (null hides it); the action button is shown only when both action arguments are given.</summary>
    public void ShowBanner(string? message, bool warning, string? actionText = null, IRelayCommand? actionCommand = null)
    {
        BannerIsWarning = warning;
        BannerActionCommand = actionCommand;
        BannerActionText = actionCommand is null ? null : actionText;
        BannerText = message;
    }

    /// <summary>Update banners from <see cref="UpdateService"/>: offers "Restart now" once an update is downloaded.</summary>
    public void ShowUpdateBanner(string message)
    {
        if (_updates.IsUpdateReady) ShowBanner(message, warning: false, "Restart now", RestartToUpdateCommand);
        else ShowBanner(message, warning: false);
    }

    private void RestartToUpdate()
    {
        try
        {
            _updates.RestartToUpdate();
        }
        catch (Exception ex)
        {
            ShowBanner("Could not restart to update: " + ex.Message, warning: true);
        }
    }

    // ---------- Refresh ----------

    /// <summary>Never throws. A request while a refresh is running is queued (runs once afterwards).</summary>
    public async Task RefreshAsync()
    {
        if (_refreshRunning)
        {
            _refreshAgain = true;
            return;
        }

        _refreshRunning = true;
        NotifyStatus();
        try
        {
            do
            {
                _refreshAgain = false;
                await RefreshOnceAsync();
            } while (_refreshAgain);
        }
        finally
        {
            _refreshRunning = false;
            NotifyStatus();
        }
    }

    private async Task RefreshOnceAsync()
    {
        var settings = _services.Settings;
        // op.exe may have been installed while the app was open: look it up again before giving up.
        if (_services.Catalog is null) _services.Apply(_services.Load);
        var catalog = _services.Catalog;
        if (catalog is null)
        {
            RefreshFailed(OpNotFoundMessage);
            return;
        }

        RefreshResult result;
        try
        {
            result = await Task.Run(() => catalog.RefreshAsync(settings, CancellationToken.None));
        }
        catch (OpException ex)
        {
            RefreshFailed(ex.UserMessage);
            return;
        }
        catch (JsonException)
        {
            RefreshFailed(OpUnreadableMessage);
            return;
        }
        catch (Exception ex)
        {
            RefreshFailed("Refresh failed: " + ex.Message);
            return;
        }

        // Settings changed while op was running: discard the stale result and refresh again with the new ones.
        if (!ReferenceEquals(settings, _services.Settings))
        {
            _refreshAgain = true;
            return;
        }

        _entries = result.Entries;
        _refreshedAt = DateTimeOffset.Now;
        _cachedAt = null;
        RebuildGroups();
        // A partial result (some account failed) must not replace a complete offline cache.
        if (result.Warnings.Count == 0) SaveCache(settings);

        var messages = new List<string>(result.Warnings);
        if (result.Entries.Count == 0)
            messages.Add($"No items tagged \"{settings.Tag}\" or \"{settings.PuttyTag}\" found — tag your server items in 1Password.");
        if (messages.Count > 0) ShowBanner(string.Join(Environment.NewLine, messages), warning: true);

        SyncPutty(result);
    }

    private void RefreshFailed(string message)
    {
        GoOffline();
        ShowBanner(message, warning: true);
    }

    // The list shown is no longer live: present it as the cached list from the last good refresh.
    private void GoOffline()
    {
        _cachedAt = _refreshedAt ?? _cachedAt;
        _refreshedAt = null;
        NotifyStatus();
    }

    private void SyncPutty(RefreshResult result)
    {
        _services.ResetPutty();
        var ssh = result.Entries.Where(e => e.Kind == ConnectionKind.Ssh).ToList();
        if (ssh.Count == 0) return;
        try
        {
            var sync = _services.Putty.Sync(ssh, result.SshUserNames);
            if (!sync.TemplateMissing || _templatePromptShown) return;

            _templatePromptShown = true;
            if (!_dialogs.Confirm("Create PuTTY template now?", TemplatePromptText())) return;
            _services.Putty.CreateTemplateFromDefaults();
            OpenTemplateInPutty();
            _services.Putty.Sync(ssh, result.SshUserNames);
        }
        catch (Exception ex)
        {
            ShowBanner("Could not update the PuTTY sessions: " + ex.Message, warning: true);
        }
    }

    // ---------- Offline cache ----------

    private void LoadCache()
    {
        try
        {
            if (!File.Exists(AppPaths.CacheFile)) return;
            var snap = CacheCodec.DecodeFor(File.ReadAllBytes(AppPaths.CacheFile), _services.Protector, _services.Settings);
            if (snap is null) return;
            _entries = snap.Entries;
            _cachedAt = snap.SavedAt;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // No readable cache: start with an empty list.
        }
    }

    private void SaveCache(AppSettings s)
    {
        try
        {
            var bytes = CacheCodec.Encode(
                new CacheSnapshot(CacheCodec.SchemaVersion, DateTimeOffset.Now, s.Tag, s.PuttyTag, _entries),
                _services.Protector);
            var tmp = AppPaths.CacheFile + ".tmp";
            File.WriteAllBytes(tmp, bytes);
            File.Move(tmp, AppPaths.CacheFile, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
        {
            ShowBanner("Could not save the offline cache: " + ex.Message, warning: true);
        }
    }

    // ---------- List ----------

    private void RebuildGroups()
    {
        var searching = !string.IsNullOrWhiteSpace(SearchText);
        Groups.Clear();
        foreach (var g in CatalogView.Group(_entries, SearchText))
        {
            var key = (g.AccountId, g.VaultId);
            Groups.Add(new GroupViewModel(this, g, searching || !_collapsed.Contains(key), vm =>
            {
                if (!string.IsNullOrWhiteSpace(SearchText)) return; // remember only the unfiltered layout
                if (vm.IsExpanded) _collapsed.Remove(key);
                else _collapsed.Add(key);
            }));
        }
    }

    private void NotifyStatus()
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(IsOnline));
        RefreshCommand.NotifyCanExecuteChanged();
    }

    // ---------- Settings ----------

    private void OpenSettings()
    {
        var load = _services.Load;
        var editor = new SettingsViewModel(load.Effective);
        if (!_dialogs.EditSettings(editor) || editor.Result is not { } edited) return;

        try
        {
            AppPaths.EnsureDirectories();
            SettingsFiles.SaveUserOverrides(edited, load.TeamDefaults);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowBanner("Could not save the settings: " + ex.Message, warning: true);
            return;
        }

        var old = load.Effective;
        var reloaded = SettingsFiles.Load();
        _services.Apply(reloaded);
        if (reloaded.Warnings.Count > 0)
            ShowBanner(string.Join(Environment.NewLine, reloaded.Warnings), warning: true);

        var now = reloaded.Effective;
        // The list was fetched for the old tags: it must not be presented as Online for the new ones.
        if (now.Tag != old.Tag || now.PuttyTag != old.PuttyTag) GoOffline();
        var needsRefresh = now.Tag != old.Tag || now.PuttyTag != old.PuttyTag || now.OpPath != old.OpPath
            || !now.Accounts.SequenceEqual(old.Accounts);
        if (needsRefresh) _ = RefreshAsync();
    }

    // ---------- PuTTY template ----------

    private string TemplatePromptText()
    {
        var template = _services.Settings.PuttyTemplateSession;
        return $"SynRdpManager creates one PuTTY saved session per SSH server, copied from the template session \"{template}\". "
            + "That template does not exist yet." + Environment.NewLine + Environment.NewLine
            + $"Yes copies PuTTY's \"Default Settings\" to \"{template}\" and opens it in PuTTY, so you can adjust "
            + "keyboard, colours, SSH options etc. and click Save. Afterwards use \"Reload all from template\" "
            + "(right-click a customer) to apply your changes to its sessions." + Environment.NewLine + Environment.NewLine
            + "Create PuTTY template now?";
    }

    private string TemplateMissingText() =>
        $"The PuTTY template session \"{_services.Settings.PuttyTemplateSession}\" does not exist yet, so no PuTTY "
        + "sessions are generated. Create the template first with \"Edit PuTTY template\" in the toolbar.";

    private void EditTemplate()
    {
        try
        {
            if (!_services.Putty.TemplateExists())
            {
                if (!_dialogs.Confirm("Create PuTTY template now?", TemplatePromptText())) return;
                _services.Putty.CreateTemplateFromDefaults();
            }
        }
        catch (Exception ex)
        {
            ShowBanner("Could not create the PuTTY template: " + ex.Message, warning: true);
            return;
        }
        OpenTemplateInPutty();
    }

    private void OpenTemplateInPutty() => StartPutty(["-load", _services.Settings.PuttyTemplateSession]);

    private bool StartPutty(IReadOnlyList<string> args)
    {
        if (_services.PuttyPath is not { } putty)
        {
            ShowBanner(PuttyNotFoundMessage, warning: true);
            return false;
        }
        try
        {
            _services.Launcher.Start(putty, args);
            return true;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            ShowBanner("Could not start PuTTY: " + ex.Message, warning: true);
            return false;
        }
    }
}
