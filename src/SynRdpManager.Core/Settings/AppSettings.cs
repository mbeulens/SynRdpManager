namespace SynRdpManager.Core.Settings;

public enum LaunchMode { Auto, Clipboard }

public record AppSettings
{
    public string Tag { get; init; } = "MstscDealer";
    public string PuttyTag { get; init; } = "PuttyDealer";
    public string? OpPath { get; init; }
    public string? PuttyPath { get; init; }
    public string PuttyTemplateSession { get; init; } = "SynRdpManager-Template";

    /// <summary>Account UUIDs or emails (case-insensitive). Empty means all accounts.</summary>
    public IReadOnlyList<string> Accounts { get; init; } = Array.Empty<string>();

    public LaunchMode LaunchMode { get; init; } = LaunchMode.Auto;
    public int CredentialCleanupSeconds { get; init; } = 15;
}
