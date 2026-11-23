using SynRdpManager.Core.Platform;

namespace SynRdpManager.Core.Tests.Platform;

public class ExeLocatorTests
{
    static string J(string d, string f) => Path.Combine(d, f);

    [Fact]
    public void Configured_wins_when_it_exists()
    {
        var r = ExeLocator.Find("op.exe", "/cfg/op.exe", ["/c1"], "/p1", _ => true);
        Assert.Equal("/cfg/op.exe", r);
    }

    [Fact]
    public void Candidate_dirs_in_order_then_path_entries()
    {
        var existing = new HashSet<string> { J("/c2", "op.exe"), J("/p2", "op.exe") };
        Assert.Equal(J("/c2", "op.exe"),
            ExeLocator.Find("op.exe", "/missing/op.exe", ["/c1", "/c2"], "/p1" + Path.PathSeparator + "/p2", existing.Contains));

        existing.Remove(J("/c2", "op.exe"));
        Assert.Equal(J("/p2", "op.exe"),
            ExeLocator.Find("op.exe", null, ["/c1", "/c2"], "/p1" + Path.PathSeparator + "/p2", existing.Contains));
    }

    [Fact]
    public void Returns_null_when_none_exist()
    {
        Assert.Null(ExeLocator.Find("op.exe", "/cfg", ["/c1"], "/p1", _ => false));
    }
}
