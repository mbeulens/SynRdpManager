using SynRdpManager.Core.Catalog;
using SynRdpManager.Core.Models;
using SynRdpManager.Core.OnePassword;
using SynRdpManager.Core.Platform;
using SynRdpManager.Core.Settings;
using SynRdpManager.Core.Tests.OnePassword;

namespace SynRdpManager.Core.Tests.Catalog;

public class CatalogServiceTests
{
    sealed record Itm(string Id, string Title, string Vault, string Host, string? User = null);

    static string Accounts(params (string Uuid, string Email)[] a) =>
        "[" + string.Join(",", a.Select(x => $"{{\"url\":\"u\",\"email\":\"{x.Email}\",\"account_uuid\":\"{x.Uuid}\"}}")) + "]";

    static string ListJson(IEnumerable<Itm> items) =>
        "[" + string.Join(",", items.Select(i =>
            $"{{\"id\":\"{i.Id}\",\"title\":\"{i.Title}\",\"vault\":{{\"id\":\"v-{i.Vault}\",\"name\":\"{i.Vault}\"}}}}")) + "]";

    static string GetJson(IEnumerable<Itm> items) =>
        string.Join("\n", items.Select(i =>
            $"{{\"id\":\"{i.Id}\",\"title\":\"{i.Title}\",\"vault\":{{\"id\":\"v-{i.Vault}\",\"name\":\"{i.Vault}\"}}," +
            "\"fields\":[" +
            (i.User is null ? "" : $"{{\"label\":\"username\",\"purpose\":\"USERNAME\",\"value\":\"{i.User}\"}},") +
            $"{{\"label\":\"Hostname\",\"value\":\"{i.Host}\"}}]}}"));

    /// <summary>Answers by args: account list, item list per account+tag, batched get (only the items listed on stdin).</summary>
    static FakeRunner Fake(string accountsJson, Dictionary<(string Acct, string Tag), Itm[]> data,
        Func<string, string, ProcessResult?>? fail = null)
    {
        FakeRunner? self = null;
        self = new FakeRunner(args =>
        {
            if (args[0] == "account") return new ProcessResult(0, accountsJson, "", false);
            var acct = args[^1];
            var step = args[1];
            if (fail?.Invoke(acct, step) is { } f) return f;
            if (step == "list")
                return new ProcessResult(0, ListJson(data.GetValueOrDefault((acct, args[3])) ?? []), "", false);
            if (step == "get" && args[2] == "-")
            {
                string stdin;
                lock (self!.Calls) stdin = self.Calls.Last(c => c.Stdin is not null && c.Args[^1] == acct).Stdin!;
                var ids = OpJson.ParseItemList(stdin).Select(i => i.Id).ToHashSet();
                var items = data.Where(kv => kv.Key.Acct == acct).SelectMany(kv => kv.Value).Where(i => ids.Contains(i.Id));
                return new ProcessResult(0, GetJson(items), "", false);
            }
            return new ProcessResult(1, "", "unexpected", false);
        });
        return self;
    }

    static readonly string TwoAccounts = Accounts(("A1", "a@x.nl"), ("B1", "b@x.nl"));

    static Dictionary<(string, string), Itm[]> Data() => new()
    {
        [("A1", "MstscDealer")] = [new Itm("a-rdp", "Alpha", "Servers", "alpha.local")],
        [("A1", "PuttyDealer")] = [new Itm("a-ssh", "Alpha SSH", "Servers", "alpha-ssh.local:2222", "root")],
        [("B1", "MstscDealer")] = [new Itm("b-rdp", "Bravo", "Servers", "bravo.local")],
        [("B1", "PuttyDealer")] = [new Itm("b-ssh", "Bravo SSH", "Other", "bravo-ssh.local", "ops")],
    };

