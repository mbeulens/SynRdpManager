using SynRdpManager.Core.Models;

namespace SynRdpManager.Core.Hosts;

public static class HostParser
{
    public static HostAddress? Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var s = raw.Trim();

        var scheme = s.IndexOf("://", StringComparison.Ordinal);
        if (scheme >= 0) s = s[(scheme + 3)..];

        var end = s.IndexOfAny(['/', '?', '#']);
        if (end >= 0) s = s[..end];

        var at = s.LastIndexOf('@');
        if (at >= 0) s = s[(at + 1)..];

        if (s.Length == 0) return null;

        string host;
        string? portText = null;

        if (s[0] == '[')
        {
            var close = s.IndexOf(']');
            if (close < 0) return null;
            host = s[1..close];
            var rest = s[(close + 1)..];
            if (rest.Length > 0)
            {
                if (rest[0] != ':') return null;
                portText = rest[1..];
            }
        }
        else if (s.Count(c => c == ':') > 1)
        {
            host = s;
        }
        else
        {
            var colon = s.LastIndexOf(':');
            if (colon >= 0)
            {
                host = s[..colon];
                portText = s[(colon + 1)..];
            }
            else host = s;
        }

        if (host.Length == 0) return null;

        int? port = null;
        if (portText is not null)
        {
            if (!int.TryParse(portText, System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out var p) || p < 1 || p > 65535)
                return null;
            port = p;
        }

        return new HostAddress(host, port);
    }
}
