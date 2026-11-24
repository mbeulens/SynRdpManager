using System.Text;
using SynRdpManager.Core.Models;

namespace SynRdpManager.Core.Putty;

public static class PuttyNames
{
    public const string DefaultSettings = "Default Settings";

    // Mirrors PuTTY's mungestr: escapes space, '\', '*', '?', '%', bytes < 0x20 or > 0x7E, and a leading '.'.
    public static string Escape(string name)
    {
        var sb = new StringBuilder();
        var first = true;
        foreach (var rune in name.EnumerateRunes())
        {
            var s = rune.ToString();
            var plain = rune.Value is >= 0x21 and <= 0x7E
                && rune.Value is not ('\\' or '*' or '?' or '%')
                && !(first && rune.Value == '.');
            if (plain) sb.Append(s);
            else foreach (var b in Encoding.UTF8.GetBytes(s)) sb.Append('%').Append(b.ToString("X2"));
            first = false;
        }
        return sb.ToString();
    }

    public static string BaseNameFor(ServerEntry e) => $"{e.VaultName} - {e.Title}";

    public static IReadOnlyDictionary<string, string> AssignNames(IEnumerable<ServerEntry> entries, string templateName)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { templateName, DefaultSettings };
        var result = new Dictionary<string, string>();
        foreach (var e in entries.OrderBy(x => x.ItemId, StringComparer.Ordinal))
        {
            var baseName = BaseNameFor(e);
            var name = baseName;
            for (var n = 2; !used.Add(name); n++) name = $"{baseName} ({n})";
            result[e.ItemId] = name;
        }
        return result;
    }
}