    [Fact]
    public async Task Refresh_queries_each_account_for_both_tags()
    {
        var r = Fake(TwoAccounts, Data());
        var res = await new CatalogService(new OpClient("op", r)).RefreshAsync(new AppSettings(), default);

        Assert.Empty(res.Warnings);
        Assert.Equal(4, res.Entries.Count);
        var a = res.Entries.Single(e => e.ItemId == "a-rdp");
        Assert.Equal(ConnectionKind.Rdp, a.Kind);
        Assert.Equal("a@x.nl", a.AccountName);
        Assert.Equal("A1", a.AccountId);
        Assert.Equal("alpha.local", a.Address!.Host);
        var s = res.Entries.Single(e => e.ItemId == "b-ssh");
        Assert.Equal(ConnectionKind.Ssh, s.Kind);
        Assert.Equal("b@x.nl", s.AccountName);
        Assert.Equal("Other", s.VaultName);
        var listCalls = r.Calls.Where(c => c.Args.Length > 1 && c.Args[1] == "list" && c.Args[0] == "item").ToList();
        Assert.Equal(4, listCalls.Count);
        Assert.Equal(2, listCalls.Count(c => c.Args[3] == "MstscDealer"));
        Assert.Equal(2, listCalls.Count(c => c.Args[3] == "PuttyDealer"));
    }

    [Fact]
    public async Task Refresh_respects_Accounts_filter()
    {
        var r = Fake(TwoAccounts, Data());
        var s = new AppSettings { Accounts = ["B@X.NL"] };
        var res = await new CatalogService(new OpClient("op", r)).RefreshAsync(s, default);
        Assert.Equal(2, res.Entries.Count);
        Assert.All(res.Entries, e => Assert.Equal("B1", e.AccountId));
        Assert.DoesNotContain(r.Calls, c => c.Args[^1] == "A1");
    }

    [Fact]
    public async Task Refresh_one_account_failing_adds_warning_keeps_others()
    {
        var r = Fake(TwoAccounts, Data(), (acct, _) =>
            acct == "A1" ? new ProcessResult(1, "secret-out", "authorization prompt dismissed", false) : null);
        var res = await new CatalogService(new OpClient("op", r)).RefreshAsync(new AppSettings(), default);
        Assert.Equal(2, res.Entries.Count);
        Assert.All(res.Entries, e => Assert.Equal("B1", e.AccountId));
        var w = Assert.Single(res.Warnings);
        Assert.StartsWith("Account a@x.nl: ", w);
        Assert.Contains("locked", w);
        Assert.DoesNotContain("secret-out", w);
    }

    [Fact]
    public async Task Refresh_all_failing_throws_first_OpException()
    {
        var r = Fake(TwoAccounts, Data(), (_, _) => new ProcessResult(1, "", "authorization prompt dismissed", false));
        var ex = await Assert.ThrowsAsync<OpException>(() =>
            new CatalogService(new OpClient("op", r)).RefreshAsync(new AppSettings(), default));
        Assert.Equal(OpErrorKind.Locked, ex.Kind);
    }

    [Fact]
    public async Task Refresh_malformed_json_for_one_account_is_a_warning()
    {
        var r = Fake(TwoAccounts, Data(), (acct, step) =>
            acct == "A1" && step == "list" ? new ProcessResult(0, "{ not json", "", false) : null);
        var res = await new CatalogService(new OpClient("op", r)).RefreshAsync(new AppSettings(), default);
        Assert.Equal(2, res.Entries.Count);
        var w = Assert.Single(res.Warnings);
        Assert.Equal("Account a@x.nl: 1Password returned data SynRdpManager could not read.", w);
    }

    [Fact]
    public async Task Refresh_malformed_json_for_every_account_throws_OpException()
    {
        var r = Fake(TwoAccounts, Data(), (_, step) => step == "list" ? new ProcessResult(0, "{ not json", "", false) : null);
        var ex = await Assert.ThrowsAsync<OpException>(() =>
            new CatalogService(new OpClient("op", r)).RefreshAsync(new AppSettings(), default));
        Assert.Equal(OpErrorKind.Other, ex.Kind);
    }

