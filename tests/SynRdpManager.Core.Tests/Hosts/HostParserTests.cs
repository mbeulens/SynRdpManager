using SynRdpManager.Core.Hosts;
using SynRdpManager.Core.Models;

namespace SynRdpManager.Core.Tests.Hosts;

public class HostParserTests
{
    [Theory]
    [InlineData("server01", "server01", null)]
    [InlineData("  server01.customer-a.local  ", "server01.customer-a.local", null)]
    [InlineData("server01:3390", "server01", 3390)]
    [InlineData("rdp://server01:3390/", "server01", 3390)]
    [InlineData("https://server01/path?q=1", "server01", null)]
    [InlineData("ssh://user@linux01:2222", "linux01", 2222)]
    [InlineData("[fe80::1]:3389", "fe80::1", 3389)]
    [InlineData("[fe80::1]", "fe80::1", null)]
    [InlineData("fe80::1", "fe80::1", null)]
    [InlineData("10.0.0.5", "10.0.0.5", null)]
    public void HostParser_cases(string raw, string host, int? port)
    {
        var result = HostParser.Parse(raw);
        Assert.NotNull(result);
        Assert.Equal(host, result!.Host);
        Assert.Equal(port, result.Port);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("server01:abc")]
    [InlineData("server01:70000")]
    [InlineData("server01:0")]
    public void HostParser_invalid_returns_null(string? raw)
    {
        Assert.Null(HostParser.Parse(raw));
    }

    [Fact]
    public void MstscTarget_and_CredentialTarget()
    {
        var a = new HostAddress("h", 3390);
        Assert.Equal("h:3390", a.MstscTarget);
        Assert.Equal("TERMSRV/h", a.CredentialTarget);
        Assert.Equal("h", new HostAddress("h", null).MstscTarget);
        Assert.Equal("[fe80::1]:3389", new HostAddress("fe80::1", 3389).MstscTarget);
        Assert.Equal("fe80::1", new HostAddress("fe80::1", null).MstscTarget);
        Assert.Equal("TERMSRV/h", new HostAddress("h", 3390).CredentialTarget);
        Assert.Equal(22, new HostAddress("h", null).SshPort);
        Assert.Equal(2222, new HostAddress("h", 2222).SshPort);
    }

    [Fact]
    public void RdpCredentials_ToString_hides_password()
    {
        Assert.Equal("RdpCredentials(bob, ***)", new RdpCredentials("bob", "s3cret").ToString());
    }
}
