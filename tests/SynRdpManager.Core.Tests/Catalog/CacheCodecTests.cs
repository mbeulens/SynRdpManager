using System.Text;
using SynRdpManager.Core.Catalog;
using SynRdpManager.Core.Models;
using SynRdpManager.Core.Platform;
using SynRdpManager.Core.Settings;

namespace SynRdpManager.Core.Tests.Catalog;

public class CacheCodecTests
{
    sealed class XorProtector : IDataProtector
    {
        public byte[] Protect(byte[] d) => d.Select(b => (byte)(b ^ 0x5A)).ToArray();
        public byte[] Unprotect(byte[] d) => Protect(d);
    }

    sealed class IdentityProtector : IDataProtector
    {
        public byte[] Protect(byte[] d) => d;
        public byte[] Unprotect(byte[] d) => d;
    }

    sealed class ThrowingProtector : IDataProtector
    {
        public byte[] Protect(byte[] d) => d;
        public byte[] Unprotect(byte[] d) => throw new System.Security.Cryptography.CryptographicException("nope");
    }

    static readonly DateTimeOffset Saved = new(2026, 11, 24, 10, 0, 0, TimeSpan.FromHours(1));

    static CacheSnapshot Snap(int schema = CacheCodec.SchemaVersion, string tag = "MstscDealer", string putty = "PuttyDealer") => new(
        schema, Saved, tag, putty,
        [
            new ServerEntry("A1", "a@x.nl", "v1", "Servers", "i1", "Alpha", ConnectionKind.Rdp, new HostAddress("alpha.local", 3390)),
            new ServerEntry("A1", "a@x.nl", "v1", "Servers", "i2", "Beta", ConnectionKind.Ssh, new HostAddress("beta.local", null)),
            new ServerEntry("B1", "b@x.nl", "v2", "Other", "i3", "NoAddr", ConnectionKind.Rdp, null),
        ]);

    [Fact]
    public void Cache_roundtrip()
    {
        var p = new XorProtector();
        var snap = Snap();
        var bytes = CacheCodec.Encode(snap, p);
        var back = CacheCodec.Decode(bytes, p);
        Assert.NotNull(back);
        Assert.Equal(snap.SchemaVersion, back.SchemaVersion);
        Assert.Equal(Saved, back.SavedAt);
        Assert.Equal("MstscDealer", back.Tag);
        Assert.Equal("PuttyDealer", back.PuttyTag);
        Assert.Equal(snap.Entries, back.Entries);
        Assert.Null(back.Entries[2].Address);
        Assert.Equal(new HostAddress("beta.local", null), back.Entries[1].Address);
    }

    [Fact]
    public void Cache_is_actually_protected()
    {
        var bytes = CacheCodec.Encode(Snap(), new XorProtector());
        Assert.DoesNotContain("alpha.local", Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public void Cache_contains_no_usernames_or_passwords()
    {
        // ServerEntry carries neither; the fixture values must never appear in the serialized cache.
        var bytes = CacheCodec.Encode(Snap(), new IdentityProtector());
        var text = Encoding.UTF8.GetString(bytes);
        Assert.DoesNotContain("fake-pass-1", text);
        Assert.DoesNotContain("svc", text);
        Assert.DoesNotContain("password", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("username", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("alpha.local", text);
    }

    [Fact]
    public void Decode_returns_null_on_garbage()
    {
        Assert.Null(CacheCodec.Decode([1, 2, 3, 4], new IdentityProtector()));
        Assert.Null(CacheCodec.Decode([], new IdentityProtector()));
        Assert.Null(CacheCodec.Decode(Encoding.UTF8.GetBytes("null"), new IdentityProtector()));
        Assert.Null(CacheCodec.Decode(Encoding.UTF8.GetBytes("{}"), new IdentityProtector()));
    }

    [Fact]
    public void Decode_returns_null_on_other_schema()
    {
        var p = new XorProtector();
        Assert.Null(CacheCodec.Decode(CacheCodec.Encode(Snap(schema: 99), p), p));
    }

    [Fact]
    public void Snapshot_for_other_tag_is_ignored()
    {
        var p = new XorProtector();
        var bytes = CacheCodec.Encode(Snap(), p);
        Assert.NotNull(CacheCodec.DecodeFor(bytes, p, new AppSettings()));
        Assert.Null(CacheCodec.DecodeFor(bytes, p, new AppSettings { Tag = "Other" }));
        Assert.Null(CacheCodec.DecodeFor(bytes, p, new AppSettings { PuttyTag = "Other" }));
    }

    [Fact]
    public void Decode_returns_null_when_Unprotect_throws()
    {
        var bytes = CacheCodec.Encode(Snap(), new IdentityProtector());
        Assert.Null(CacheCodec.Decode(bytes, new ThrowingProtector()));
    }
}
