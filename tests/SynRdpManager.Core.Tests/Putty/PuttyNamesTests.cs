using SynRdpManager.Core.Models;
using SynRdpManager.Core.Putty;

namespace SynRdpManager.Core.Tests.Putty;

public class PuttyNamesTests
{
    [Theory]
    [InlineData("A - B", "A%20-%20B")]
    [InlineData("a\\b*c", "a%5Cb%2Ac")]
    [InlineData(".x", "%2Ex")]
    [InlineData("x.y", "x.y")]
    [InlineData("50%", "50%25")]
    [InlineData("é", "%C3%A9")]
    [InlineData("a/b", "a/b")]
    public void Escape_matches_putty_rules(string input, string expected) =>
        Assert.Equal(expected, PuttyNames.Escape(input));

    private static ServerEntry E(string id, string vault, string title) =>
        new("a", "A", "v", vault, id, title, ConnectionKind.Ssh, new HostAddress("h", null));

    [Fact]
    public void Duplicate_names_get_suffix()
    {
        var names = PuttyNames.AssignNames(new[] { E("2", "V", "T"), E("1", "V", "T") }, "Tpl");
        Assert.Equal("V - T", names["1"]);
        Assert.Equal("V - T (2)", names["2"]);
    }

    [Fact]
    public void Names_clashing_with_template_or_default_settings_get_suffix()
    {
        var names = PuttyNames.AssignNames(new[] { E("1", "Tpl", ""), E("2", "default settings", "") }, "Tpl - ");
        Assert.Equal("Tpl -  (2)", names["1"]);
        var names2 = PuttyNames.AssignNames(new[] { E("1", "Default", "Settings") }, "Tpl");
        Assert.Equal("Default - Settings", names2["1"]);
        var names3 = PuttyNames.AssignNames(new[] { E("1", "X", "Y") }, "x - y");
        Assert.Equal("X - Y (2)", names3["1"]);
    }
}
