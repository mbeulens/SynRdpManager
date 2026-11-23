namespace SynRdpManager.Core.Tests.OnePassword;

internal static class Fx
{
    public static string Read(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));
}
