namespace SynRdpManager.Core.OnePassword;

public sealed record OpAccount(string AccountUuid, string Email, string Url);

public sealed record OpItemSummary(string Id, string Title, string VaultId, string VaultName);

public sealed record OpField(string Label, string? Purpose, string? Value);

public sealed record OpItem(
    string Id,
    string Title,
    string VaultId,
    string VaultName,
    IReadOnlyList<OpField> Fields,
    IReadOnlyList<string> Urls);
