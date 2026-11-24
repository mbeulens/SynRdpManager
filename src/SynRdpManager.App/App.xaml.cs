using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using SynRdpManager.App.Platform;
using SynRdpManager.App.Services;
using SynRdpManager.App.ViewModels;
using SynRdpManager.App.Views;
using SynRdpManager.Core.Rdp;

namespace SynRdpManager.App;

/// <summary>Composition root.</summary>
public partial class App : Application
{
    private RdpLauncher? _rdp;
    private MainViewModel? _vm;

    private const string UpdateRepoUrl = "https://github.com/mbeulens/SynRdpManager";

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandledException;

        var startupWarnings = new List<string>();
        try
        {
            AppPaths.EnsureDirectories();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            startupWarnings.Add("Could not create the settings/cache folders: " + ex.Message);
        }

        var load = SettingsFiles.Load();
        var launcher = new ProcessLauncher();
        _rdp = new RdpLauncher(new WindowsCredentialStore(), launcher, new WindowsClipboardService(),
            TimeProvider.System,
            // May be called from a timer thread: marshal to the UI.
            onError: message => Dispatcher.InvokeAsync(() => _vm?.ShowBanner(message, warning: true)));

        try
        {
            _rdp.CleanupLeftovers();
        }
        catch (Win32Exception ex)
        {
            startupWarnings.Add("Could not check for leftover Remote Desktop credentials: " + ex.Message);
        }

        var rdp = _rdp;
        var updates = new UpdateService(UpdateRepoUrl,
            showBanner: message => Dispatcher.InvokeAsync(() => _vm?.ShowUpdateBanner(message)),
            beforeRestart: rdp.CleanupPendingNow);

        var services = new AppServices(load, _rdp, launcher, new DpapiProtector(), new RegistryPuttySessionStore());
        _vm = new MainViewModel(services, new WpfDialogService(), updates);
        var window = new MainWindow(_vm);
        MainWindow = window;
        window.Show();

        _vm.Start(startupWarnings);
        _ = updates.CheckInBackgroundAsync(); // never throws; silent on failure
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _rdp?.CleanupPendingNow();
        }
        catch (Exception)
        {
            // Exiting anyway; leftovers are removed by CleanupLeftovers on the next start.
        }
        base.OnExit(e);
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        var message = "Unexpected error: " + e.Exception.Message;
        if (_vm is not null && MainWindow is { IsVisible: true }) _vm.ShowBanner(message, warning: true);
        else
        {
            // Startup failed before the window existed: do not linger as an invisible process.
            MessageBox.Show(message, "SynRdpManager", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }
}
