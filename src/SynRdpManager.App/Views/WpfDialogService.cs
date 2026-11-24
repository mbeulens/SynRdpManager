using System.Windows;
using SynRdpManager.App.ViewModels;

namespace SynRdpManager.App.Views;

public sealed class WpfDialogService : IDialogService
{
    private static Window? Owner => Application.Current?.MainWindow is { IsVisible: true } w ? w : null;

    public bool Confirm(string title, string message) =>
        Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public void Inform(string title, string message) =>
        Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);

    public bool EditSettings(SettingsViewModel settings)
    {
        var window = new SettingsWindow(settings) { Owner = Owner };
        return window.ShowDialog() == true;
    }

    private static MessageBoxResult Show(string message, string title, MessageBoxButton buttons, MessageBoxImage image) =>
        Owner is { } owner
            ? MessageBox.Show(owner, message, title, buttons, image)
            : MessageBox.Show(message, title, buttons, image);
}
