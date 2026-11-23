namespace SynRdpManager.Core.Platform;

public static class ExeLocator
{
    public static string? Find(string exeName, string? configured, IEnumerable<string> candidateDirs, string? pathEnv, Func<string, bool> exists)
    {
        if (!string.IsNullOrWhiteSpace(configured) && exists(configured)) return configured;

        foreach (var dir in candidateDirs)
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            var p = Path.Combine(dir, exeName);
            if (exists(p)) return p;
        }

        if (!string.IsNullOrWhiteSpace(pathEnv))
            foreach (var dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var p = Path.Combine(dir.Trim('"'), exeName);
                if (exists(p)) return p;
            }

        return null;
    }
}
