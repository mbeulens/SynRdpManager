using System.Text.Json.Nodes;
using SynRdpManager.Core.Settings;

namespace SynRdpManager.Core.Tests.Settings;

public class SettingsLoaderTests
{
    [Fact]
    public void Defaults_when_no_files()
    {
        var r = SettingsLoader.Load(null, null);
        Assert.Equal("MstscDealer", r.Effective.Tag);
        Assert.Equal("PuttyDealer", r.Effective.PuttyTag);
        Assert.Equal("SynRdpManager-Template", r.Effective.PuttyTemplateSession);
        Assert.Equal(LaunchMode.Auto, r.Effective.LaunchMode);
        Assert.Equal(15, r.Effective.CredentialCleanupSeconds);
        Assert.Empty(r.Effective.Accounts);
        Assert.Null(r.Effective.OpPath);
        Assert.Null(r.Effective.PuttyPath);
        Assert.Empty(r.Warnings);
    }

    [Fact]
    public void Team_file_overrides_defaults()
    {
        var r = SettingsLoader.Load("""{"Tag":"TeamTag","Accounts":["a@b.c"],"LaunchMode":"Clipboard","OpPath":"C:\\op.exe"}""", null);
        Assert.Equal("TeamTag", r.Effective.Tag);
        Assert.Equal(new[] { "a@b.c" }, r.Effective.Accounts);
        Assert.Equal(LaunchMode.Clipboard, r.Effective.LaunchMode);
        Assert.Equal("C:\\op.exe", r.Effective.OpPath);
        Assert.Equal(r.Effective, r.TeamDefaults with { Accounts = r.Effective.Accounts });
    }

    [Fact]
    public void User_file_overrides_team_per_key()
    {
        var r = SettingsLoader.Load("""{"Tag":"T","PuttyTag":"PT"}""", """{"tag":"U"}""");
        Assert.Equal("U", r.Effective.Tag);
        Assert.Equal("PT", r.Effective.PuttyTag);
        Assert.Equal("T", r.TeamDefaults.Tag);
    }

    [Fact]
    public void Invalid_json_is_ignored_with_warning()
    {
        var r = SettingsLoader.Load("{ not json SECRET", """{"Tag":"U"}""");
        Assert.Equal("U", r.Effective.Tag);
        var w = Assert.Single(r.Warnings);
        Assert.Contains("team settings", w);
        Assert.DoesNotContain("SECRET", w);
    }

    [Fact]
    public void Duplicate_keys_ignore_that_layer_with_warning()
    {
        var r = SettingsLoader.Load("""{"Tag":"T"}""", """{"Tag":"a","Tag":"b"}""");
        Assert.Equal("T", r.Effective.Tag);
        var w = Assert.Single(r.Warnings);
        Assert.Contains("user settings", w);
    }

    [Fact]
    public void Unknown_keys_are_ignored()
    {
        var r = SettingsLoader.Load("""{"Bogus":1,"Tag":"T"}""", null);
        Assert.Equal("T", r.Effective.Tag);
        Assert.Empty(r.Warnings);
    }

    [Theory]
    [InlineData(1, 5)]
    [InlineData(500, 120)]
    [InlineData(30, 30)]
    public void CredentialCleanupSeconds_is_clamped_to_5_120(int input, int expected)
    {
        var r = SettingsLoader.Load(null, $$"""{"CredentialCleanupSeconds":{{input}}}""");
        Assert.Equal(expected, r.Effective.CredentialCleanupSeconds);
    }

    [Fact]
    public void Blank_tag_falls_back_to_default()
    {
        var r = SettingsLoader.Load("""{"Tag":"T"}""", """{"Tag":"  ","PuttyTag":""}""");
        Assert.Equal("T", r.Effective.Tag);
        Assert.Equal("PuttyDealer", r.Effective.PuttyTag);
    }

    [Fact]
    public void Invalid_enum_or_wrong_type_ignores_only_that_key()
    {
        var r = SettingsLoader.Load(null, """{"LaunchMode":"Nope","CredentialCleanupSeconds":"x","Tag":"U"}""");
        Assert.Equal("U", r.Effective.Tag);
        Assert.Equal(LaunchMode.Auto, r.Effective.LaunchMode);
        Assert.Equal(15, r.Effective.CredentialCleanupSeconds);
        Assert.Equal(2, r.Warnings.Count);
        Assert.All(r.Warnings, w => Assert.Contains("user settings", w));
    }

    [Fact]
    public void Serialize_writes_only_keys_differing_from_team()
    {
        var team = SettingsLoader.Load("""{"PuttyTag":"PT","Accounts":["x"]}""", null).TeamDefaults;
        var edited = team with { Tag = "Mine", Accounts = new List<string> { "x" } };
        var o = JsonNode.Parse(SettingsLoader.SerializeUserOverrides(edited, team))!.AsObject();
        Assert.Equal(new[] { "Tag" }, o.Select(p => p.Key).ToArray());
        Assert.Equal("Mine", (string?)o["Tag"]);
    }

    [Fact]
    public void Serialize_writes_enum_as_string_and_round_trips()
    {
        var team = new AppSettings();
        var edited = team with { LaunchMode = LaunchMode.Clipboard, Accounts = new[] { "a", "b" }, CredentialCleanupSeconds = 40 };
        var json = SettingsLoader.SerializeUserOverrides(edited, team);
        Assert.Contains("\"LaunchMode\": \"Clipboard\"", json);
        var back = SettingsLoader.Load(null, json).Effective;
        Assert.Equal(LaunchMode.Clipboard, back.LaunchMode);
        Assert.Equal(new[] { "a", "b" }, back.Accounts);
        Assert.Equal(40, back.CredentialCleanupSeconds);
    }

    [Fact]
    public void Serialize_no_differences_gives_empty_object()
    {
        var s = new AppSettings();
        Assert.Equal("{}", SettingsLoader.SerializeUserOverrides(s, s));
    }
}