    [Fact]
    public async Task Refresh_collects_ssh_usernames_only_for_ssh_items()
    {
        var r = Fake(TwoAccounts, Data());
        var res = await new CatalogService(new OpClient("op", r)).RefreshAsync(new AppSettings(), default);
        Assert.Equal(2, res.SshUserNames.Count);
        Assert.Equal("root", res.SshUserNames["a-ssh"]);
        Assert.Equal("ops", res.SshUserNames["b-ssh"]);
        Assert.False(res.SshUserNames.ContainsKey("a-rdp"));
    }

    [Fact]
    public async Task Refresh_zero_accounts_throws_IntegrationDisabled()
    {
        var r = Fake("[]", Data());
        var ex = await Assert.ThrowsAsync<OpException>(() =>
            new CatalogService(new OpClient("op", r)).RefreshAsync(new AppSettings(), default));
        Assert.Equal(OpErrorKind.IntegrationDisabled, ex.Kind);
        Assert.Equal("No 1Password accounts are available to the CLI. Unlock 1Password and enable Settings → Developer → \"Integrate with 1Password CLI\", then press Refresh.", ex.UserMessage);
        Assert.Single(r.Calls);
    }

    [Fact]
    public async Task Refresh_accounts_filter_matching_none_throws()
    {
        var r = Fake(TwoAccounts, Data());
        var ex = await Assert.ThrowsAsync<OpException>(() =>
            new CatalogService(new OpClient("op", r)).RefreshAsync(new AppSettings { Accounts = ["nobody@x.nl"] }, default));
        Assert.Equal(OpErrorKind.NotSignedIn, ex.Kind);
        Assert.Equal("None of the accounts in the Accounts setting are signed in to 1Password. Check the Accounts setting.", ex.UserMessage);
        Assert.Single(r.Calls);
    }

    [Fact]
    public async Task Refresh_account_list_failure_propagates()
    {
        var r = new FakeRunner(_ => new ProcessResult(1, "", "No accounts configured", false));
        var ex = await Assert.ThrowsAsync<OpException>(() =>
            new CatalogService(new OpClient("op", r)).RefreshAsync(new AppSettings(), default));
        Assert.Equal(OpErrorKind.NotSignedIn, ex.Kind);
    }

    [Fact]
    public async Task GetLiveItem_finds_account_and_reads_item()
    {
        var r = new FakeRunner(a => a[0] == "account"
            ? new ProcessResult(0, TwoAccounts, "", false)
            : new ProcessResult(0, GetJson([new Itm("a-rdp", "Alpha", "Servers", "alpha.local")]), "", false));
        var entry = new ServerEntry("B1", "b@x.nl", "v-Servers", "Servers", "a-rdp", "Alpha", ConnectionKind.Rdp, null);
        var (acc, item) = await new CatalogService(new OpClient("op", r)).GetLiveItemAsync(entry, default);
        Assert.Equal("b@x.nl", acc.Email);
        Assert.Equal("Alpha", item.Title);
        Assert.Equal(["item", "get", "a-rdp", "--vault", "v-Servers", "--format", "json", "--account", "B1"], r.Calls[1].Args);
    }

    [Fact]
    public async Task GetLiveItem_missing_account_throws_NotSignedIn()
    {
        var r = new FakeRunner(_ => new ProcessResult(0, TwoAccounts, "", false));
        var entry = new ServerEntry("ZZ", "z@x.nl", "v", "V", "i", "T", ConnectionKind.Rdp, null);
        var ex = await Assert.ThrowsAsync<OpException>(() =>
            new CatalogService(new OpClient("op", r)).GetLiveItemAsync(entry, default));
        Assert.Equal(OpErrorKind.NotSignedIn, ex.Kind);
    }
}
