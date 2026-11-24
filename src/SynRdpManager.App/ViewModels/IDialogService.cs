namespace SynRdpManager.App.ViewModels;

/// <summary>Modal dialogs the view models need; implemented by the views.</summary>
public interface IDialogService
{
    bool Confirm(string title, string message);
    void Inform(string title, string message);

    /// <summary>Shows the settings window; true when the user saved (then <see cref="SettingsViewModel.Result"/> is set).</summary>
    bool EditSettings(SettingsViewModel settings);
}
