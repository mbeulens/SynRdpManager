using SynRdpManager.Core.Models;

namespace SynRdpManager.Core.Catalog;

public sealed record VaultGroup(string DisplayName, string AccountId, string VaultId, IReadOnlyList<ServerEntry> Entries);

public static class CatalogView
{
    public static IReadOnlyList<VaultGroup> Group(IEnumerable<ServerEntry> entries, string? search)
    {
        var term = search?.Trim();
        var filtered = entries
            .Where(e => string.IsNullOrEmpty(term) || Matches(e, term))
            .ToList();

        var ambiguous = filtered
            .GroupBy(e => e.VaultName, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Select(e => e.AccountId).Distinct().Count() > 1)
            .Select(g => g.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return filtered
            .GroupBy(e => (e.AccountId, e.VaultId))
            .Select(g =>
            {
                var first = g.First();
                var name = ambiguous.Contains(first.VaultName)
                    ? $"{first.VaultName} ({first.AccountName})"
                    : first.VaultName;
                var sorted = g.OrderBy(e => e.Title, StringComparer.OrdinalIgnoreCase).ToList();
                return new VaultGroup(name, g.Key.AccountId, g.Key.VaultId, sorted);
            })
            .OrderBy(g => g.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(g => g.AccountId, StringComparer.Ordinal)
            .ToList();
    }

    static bool Matches(ServerEntry e, string term) =>
        Has(e.Title, term) || Has(e.VaultName, term) || (e.Address is not null && Has(e.Address.Host, term));

    static bool Has(string s, string term) => s.Contains(term, StringComparison.OrdinalIgnoreCase);
}
