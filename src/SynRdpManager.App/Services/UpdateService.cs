using Velopack;
using Velopack.Sources;

namespace SynRdpManager.App.Services;

/// <summary>
/// Checks GitHub Releases via Velopack, downloads an available update and offers a restart.
/// Velopack calls run on the thread pool; call the public methods from the UI thread
/// (<paramref name="showBanner"/> is invoked on the caller's context after each await).
/// Does nothing when the app was not installed with Setup.exe or the Velopack portable zip (e.g. dev build).
/// </summary>
public sealed class UpdateService(string repoUrl, Action<string> showBanner, Action beforeRestart)
{
    private const string NotInstalledMessage = "Updates are only available when SynRdpManager was installed with Setup.exe or the portable zip from GitHub Releases.";
    private const string FailedMessage = "Update check failed — try again later.";

    private Task<UpdateManager>? _managerTask;
    private UpdateManager? _manager;
    private VelopackAsset? _ready;
    private Task? _running; // the check/download in flight, shared by automatic and manual checks

    public bool IsUpdateReady => _ready is not null;

    /// <summary>Automatic check after startup: silent on any failure, shows a banner only when an update is ready.</summary>
    public async Task CheckInBackgroundAsync()
    {
        try
        {
            if (!await IsInstalledAsync()) return;
            await CheckAndDownloadAsync();
            if (_ready is not null) ShowReady();
        }
        catch (Exception)
        {
            // Offline, GitHub down, rate limit…: the automatic check never bothers the user.
        }
    }

    /// <summary>"Check for updates" from the menu: always reports the outcome in the banner.</summary>
    public async Task CheckNowAsync()
    {
        try
        {
            if (!await IsInstalledAsync())
            {
                showBanner(NotInstalledMessage);
                return;
            }
            await CheckAndDownloadAsync();
        }
        catch (Exception)
        {
            showBanner(FailedMessage);
            return;
        }

        if (_ready is not null) ShowReady();
        else showBanner($"You're on the latest version (v{_manager?.CurrentVersion}).");
    }

    /// <summary>Cleans up like a normal exit, then lets Velopack apply the downloaded update and restart the app.</summary>
    public void RestartToUpdate()
    {
        if (_manager is null || _ready is null) return;
        try { beforeRestart(); } catch { /* best-effort cleanup; CleanupLeftovers runs on next start */ }
        _manager.ApplyUpdatesAndRestart(_ready);
    }

    private void ShowReady() => showBanner($"Version {_ready!.Version} is ready — restart to update.");

    private async Task<bool> IsInstalledAsync()
    {
        if (_managerTask is { IsFaulted: true }) _managerTask = null; // retry on the next check
        _managerTask ??= Task.Run(() => new UpdateManager(new GithubSource(repoUrl, accessToken: null, prerelease: false)));
        _manager = await _managerTask;
        return _manager.IsInstalled;
    }

    private Task CheckAndDownloadAsync()
    {
        if (_ready is not null) return Task.CompletedTask;
        if (_running is { IsCompleted: false }) return _running;
        return _running = RunCheckAndDownloadAsync(_manager!);
    }

    private async Task RunCheckAndDownloadAsync(UpdateManager manager)
    {
        var asset = await Task.Run(async () =>
        {
            var info = await manager.CheckForUpdatesAsync();
            if (info is null) return null;
            await manager.DownloadUpdatesAsync(info);
            return info.TargetFullRelease;
        });
        if (asset is not null) _ready = asset;
    }
}
