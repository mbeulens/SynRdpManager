using SynRdpManager.Core;

namespace SynRdpManager.Core.Tests;

public class VersionTests
{
    [Fact]
    public void Assembly_version_matches_VERSION_file()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "VERSION")))
            dir = dir.Parent;
        Assert.NotNull(dir);

        var fileVersion = File.ReadAllText(Path.Combine(dir!.FullName, "VERSION")).Trim();
        var asmVersion = typeof(CoreInfo).Assembly.GetName().Version!.ToString(3);

        Assert.Equal(fileVersion, asmVersion);
    }
}
