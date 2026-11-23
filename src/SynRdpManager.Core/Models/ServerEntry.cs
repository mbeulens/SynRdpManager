namespace SynRdpManager.Core.Models;

public enum ConnectionKind { Rdp, Ssh }

public sealed record HostAddress(string Host, int? Port)
{
    public string MstscTarget
    {
        get
        {
            if (Port is null) return Host;
            var host = Host.Contains(':') ? $"[{Host}]" : Host;
            return $"{host}:{Port}";
        }
    }

    public string CredentialTarget => "TERMSRV/" + Host;

    public int SshPort => Port ?? 22;
}

public sealed record ServerEntry(
    string AccountId,
    string AccountName,
    string VaultId,
    string VaultName,
    string ItemId,
    string Title,
    ConnectionKind Kind,
    HostAddress? Address);

public sealed record RdpCredentials(string UserName, string Password)
{
    public override string ToString() => $"RdpCredentials({UserName}, ***)";
}
