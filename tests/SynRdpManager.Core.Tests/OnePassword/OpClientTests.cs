using SynRdpManager.Core.OnePassword;
using SynRdpManager.Core.Platform;

namespace SynRdpManager.Core.Tests.OnePassword;

public class OpClientTests
{
    sealed class FakeRunner(Func<IReadOnlyList<string>, ProcessResult> respond) : IProcessRunner
    {
        public List<(string Exe, string[] Args, string? Stdin, TimeSpan Timeout)> Calls { get; } = [];
        public Task<ProcessResult> RunAsync(string exe, IReadOnlyList<string> args, string? stdin, TimeSpan timeout, CancellationToken ct)
        {
            Calls.Add((exe, args.ToArray(), stdin, timeout));
            return Task.FromResult(respond(args));
        }
    }

    static ProcessResult Ok(string s) => new(0, s, "", false);
    static readonly OpAccount Acc = new("acct-uuid", "me@x.example", "x.1password.com");

    [Theory]
    [InlineData("No accounts configured for use with 1Password CLI", OpErrorKind.NotSignedIn)]
    [InlineData("[ERROR] authorization prompt dismissed", OpErrorKind.Locked)]
    [InlineData("error connecting to desktop app", OpErrorKind.IntegrationDisabled)]
    [InlineData("AUTHORIZATION PROMPT DISMISSED", OpErrorKind.Locked)]
    [InlineData("something else", OpErrorKind.Other)]
    public void Classify_by_stderr(string stderr, OpErrorKind kind)
    {
        Assert.Equal(kind, OpErrorClassifier.Classify(new ProcessResult(1, "", stderr, false)));
    }

    [Fact]
    public void Classify_timed_out()
    {
        Assert.Equal(OpErrorKind.TimedOut, OpErrorClassifier.Classify(new ProcessResult(-1, "", "", true)));
    }

    [Fact]
    public async Task Timeout_maps_to_TimedOut()
    {
        var c = new OpClient("op", new FakeRunner(_ => new ProcessResult(-1, "", "", true)));
        var ex = await Assert.ThrowsAsync<OpException>(() => c.ListAccountsAsync(default));
        Assert.Equal(OpErrorKind.TimedOut, ex.Kind);
    }

    [Fact]
    public async Task ListAccounts_uses_exact_args()
    {
        var r = new FakeRunner(_ => Ok(Fx.Read("account-list.json")));
        var accounts = await new OpClient("op", r).ListAccountsAsync(default);
        Assert.Equal(2, accounts.Count);
        Assert.Equal(["account", "list", "--format", "json"], r.Calls[0].Args);
        Assert.Equal("op", r.Calls[0].Exe);
        Assert.Equal(TimeSpan.FromSeconds(120), r.Calls[0].Timeout);
    }

    [Fact]
    public async Task GetItemsByTag_uses_single_batched_get()
    {
        var list = Fx.Read("item-list.json");
        var r = new FakeRunner(a => a[0] == "item" && a[1] == "list" ? Ok(list) : Ok(Fx.Read("item-get-batch.json")));
        var items = await new OpClient("op", r).GetItemsByTagAsync(Acc, "MstscDealer", default);
        Assert.Equal(3, items.Count);
        Assert.Equal(2, r.Calls.Count);
        Assert.Equal(["item", "list", "--tags", "MstscDealer", "--format", "json", "--account", "acct-uuid"], r.Calls[0].Args);
        Assert.Equal(["item", "get", "-", "--format", "json", "--account", "acct-uuid"], r.Calls[1].Args);
        Assert.Equal(list, r.Calls[1].Stdin);
    }

    [Fact]
    public async Task GetItemsByTag_empty_list_skips_batched_get()
    {
        var r = new FakeRunner(_ => Ok("[]"));
        var items = await new OpClient("op", r).GetItemsByTagAsync(Acc, "t", default);
        Assert.Empty(items);
        Assert.Single(r.Calls);
    }

    [Fact]
    public async Task GetItem_uses_exact_args()
    {
        var r = new FakeRunner(_ => Ok(Fx.Read("item-get-server.json")));
        var item = await new OpClient("op", r).GetItemAsync(Acc, "vault1", "item1", default);
        Assert.Equal("Customer A - server01", item.Title);
        Assert.Equal(["item", "get", "item1", "--vault", "vault1", "--format", "json", "--account", "acct-uuid"], r.Calls[0].Args);
    }

    [Fact]
    public async Task Exception_messages_never_contain_stdout()
    {
        var r = new FakeRunner(_ => new ProcessResult(1, "password fake-pass-1", "boom", false));
        var ex = await Assert.ThrowsAsync<OpException>(() => new OpClient("op", r).ListAccountsAsync(default));
        Assert.DoesNotContain("fake-pass-1", ex.ToString());
        Assert.DoesNotContain("fake-pass-1", ex.UserMessage);
        Assert.DoesNotContain("fake-pass-1", new ProcessResult(1, "fake-pass-1", "", false).ToString());
    }

    [Fact]
    public async Task Missing_exe_throws_NotFound_without_running()
    {
        var r = new FakeRunner(_ => Ok("[]"));
        var ex = await Assert.ThrowsAsync<OpException>(() => new OpClient("", r).ListAccountsAsync(default));
        Assert.Equal(OpErrorKind.NotFound, ex.Kind);
        Assert.Empty(r.Calls);
    }
}
