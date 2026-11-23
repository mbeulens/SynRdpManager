using SynRdpManager.Core.Hosts;
using SynRdpManager.Core.Models;

namespace SynRdpManager.Core.OnePassword;

public static class ItemMapper
{
    public static ServerEntry ToEntry(OpAccount acc, OpItem item, ConnectionKind kind)
    {
        var raw = item.Fields
            .FirstOrDefault(f => IsLabel(f, "hostname") && !string.IsNullOrWhiteSpace(f.Value))?.Value
            ?? item.Urls.FirstOrDefault(); // Urls is ordered primary-first by OpJson
        return new ServerEntry(acc.AccountUuid, acc.Email, item.VaultId, item.VaultName,
            item.Id, item.Title, kind, HostParser.Parse(raw));
    }

    public static string? ComposeUserName(OpItem item)
    {
        var user = item.Fields.FirstOrDefault(f => f.Purpose == "USERNAME")?.Value?.Trim();
        if (string.IsNullOrEmpty(user)) return null;
        if (user.Contains('\\') || user.Contains('@')) return user;
        var domain = item.Fields.FirstOrDefault(f => IsLabel(f, "domain") && !string.IsNullOrWhiteSpace(f.Value))?.Value?.Trim();
        return domain is null ? user : $"{domain}\\{user}";
    }

    public static string? GetPassword(OpItem item) =>
        item.Fields.FirstOrDefault(f => f.Purpose == "PASSWORD")?.Value;

    static bool IsLabel(OpField f, string name) =>
        string.Equals(f.Label?.Trim(), name, StringComparison.OrdinalIgnoreCase);
}
