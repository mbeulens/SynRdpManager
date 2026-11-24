using SynRdpManager.Core.Platform;

namespace SynRdpManager.Core.Tests.Putty;

public sealed class InMemoryPuttyStore : IPuttySessionStore
{
    public Dictionary<string, Dictionary<string, object>> Sessions { get; } = new(StringComparer.OrdinalIgnoreCase);
    public int WriteCount { get; private set; }

    public bool Exists(string name) => Sessions.ContainsKey(name);

    public IReadOnlyDictionary<string, object> Read(string name) =>
        new Dictionary<string, object>(Sessions[name]);

    public void Write(string name, IReadOnlyDictionary<string, object> values)
    {
        WriteCount++;
        if (!Sessions.TryGetValue(name, out var s)) Sessions[name] = s = new();
        foreach (var kv in values) s[kv.Key] = kv.Value;
    }
}
