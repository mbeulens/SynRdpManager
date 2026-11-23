using SynRdpManager.Core.OnePassword;

namespace SynRdpManager.Core.Tests.OnePassword;

public class OpJsonTests
{
    [Fact]
    public void ParseAccounts_reads_fields()
    {
        var a = OpJson.ParseAccounts(Fx.Read("account-list.json"));
        Assert.Equal(2, a.Count);
        Assert.Equal("ACCTUUID0000000000000000A1", a[0].AccountUuid);
        Assert.Equal("admin@customer-a.example", a[0].Email);
        Assert.Equal("customer-a.1password.com", a[0].Url);
    }

    [Fact]
    public void ParseItemList_reads_vault()
    {
        var l = OpJson.ParseItemList(Fx.Read("item-list.json"));
        Assert.Equal(3, l.Count);
        Assert.Equal("Customer A - server01", l[0].Title);
        Assert.Equal("vault00000000000000000001", l[0].VaultId);
        Assert.Equal("Customer A", l[0].VaultName);
    }

    [Fact]
    public void ParseItems_handles_concatenated_objects()
    {
        var items = OpJson.ParseItems(Fx.Read("item-get-batch.json"));
        Assert.Equal(3, items.Count);
        Assert.Equal("Customer A - server02", items[1].Title);
        Assert.Equal(2, items[1].Urls.Count);
        Assert.Empty(items[2].Urls);
    }

    [Fact]
    public void ParseItems_handles_single_object_and_array()
    {
        var one = Fx.Read("item-get-server.json");
        Assert.Single(OpJson.ParseItems(one));
        Assert.Equal(2, OpJson.ParseItems("[" + one + "," + one + "]").Count);
    }

    [Fact]
    public void ParseItems_tolerates_missing_optional_and_unknown_properties()
    {
        var items = OpJson.ParseItems("""{"id":"i","title":"t","vault":{"id":"v","name":"n"},"extra":{"x":[1]},"fields":[{"label":"a"}]}""");
        Assert.Single(items);
        Assert.Null(items[0].Fields[0].Purpose);
        Assert.Null(items[0].Fields[0].Value);
        Assert.Empty(items[0].Urls);
    }

    [Fact]
    public void ParseItems_empty_input_gives_empty()
    {
        Assert.Empty(OpJson.ParseItems(""));
        Assert.Empty(OpJson.ParseItemList("[]"));
    }
}
