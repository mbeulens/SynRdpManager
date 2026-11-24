using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using SynRdpManager.Core.Settings;

namespace SynRdpManager.App.ViewModels;

public sealed class SettingsViewModel : ObservableObject
{
    private const int MinCleanupSeconds = 5;
    private const int MaxCleanupSeconds = 120;

    private string _tag;
    private string _puttyTag;
    private string _puttyTemplateSession;
    private string _opPath;
    private string _puttyPath;
    private string _accountsText;
    private LaunchMode _launchMode;
    private string _cleanupSecondsText;

    public SettingsViewModel(AppSettings current)
    {
        _tag = current.Tag;
        _puttyTag = current.PuttyTag;
        _puttyTemplateSession = current.PuttyTemplateSession;
        _opPath = current.OpPath ?? "";
        _puttyPath = current.PuttyPath ?? "";
        _accountsText = string.Join(", ", current.Accounts);
        _launchMode = current.LaunchMode;
        _cleanupSecondsText = current.CredentialCleanupSeconds.ToString(CultureInfo.InvariantCulture);
    }

    public IReadOnlyList<LaunchMode> LaunchModes { get; } = Enum.GetValues<LaunchMode>();

    public string Tag { get => _tag; set => SetProperty(ref _tag, value); }
    public string PuttyTag { get => _puttyTag; set => SetProperty(ref _puttyTag, value); }
    public string PuttyTemplateSession { get => _puttyTemplateSession; set => SetProperty(ref _puttyTemplateSession, value); }
    public string OpPath { get => _opPath; set => SetProperty(ref _opPath, value); }
    public string PuttyPath { get => _puttyPath; set => SetProperty(ref _puttyPath, value); }
    public string AccountsText { get => _accountsText; set => SetProperty(ref _accountsText, value); }
    public LaunchMode LaunchMode { get => _launchMode; set => SetProperty(ref _launchMode, value); }
    public string CleanupSecondsText { get => _cleanupSecondsText; set => SetProperty(ref _cleanupSecondsText, value); }

    /// <summary>The validated settings after a successful <see cref="TryApply"/>.</summary>
    public AppSettings? Result { get; private set; }

    public bool TryApply(out string? error)
    {
        Result = null;
        if (string.IsNullOrWhiteSpace(Tag)) { error = "Tag (RDP) must not be empty."; return false; }
        if (string.IsNullOrWhiteSpace(PuttyTag)) { error = "Tag (SSH / PuTTY) must not be empty."; return false; }
        if (string.IsNullOrWhiteSpace(PuttyTemplateSession)) { error = "PuTTY template session must not be empty."; return false; }
        if (!int.TryParse(CleanupSecondsText?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds)
            || seconds < MinCleanupSeconds || seconds > MaxCleanupSeconds)
        {
            error = $"Credential cleanup must be a whole number of seconds between {MinCleanupSeconds} and {MaxCleanupSeconds}.";
            return false;
        }

        Result = new AppSettings
        {
            Tag = Tag.Trim(),
            PuttyTag = PuttyTag.Trim(),
            PuttyTemplateSession = PuttyTemplateSession.Trim(),
            OpPath = Blank(OpPath),
            PuttyPath = Blank(PuttyPath),
            Accounts = (AccountsText ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList(),
            LaunchMode = LaunchMode,
            CredentialCleanupSeconds = seconds,
        };
        error = null;
        return true;
    }

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim().Trim('"');
}
