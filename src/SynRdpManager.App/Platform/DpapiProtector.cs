using System.Security.Cryptography;
using System.Text;
using SynRdpManager.Core.Platform;

namespace SynRdpManager.App.Platform;

public sealed class DpapiProtector : IDataProtector
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("SynRdpManager.cache.v1");

    public byte[] Protect(byte[] data) =>
        ProtectedData.Protect(data, Entropy, DataProtectionScope.CurrentUser);

    public byte[] Unprotect(byte[] data) =>
        ProtectedData.Unprotect(data, Entropy, DataProtectionScope.CurrentUser);
}
