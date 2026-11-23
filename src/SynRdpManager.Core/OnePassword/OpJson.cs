using System.Text;
using System.Text.Json;

namespace SynRdpManager.Core.OnePassword;

public static class OpJson
{
    public static IReadOnlyList<OpAccount> ParseAccounts(string json) =>
        Elements(json)
            .Select(e => new OpAccount(Str(e, "account_uuid") ?? "", Str(e, "email") ?? "", Str(e, "url") ?? ""))
            .ToList();

    public static IReadOnlyList<OpItemSummary> ParseItemList(string json) =>
        Elements(json)
            .Select(e =>
            {
                var (vid, vname) = Vault(e);
                return new OpItemSummary(Str(e, "id") ?? "", Str(e, "title") ?? "", vid, vname);
            })
            .ToList();

    /// <summary>Parses one object, an array, or several concatenated objects (as `op item get -` prints them).</summary>
    public static IReadOnlyList<OpItem> ParseItems(string json) =>
        Elements(json).Select(ToItem).ToList();

    static OpItem ToItem(JsonElement e)
    {
        var (vid, vname) = Vault(e);
        var fields = new List<OpField>();
        if (e.TryGetProperty("fields", out var fs) && fs.ValueKind == JsonValueKind.Array)
            foreach (var f in fs.EnumerateArray())
                if (f.ValueKind == JsonValueKind.Object)
                    fields.Add(new OpField(Str(f, "label") ?? "", Str(f, "purpose"), Str(f, "value")));

        var urls = new List<string>();
        var primary = new List<string>();
        if (e.TryGetProperty("urls", out var us) && us.ValueKind == JsonValueKind.Array)
            foreach (var u in us.EnumerateArray())
                if (u.ValueKind == JsonValueKind.Object && Str(u, "href") is { Length: > 0 } href)
                {
                    if (u.TryGetProperty("primary", out var p) && p.ValueKind == JsonValueKind.True)
                        primary.Add(href);
                    else
                        urls.Add(href);
                }
        // Primary url first so "primary, else first" is simply Urls[0].
        primary.AddRange(urls);
        return new OpItem(Str(e, "id") ?? "", Str(e, "title") ?? "", vid, vname, fields, primary);
    }

    static (string Id, string Name) Vault(JsonElement e) =>
        e.TryGetProperty("vault", out var v) && v.ValueKind == JsonValueKind.Object
            ? (Str(v, "id") ?? "", Str(v, "name") ?? "")
            : ("", "");

    static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    static List<JsonElement> Elements(string json)
    {
        var result = new List<JsonElement>();
        if (string.IsNullOrWhiteSpace(json)) return result;
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json),
            new JsonReaderOptions { AllowMultipleValues = true });
        while (reader.Read())
        {
            using var doc = JsonDocument.ParseValue(ref reader);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Array)
                foreach (var el in root.EnumerateArray())
                {
                    if (el.ValueKind == JsonValueKind.Object) result.Add(el.Clone());
                }
            else if (root.ValueKind == JsonValueKind.Object)
                result.Add(root.Clone());
        }
        return result;
    }
}
