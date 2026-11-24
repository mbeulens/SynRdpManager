using Microsoft.Win32;
using SynRdpManager.Core.Platform;
using SynRdpManager.Core.Putty;

namespace SynRdpManager.App.Platform;

public sealed class RegistryPuttySessionStore : IPuttySessionStore
{
    private const string SessionsPath = @"Software\SimonTatham\PuTTY\Sessions\";

    private static string KeyPath(string name) => SessionsPath + PuttyNames.Escape(name);

    public bool Exists(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath(name));
        return key is not null;
    }

    public IReadOnlyDictionary<string, object> Read(string name)
    {
        var result = new Dictionary<string, object>();
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath(name));
        if (key is null) return result;

        foreach (var valueName in key.GetValueNames())
        {
            var kind = key.GetValueKind(valueName);
            var value = key.GetValue(valueName);
            switch (kind)
            {
                case RegistryValueKind.String when value is string s:
                    result[valueName] = s;
                    break;
                case RegistryValueKind.DWord when value is int i:
                    result[valueName] = i;
                    break;
            }
        }
        return result;
    }

    public void Write(string name, IReadOnlyDictionary<string, object> values)
    {
        // Validate first so nothing is written when a value type is unsupported.
        foreach (var (k, v) in values)
        {
            if (v is not (string or int))
                throw new ArgumentException($"Unsupported value type for '{k}': {v?.GetType().Name ?? "null"}", nameof(values));
        }

        using var key = Registry.CurrentUser.CreateSubKey(KeyPath(name), writable: true);
        foreach (var (k, v) in values)
        {
            switch (v)
            {
                case string s: key.SetValue(k, s, RegistryValueKind.String); break;
                case int i: key.SetValue(k, i, RegistryValueKind.DWord); break;
            }
        }
    }
}
