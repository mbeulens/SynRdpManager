using SynRdpManager.Core.Models;
using SynRdpManager.Core.Platform;

namespace SynRdpManager.Core.Putty;

public sealed record PuttySyncResult(bool TemplateMissing, int Created, int Updated);

public sealed class PuttySessionManager(IPuttySessionStore store, string templateName)
{
    private IReadOnlyDictionary<string, string> _names = new Dictionary<string, string>();

    public bool TemplateExists() => store.Exists(templateName);

    public void CreateTemplateFromDefaults()
    {
        if (store.Exists(PuttyNames.DefaultSettings))
            store.Write(templateName, store.Read(PuttyNames.DefaultSettings));
        else
            store.Write(templateName, new Dictionary<string, object> { ["Protocol"] = "ssh", ["PortNumber"] = 22 });
    }

    public string SessionNameFor(ServerEntry e) =>
        _names.TryGetValue(e.ItemId, out var n) ? n : PuttyNames.BaseNameFor(e);

    public PuttySyncResult Sync(IReadOnlyList<ServerEntry> sshEntries, IReadOnlyDictionary<string, string?> userNames)
    {
        if (!TemplateExists()) return new PuttySyncResult(true, 0, 0);

        var entries = sshEntries.Where(e => e.Address is not null).ToList();
        _names = PuttyNames.AssignNames(entries, templateName);

        IReadOnlyDictionary<string, object>? template = null;
        int created = 0, updated = 0;
        foreach (var e in entries)
        {
            var name = _names[e.ItemId];
            var user = userNames.TryGetValue(e.ItemId, out var u) ? u ?? "" : "";
            var host = e.Address!.Host;
            var port = e.Address.SshPort;

            if (!store.Exists(name))
            {
                template ??= store.Read(templateName);
                var values = new Dictionary<string, object>(template)
                {
                    ["HostName"] = host,
                    ["PortNumber"] = port,
                    ["Protocol"] = "ssh",
                    ["UserName"] = user,
                };
                store.Write(name, values);
                created++;
                continue;
            }

            var cur = store.Read(name);
            var changed = !(cur.TryGetValue("HostName", out var h) && h is string hs && hs == host)
                || !(cur.TryGetValue("PortNumber", out var p) && p is int pi && pi == port)
                || (cur.TryGetValue("UserName", out var cu) && cu is string cs ? cs : "") != user;
            if (!changed) continue;
            store.Write(name, new Dictionary<string, object>
            {
                ["HostName"] = host,
                ["PortNumber"] = port,
                ["UserName"] = user,
            });
            updated++;
        }
        return new PuttySyncResult(false, created, updated);
    }

    public void ReloadFromTemplate(ServerEntry e, string? userName)
    {
        var name = SessionNameFor(e);
        var values = new Dictionary<string, object>(store.Read(templateName));
        var existing = store.Exists(name) ? store.Read(name) : null;
        if (existing is not null && existing.TryGetValue("HostName", out var h)) values["HostName"] = h;
        else if (e.Address is not null) values["HostName"] = e.Address.Host;
        if (existing is not null && existing.TryGetValue("PortNumber", out var p)) values["PortNumber"] = p;
        else if (e.Address is not null) values["PortNumber"] = e.Address.SshPort;
        values["Protocol"] = "ssh";
        values["UserName"] = userName ?? "";
        store.Write(name, values);
    }
}
