using System.IO;
using SynRdpManager.Core.Settings;

namespace SynRdpManager.App.Platform;

public static class AppPaths
{
    private const string FolderName = "SynRdpManager";

    private static string LocalDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), FolderName);

    private static string RoamingDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), FolderName);

    public static string CacheFile => Path.Combine(LocalDir, "cache.bin");

    public static string UserSettingsFile => Path.Combine(RoamingDir, "settings.json");

    /// <summary>
    /// First existing team settings file: next to the exe, then the Velopack install root (survives updates).
    /// When none exists, the file next to the exe (reading it then yields nothing).
    /// </summary>
    public static string TeamSettingsFile
    {
        get
        {
            var candidates = SettingsLoader.TeamSettingsCandidates(AppContext.BaseDirectory);
            return candidates.FirstOrDefault(File.Exists) ?? candidates[0];
        }
    }

    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(LocalDir);
        Directory.CreateDirectory(RoamingDir);
    }
}
