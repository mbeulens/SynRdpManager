using SynRdpManager.Core.Models;
using SynRdpManager.Core.Putty;

namespace SynRdpManager.Core.Tests.Putty;

public class PuttySessionManagerTests
{
    private const string Tpl = "SynRdpManager-Template";

    private static ServerEntry E(string id, string title, string? host = "h1", int? port = null) =>
        new("a", "A", "v", "V", id, title, ConnectionKind.Ssh, host is null ? null : new HostAddress(host, port));

    private static (InMemoryPuttyStore, PuttySessionManager) Make(bool template = true)
    {
        var s = new InMemoryPuttyStore();
        if (template)
            s.Sessions[Tpl] = new() { ["TerminalType"] = "xterm-256color", ["FontHeight"] = 12, ["HostName"] = "tplhost", ["PortNumber"] = 22 };
        return (s, new PuttySessionManager(s, Tpl));
    }

    private static readonly Dictionary<string, string?> NoUsers = new();

    [Fact]
    public void Sync_does_nothing_without_template()
    {
        var (s, m) = Make(false);
        var r = m.Sync(new[] { E("1", "T") }, NoUsers);
        Assert.True(r.TemplateMissing);
        Assert.Empty(s.Sessions);
        Assert.Equal(0, s.WriteCount);
    }

    [Fact]
    public void Sync_creates_missing_session_from_template_plus_host_port_user_protocol()
    {
        var (s, m) = Make();
        var r = m.Sync(new[] { E("1", "T", "srv", 2222) }, new Dictionary<string, string?> { ["1"] = "bob" });
        Assert.Equal(new PuttySyncResult(false, 1, 0), r);
        var v = s.Sessions["V - T"];
        Assert.Equal("xterm-256color", v["TerminalType"]);
        Assert.Equal(12, v["FontHeight"]);
        Assert.Equal("srv", v["HostName"]);
        Assert.Equal(2222, v["PortNumber"]);
        Assert.Equal("ssh", v["Protocol"]);
        Assert.Equal("bob", v["UserName"]);
        Assert.Equal("V - T", m.SessionNameFor(E("1", "T")));
    }

    [Fact]
    public void Sync_existing_session_only_updates_host_port_user()
    {
        var (s, m) = Make();
        s.Sessions["V - T"] = new() { ["HostName"] = "old", ["PortNumber"] = 22, ["UserName"] = "x", ["FontHeight"] = 14 };
        var r = m.Sync(new[] { E("1", "T", "new") }, new Dictionary<string, string?> { ["1"] = "bob" });
        Assert.Equal(new PuttySyncResult(false, 0, 1), r);
        var v = s.Sessions["V - T"];
        Assert.Equal(14, v["FontHeight"]);
        Assert.Equal("new", v["HostName"]);
        Assert.Equal("bob", v["UserName"]);
        Assert.False(v.ContainsKey("TerminalType"));
    }

    [Fact]
    public void Sync_unchanged_session_is_not_rewritten()
    {
        var (s, m) = Make();
        m.Sync(new[] { E("1", "T") }, NoUsers);
        var writes = s.WriteCount;
        var r = m.Sync(new[] { E("1", "T") }, NoUsers);
        Assert.Equal(new PuttySyncResult(false, 0, 0), r);
        Assert.Equal(writes, s.WriteCount);
    }

    [Fact]
    public void Sync_null_username_writes_empty_UserName()
    {
        var (s, m) = Make();
        m.Sync(new[] { E("1", "T") }, new Dictionary<string, string?> { ["1"] = null });
        Assert.Equal("", s.Sessions["V - T"]["UserName"]);
    }

    [Fact]
    public void Entries_without_address_are_skipped()
    {
        var (s, m) = Make();
        var r = m.Sync(new[] { E("1", "T", null) }, NoUsers);
        Assert.Equal(new PuttySyncResult(false, 0, 0), r);
        Assert.Single(s.Sessions);
    }

    [Fact]
    public void ReloadFromTemplate_overwrites_all_but_keeps_host_and_port_and_sets_user()
    {
        var (s, m) = Make();
        s.Sessions["V - T"] = new() { ["HostName"] = "keep", ["PortNumber"] = 2200, ["FontHeight"] = 14, ["UserName"] = "old" };
        m.ReloadFromTemplate(E("1", "T", "other"), "bob");
        var v = s.Sessions["V - T"];
        Assert.Equal(12, v["FontHeight"]);
        Assert.Equal("xterm-256color", v["TerminalType"]);
        Assert.Equal("keep", v["HostName"]);
        Assert.Equal(2200, v["PortNumber"]);
        Assert.Equal("ssh", v["Protocol"]);
        Assert.Equal("bob", v["UserName"]);
    }

    [Fact]
    public void ReloadFromTemplate_without_existing_session_uses_entry_address()
    {
        var (s, m) = Make();
        m.ReloadFromTemplate(E("1", "T", "srv", 2222), null);
        var v = s.Sessions["V - T"];
        Assert.Equal("srv", v["HostName"]);
        Assert.Equal(2222, v["PortNumber"]);
        Assert.Equal("", v["UserName"]);
    }

    [Fact]
    public void CreateTemplateFromDefaults_copies_Default_Settings()
    {
        var (s, m) = Make(false);
        s.Sessions["Default Settings"] = new() { ["FontHeight"] = 10, ["Protocol"] = "raw" };
        m.CreateTemplateFromDefaults();
        Assert.True(m.TemplateExists());
        Assert.Equal(10, s.Sessions[Tpl]["FontHeight"]);
        Assert.Equal("raw", s.Sessions[Tpl]["Protocol"]);
    }

    [Fact]
    public void CreateTemplateFromDefaults_without_Default_Settings_writes_minimal()
    {
        var (s, m) = Make(false);
        m.CreateTemplateFromDefaults();
        Assert.Equal(new Dictionary<string, object> { ["Protocol"] = "ssh", ["PortNumber"] = 22 }, s.Sessions[Tpl]);
    }
}
