using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SynRdpManager.App.ViewModels;

namespace SynRdpManager.App.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;

    public MainWindow(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
    }

    private void ServerTree_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (FindItem(e.OriginalSource as DependencyObject)?.DataContext is EntryViewModel entry)
        {
            e.Handled = true;
            Connect(entry);
        }
    }

    private void ServerTree_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && ServerTree.SelectedItem is EntryViewModel entry)
        {
            e.Handled = true;
            Connect(entry);
        }
    }

    private void Connect(EntryViewModel entry)
    {
        if (_vm.ConnectCommand.CanExecute(entry)) _vm.ConnectCommand.Execute(entry);
    }

    // Nearest TreeViewItem around the clicked element (Runs are not Visuals, so fall back to the logical tree).
    private static TreeViewItem? FindItem(DependencyObject? d)
    {
        while (d is not null and not TreeViewItem)
            d = d is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(d)
                : LogicalTreeHelper.GetParent(d);
        return d as TreeViewItem;
    }
}
