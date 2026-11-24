using SynRdpManager.Core.Catalog;
using SynRdpManager.Core.Models;

namespace SynRdpManager.Core.Tests.Catalog;

public class CatalogViewTests
{
    static ServerEntry E(string title, string vault, string acct = "A1", string email = "a@x.nl", string? host = null) =>
        new(acct, email, "v-" + vault, vault, "i-" + title, title, ConnectionKind.Rdp,
            host is null ? null : new HostAddress(host, null));

    [Fact]
    public void Group_sorts_vaults_and_titles_case_insensitively()
    {
        var g = CatalogView.Group([E("b", "zeta"), E("B2", "Alpha"), E("a", "Alpha"), E("C", "alpha2")], null);
        Assert.Equal(["Alpha", "alpha2", "zeta"], g.Select(x => x.DisplayName));
        Assert.Equal(["a", "B2"], g[0].Entries.Select(x => x.Title));
    }

    [Fact]
    public void Group_search_matches_title_vault_or_host_case_insensitive()
    {
        var all = new[]
        {
            E("Web", "Prod", host: "x.local"),
            E("Db", "Staging", host: "db.example"),
            E("Mail", "Misc", host: "MAILHOST.local"),
        };
        Assert.Equal(["Web"], Flat(CatalogView.Group(all, " WEB ")));
        Assert.Equal(["Db"], Flat(CatalogView.Group(all, "staging")));
        Assert.Equal(["Mail"], Flat(CatalogView.Group(all, "mailhost")));
        Assert.Equal(3, Flat(CatalogView.Group(all, null)).Count);
        Assert.Equal(3, Flat(CatalogView.Group(all, "   ")).Count);
    }

    [Fact]
    public void Group_same_vault_name_in_two_accounts_gets_account_suffix()
    {
        var g = CatalogView.Group([E("a", "Servers"), E("b", "Servers", "B1", "b@x.nl"), E("c", "Other")], null);
        Assert.Equal(["Other", "Servers (a@x.nl)", "Servers (b@x.nl)"], g.Select(x => x.DisplayName));
    }

    [Fact]
    public void Group_suffix_only_when_both_accounts_remain_after_search()
    {
        var g = CatalogView.Group([E("a", "Servers"), E("b", "Servers", "B1", "b@x.nl")], "a");
        Assert.Equal("Servers", Assert.Single(g).DisplayName);
    }

    [Fact]
    public void Group_drops_empty_groups_after_search()
    {
        var g = CatalogView.Group([E("a", "One"), E("b", "Two")], "b");
        var only = Assert.Single(g);
        Assert.Equal("Two", only.DisplayName);
        Assert.Equal("v-Two", only.VaultId);
        Assert.Equal("A1", only.AccountId);
    }

    static List<string> Flat(IReadOnlyList<VaultGroup> g) => g.SelectMany(x => x.Entries).Select(e => e.Title).ToList();
}
