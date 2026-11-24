using System.Windows;
using SynRdpManager.App.ViewModels;

namespace SynRdpManager.App.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _vm;

    public SettingsWindow(SettingsViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.TryApply(out var error)) DialogResult = true;
        else MessageBox.Show(this, error, "SynRdpManager settings", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}
