using System.Text.Json;
using System.Text.Json.Nodes;

namespace SynRdpManager.Core.Settings;

public record SettingsLoadResult(AppSettings Effective, AppSettings TeamDefaults, IReadOnlyList<string> Warnings);

public static class SettingsLoader
{
    private const int MinCleanupSeconds = 5;
    private const int MaxCleanupSeconds = 120;

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    // One layer's explicitly provided (valid) values; null = not provided.
    private sealed class Layer
    {
        public string? Tag, PuttyTag, OpPath, PuttyPath, PuttyTemplateSession;
        public IReadOnlyList<string>? Accounts;
        public LaunchMode? LaunchMode;
        public int? CredentialCleanupSeconds;
    }

    public static SettingsLoadResult Load(string? teamJson, string? userJson)
    {
        var warnings = new List<string>();
        var team = ParseLayer(teamJson, "team settings", warnings);
        var user = ParseLayer(userJson, "user settings", warnings);

        var teamDefaults = Clamp(Apply(new AppSettings(), team));
        var effective = Clamp(Apply(teamDefaults, user));
        return new SettingsLoadResult(effective, teamDefaults, warnings);
    }

    public static string SerializeUserOverrides(AppSettings edited, AppSettings teamDefaults)
    {
        var o = new JsonObject();
        if (edited.Tag != teamDefaults.Tag) o["Tag"] = edited.Tag;
        if (edited.PuttyTag != teamDefaults.PuttyTag) o["PuttyTag"] = edited.PuttyTag;
        if (edited.OpPath != teamDefaults.OpPath && edited.OpPath is not null) o["OpPath"] = edited.OpPath;
        if (edited.PuttyPath != teamDefaults.PuttyPath && edited.PuttyPath is not null) o["PuttyPath"] = edited.PuttyPath;
        if (edited.PuttyTemplateSession != teamDefaults.PuttyTemplateSession)
            o["PuttyTemplateSession"] = edited.PuttyTemplateSession;
        if (!edited.Accounts.SequenceEqual(teamDefaults.Accounts))
        {
            var arr = new JsonArray();
            foreach (var a in edited.Accounts) arr.Add(a);
            o["Accounts"] = arr;
        }
        if (edited.LaunchMode != teamDefaults.LaunchMode) o["LaunchMode"] = edited.LaunchMode.ToString();
        if (edited.CredentialCleanupSeconds != teamDefaults.CredentialCleanupSeconds)
            o["CredentialCleanupSeconds"] = edited.CredentialCleanupSeconds;
        return o.ToJsonString(WriteOptions);
    }

    private static AppSettings Apply(AppSettings baseSettings, Layer l) => baseSettings with
    {
        Tag = NonBlank(l.Tag) ?? baseSettings.Tag,
        PuttyTag = NonBlank(l.PuttyTag) ?? baseSettings.PuttyTag,
        PuttyTemplateSession = NonBlank(l.PuttyTemplateSession) ?? baseSettings.PuttyTemplateSession,
        OpPath = NonBlank(l.OpPath) ?? baseSettings.OpPath,
        PuttyPath = NonBlank(l.PuttyPath) ?? baseSettings.PuttyPath,
        Accounts = l.Accounts ?? baseSettings.Accounts,
        LaunchMode = l.LaunchMode ?? baseSettings.LaunchMode,
        CredentialCleanupSeconds = l.CredentialCleanupSeconds ?? baseSettings.CredentialCleanupSeconds,
    };

    private static AppSettings Clamp(AppSettings s) => s with
    {
        CredentialCleanupSeconds = Math.Clamp(s.CredentialCleanupSeconds, MinCleanupSeconds, MaxCleanupSeconds),
    };

    private static string? NonBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

    private static Layer ParseLayer(string? json, string layerName, List<string> warnings)
    {
        var layer = new Layer();
        if (string.IsNullOrWhiteSpace(json)) return layer;

        List<KeyValuePair<string, JsonNode?>> props;
        try
        {
            var root = JsonNode.Parse(json, null, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });
            if (root is not JsonObject o)
            {
                warnings.Add($"Ignored {layerName}: the file does not contain a JSON object.");
                return layer;
            }
            props = o.ToList(); // materializes the object: duplicate keys throw ArgumentException here
        }
        catch (JsonException)
        {
            warnings.Add($"Ignored {layerName}: the file is not valid JSON.");
            return layer;
        }
        catch (ArgumentException)
        {
            warnings.Add($"Ignored {layerName}: the file contains a duplicate key.");
            return layer;
        }

        foreach (var (key, node) in props)
        {
            switch (key.ToLowerInvariant())
            {
                case "tag": layer.Tag = ReadString(node, key, layerName, warnings); break;
                case "puttytag": layer.PuttyTag = ReadString(node, key, layerName, warnings); break;
                case "oppath": layer.OpPath = ReadString(node, key, layerName, warnings); break;
                case "puttypath": layer.PuttyPath = ReadString(node, key, layerName, warnings); break;
                case "puttytemplatesession": layer.PuttyTemplateSession = ReadString(node, key, layerName, warnings); break;
                case "accounts": layer.Accounts = ReadStringList(node, key, layerName, warnings); break;
                case "launchmode": layer.LaunchMode = ReadLaunchMode(node, key, layerName, warnings); break;
                case "credentialcleanupseconds": layer.CredentialCleanupSeconds = ReadInt(node, key, layerName, warnings); break;
            }
        }
        return layer;
    }

    private static string? ReadString(JsonNode? node, string key, string layer, List<string> warnings)
    {
        if (node is null) return null; // JSON null = not set
        if (node is JsonValue v && v.TryGetValue<string>(out var s)) return s;
        warnings.Add($"Ignored '{key}' in {layer}: expected a string.");
        return null;
    }

    private static int? ReadInt(JsonNode? node, string key, string layer, List<string> warnings)
    {
        if (node is null) return null;
        if (node is JsonValue v && v.GetValueKind() == JsonValueKind.Number && v.TryGetValue<int>(out var i)) return i;
        warnings.Add($"Ignored '{key}' in {layer}: expected a whole number.");
        return null;
    }

    private static LaunchMode? ReadLaunchMode(JsonNode? node, string key, string layer, List<string> warnings)
    {
        if (node is null) return null;
        if (node is JsonValue v && v.TryGetValue<string>(out var s))
        {
            foreach (var name in Enum.GetNames<LaunchMode>())
                if (string.Equals(name, s?.Trim(), StringComparison.OrdinalIgnoreCase))
                    return Enum.Parse<LaunchMode>(name);
            warnings.Add($"Ignored '{key}' in {layer}: unknown value (expected Auto or Clipboard).");
            return null;
        }
        warnings.Add($"Ignored '{key}' in {layer}: expected a string.");
        return null;
    }

    private static IReadOnlyList<string>? ReadStringList(JsonNode? node, string key, string layer, List<string> warnings)
    {
        if (node is null) return null;
        if (node is JsonArray arr)
        {
            var list = new List<string>();
            foreach (var item in arr)
            {
                if (item is JsonValue v && v.TryGetValue<string>(out var s))
                {
                    if (!string.IsNullOrWhiteSpace(s)) list.Add(s);
                }
                else
                {
                    warnings.Add($"Ignored '{key}' in {layer}: expected an array of strings.");
                    return null;
                }
            }
            return list;
        }
        warnings.Add($"Ignored '{key}' in {layer}: expected an array of strings.");
        return null;
    }
}
