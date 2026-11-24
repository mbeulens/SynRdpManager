using SynRdpManager.Core.Settings;

namespace SynRdpManager.Core.Tests.Settings;

public class TeamSettingsCandidatesTests
{
    private static string P(params string[] parts) =>
        Path.DirectorySeparatorChar + string.Join(Path.DirectorySeparatorChar, parts);

    [Fact]
    public void TeamSettingsCandidates_returns_exe_dir_file()
    {
        var result = SettingsLoader.TeamSettingsCandidates(P("x", "app"));
        Assert.Equal([P("x", "app", "settings.json")], result);
    }

    [Fact]
    public void TeamSettingsCandidates_includes_velopack_root_when_exe_dir_is_current()
    {
        var result = SettingsLoader.TeamSettingsCandidates(P("x", "SynRdpManager", "current"));
        Assert.Equal([P("x", "SynRdpManager", "current", "settings.json"), P("x", "SynRdpManager", "settings.json")], result);
    }

    [Fact]
    public void TeamSettingsCandidates_is_case_insensitive_and_ignores_trailing_separator()
    {
        var result = SettingsLoader.TeamSettingsCandidates(P("x", "S", "Current") + Path.DirectorySeparatorChar);
        Assert.Equal([P("x", "S", "Current", "settings.json"), P("x", "S", "settings.json")], result);
    }
}
