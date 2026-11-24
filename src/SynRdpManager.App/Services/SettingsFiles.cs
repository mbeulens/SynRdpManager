using System.IO;
using SynRdpManager.App.Platform;
using SynRdpManager.Core.Settings;

namespace SynRdpManager.App.Services;

/// <summary>Reads the team (next to the exe) and user settings files and writes the user overrides.</summary>
public static class SettingsFiles
{
    public static SettingsLoadResult Load()
    {
        var readWarnings = new List<string>();
        var team = ReadIfExists(AppPaths.TeamSettingsFile, "team settings", readWarnings);
        var user = ReadIfExists(AppPaths.UserSettingsFile, "user settings", readWarnings);
        var r = SettingsLoader.Load(team, user);
        return readWarnings.Count == 0 ? r : r with { Warnings = [.. readWarnings, .. r.Warnings] };
    }

    public static void SaveUserOverrides(AppSettings edited, AppSettings teamDefaults) =>
        File.WriteAllText(AppPaths.UserSettingsFile, SettingsLoader.SerializeUserOverrides(edited, teamDefaults));

    private static string? ReadIfExists(string path, string layerName, List<string> warnings)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            warnings.Add($"Ignored {layerName}: the file could not be read ({path}).");
            return null;
        }
    }
}
