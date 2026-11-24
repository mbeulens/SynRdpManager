using System.Text.Json;
using System.Text.Json.Serialization;
using SynRdpManager.Core.Models;
using SynRdpManager.Core.Platform;
using SynRdpManager.Core.Settings;

namespace SynRdpManager.Core.Catalog;

public sealed record CacheSnapshot(
    int SchemaVersion,
    DateTimeOffset SavedAt,
    string Tag,
    string PuttyTag,
    IReadOnlyList<ServerEntry> Entries);

public static class CacheCodec
{
    public const int SchemaVersion = 1;

    static readonly JsonSerializerOptions Json = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public static byte[] Encode(CacheSnapshot s, IDataProtector p) =>
        p.Protect(JsonSerializer.SerializeToUtf8Bytes(s, Json));

    public static CacheSnapshot? Decode(byte[] data, IDataProtector p)
    {
        try
        {
            var snap = JsonSerializer.Deserialize<CacheSnapshot>(p.Unprotect(data), Json);
            if (snap is null || snap.SchemaVersion != SchemaVersion || snap.Entries is null) return null;
            return snap;
        }
        catch (Exception)
        {
            return null; // corrupt, foreign or undecryptable cache: caller treats as "no cache"
        }
    }

    public static CacheSnapshot? DecodeFor(byte[] data, IDataProtector p, AppSettings s)
    {
        var snap = Decode(data, p);
        if (snap is null) return null;
        return string.Equals(snap.Tag, s.Tag, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(snap.PuttyTag, s.PuttyTag, StringComparison.OrdinalIgnoreCase)
            ? snap : null;
    }
}
