using System.IO;
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

    public static string TeamSettingsFile => Path.Combine(AppContext.BaseDirectory, "settings.json");

    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(LocalDir);
        Directory.CreateDirectory(RoamingDir);
    }
}
