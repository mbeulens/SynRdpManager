using SynRdpManager.Core.Models;
using SynRdpManager.Core.OnePassword;

namespace SynRdpManager.Core.Tests.OnePassword;

public class ItemMapperTests
{
    static readonly OpAccount Acc = new("acct-uuid", "me@x.example", "x.1password.com");

    static OpItem Item(IEnumerable<OpField>? fields = null, params string[] urls) =>
        new("i1", "Title", "v1", "Vault", (fields ?? []).ToList(), urls);

    [Fact]
    public void ToEntry_prefers_hostname_field_over_url()
    {
        var item = Item([new("Hostname", null, " srv.local:3390 ")], "other.example");
        var e = ItemMapper.ToEntry(Acc, item, ConnectionKind.Rdp);
        Assert.Equal(new HostAddress("srv.local", 3390), e.Address);
        Assert.Equal("acct-uuid", e.AccountId);
        Assert.Equal("me@x.example", e.AccountName);
        Assert.Equal("v1", e.VaultId);
        Assert.Equal("Vault", e.VaultName);
        Assert.Equal("i1", e.ItemId);
        Assert.Equal(ConnectionKind.Rdp, e.Kind);
    }

    [Fact]
    public void ToEntry_ignores_empty_hostname_field()
    {
        var e = ItemMapper.ToEntry(Acc, Item([new("hostname", null, "  ")], "u.example"), ConnectionKind.Ssh);
        Assert.Equal("u.example", e.Address!.Host);
    }

    [Fact]
    public void ToEntry_falls_back_to_primary_url_then_first_url()
    {
        var fromItems = OpJson.ParseItems(Fx.Read("item-get-batch.json"));
        var primary = ItemMapper.ToEntry(Acc, fromItems[1], ConnectionKind.Rdp);
        Assert.Equal("primary.customer-a.local", primary.Address!.Host);

        var first = ItemMapper.ToEntry(Acc, Item(null, "a.example", "b.example"), ConnectionKind.Rdp);
        Assert.Equal("a.example", first.Address!.Host);
    }

    [Fact]
    public void ToEntry_without_host_has_null_Address()
    {
        Assert.Null(ItemMapper.ToEntry(Acc, Item(), ConnectionKind.Rdp).Address);
    }

    [Fact]
    public void ComposeUserName_prefixes_domain()
    {
        var item = Item([new("username", "USERNAME", "svc"), new("DOMAIN", null, "CORP")]);
        Assert.Equal(@"CORP\svc", ItemMapper.ComposeUserName(item));
    }

    [Theory]
    [InlineData(@"a\b")]
    [InlineData("a@b")]
    public void ComposeUserName_keeps_backslash_or_at(string user)
    {
        var item = Item([new("username", "USERNAME", user), new("domain", null, "CORP")]);
        Assert.Equal(user, ItemMapper.ComposeUserName(item));
    }

    [Fact]
    public void ComposeUserName_without_domain_and_blank_user()
    {
        Assert.Equal("svc", ItemMapper.ComposeUserName(Item([new("username", "USERNAME", "svc")])));
        Assert.Null(ItemMapper.ComposeUserName(Item([new("username", "USERNAME", "  ")])));
        Assert.Null(ItemMapper.ComposeUserName(Item()));
    }

    [Fact]
    public void GetPassword_reads_PASSWORD_purpose()
    {
        var item = OpJson.ParseItems(Fx.Read("item-get-server.json"))[0];
        Assert.Equal("fake-pass-1", ItemMapper.GetPassword(item));
        Assert.Null(ItemMapper.GetPassword(Item()));
    }
}
