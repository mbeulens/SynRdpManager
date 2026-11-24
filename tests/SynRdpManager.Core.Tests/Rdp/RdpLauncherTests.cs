using Microsoft.Extensions.Time.Testing;
using SynRdpManager.Core.Models;
using SynRdpManager.Core.Platform;
using SynRdpManager.Core.Rdp;
using SynRdpManager.Core.Settings;

namespace SynRdpManager.Core.Tests.Rdp;

public class RdpLauncherTests
{
    private const string Pw = "S3cret!pw";

    private sealed class FakeCreds : ICredentialStore
    {
        public Dictionary<string, StoredCredential> Items = new();
        public List<string> Log = new();
        public StoredCredential? Read(string target) => Items.GetValueOrDefault(target);
        public void Write(StoredCredential c) { Items[c.Target] = c; Log.Add("write " + c.Target); }
        public void Delete(string target) { Items.Remove(target); Log.Add("delete " + target); }
        public IReadOnlyList<string> TargetsWithComment(string comment) =>
            Items.Values.Where(v => v.Comment == comment).Select(v => v.Target).ToList();
    }

    private sealed class FakeLauncher(FakeCreds creds) : IProcessLauncher
    {
        public List<(string Exe, string[] Args)> Started = new();
        public bool CredentialPresentAtStart;
        public void Start(string exe, IReadOnlyList<string> args)
        {
            CredentialPresentAtStart = creds.Items.ContainsKey("TERMSRV/server01");
            Started.Add((exe, args.ToArray()));
        }
    }

    private sealed class FakeClipboard : IClipboardService
    {
        public string? Content;
        public void SetSecret(string text) => Content = text;
        public void ClearIfEquals(string text) { if (Content == text) Content = null; }
    }

    private readonly FakeCreds _creds = new();
    private readonly FakeClipboard _clip = new();
    private readonly FakeTimeProvider _time = new();
    private readonly FakeLauncher _launcher;
    private readonly RdpLauncher _sut;

    public RdpLauncherTests()
    {
        _launcher = new FakeLauncher(_creds);
        _sut = new RdpLauncher(_creds, _launcher, _clip, _time);
    }

    private static ServerEntry Entry(HostAddress? a = null, string host = "server01", int? port = 3390) =>
        new("a", "acc", "v", "vault", "i", "Title", ConnectionKind.Rdp, a ?? new HostAddress(host, port));

    private static readonly RdpCredentials Creds = new("DOMAIN\\user", Pw);
    private static readonly TimeSpan Delay = TimeSpan.FromSeconds(15);

    [Fact]
    public async Task Auto_writes_credential_then_starts_mstsc_then_deletes_after_delay()
    {
        await _sut.ConnectAsync(Entry(), Creds, LaunchMode.Auto, Delay, CancellationToken.None);

        Assert.True(_launcher.CredentialPresentAtStart);
        var (exe, args) = Assert.Single(_launcher.Started);
        Assert.Equal("mstsc.exe", exe);
        Assert.Equal(new[] { "/v:server01:3390" }, args);
        var c = _creds.Items["TERMSRV/server01"];
        Assert.Equal("SynRdpManager", c.Comment);
        Assert.Equal("DOMAIN\\user", c.UserName);
        Assert.Equal(Pw, c.Secret);

        _time.Advance(TimeSpan.FromSeconds(14));
        Assert.True(_creds.Items.ContainsKey("TERMSRV/server01"));
        _time.Advance(TimeSpan.FromSeconds(1));
        Assert.False(_creds.Items.ContainsKey("TERMSRV/server01"));
    }

    [Fact]
    public async Task Auto_backs_up_and_restores_existing_foreign_credential()
    {
        var foreign = new StoredCredential("TERMSRV/server01", "other", "foreignpw", "mine");
        _creds.Items[foreign.Target] = foreign;

        await _sut.ConnectAsync(Entry(), Creds, LaunchMode.Auto, Delay, CancellationToken.None);
        Assert.Equal("SynRdpManager", _creds.Items[foreign.Target].Comment);

        _time.Advance(Delay);
        Assert.Equal(foreign, _creds.Items[foreign.Target]);
    }

    [Fact]
    public async Task Existing_credential_with_our_comment_is_not_backed_up()
    {
        _creds.Items["TERMSRV/server01"] = new("TERMSRV/server01", "old", "oldpw", RdpLauncher.Comment);

        await _sut.ConnectAsync(Entry(), Creds, LaunchMode.Auto, Delay, CancellationToken.None);
        _time.Advance(Delay);

        Assert.False(_creds.Items.ContainsKey("TERMSRV/server01"));
    }

    [Fact]
    public async Task Null_credentials_starts_mstsc_without_writing()
    {
        await _sut.ConnectAsync(Entry(), null, LaunchMode.Auto, Delay, CancellationToken.None);

        Assert.Single(_launcher.Started);
        Assert.Empty(_creds.Log);
        Assert.Empty(_creds.Items);
    }

    [Fact]
    public async Task Clipboard_mode_sets_clipboard_and_clears_after_30s()
    {
        await _sut.ConnectAsync(Entry(), Creds, LaunchMode.Clipboard, Delay, CancellationToken.None);

        Assert.Equal(Pw, _clip.Content);
        Assert.Single(_launcher.Started);
        Assert.Empty(_creds.Log);
        _time.Advance(TimeSpan.FromSeconds(29));
        Assert.Equal(Pw, _clip.Content);
        _time.Advance(TimeSpan.FromSeconds(1));
        Assert.Null(_clip.Content);
    }

    [Fact]
    public async Task Clipboard_mode_with_null_credentials_just_starts_mstsc()
    {
        await _sut.ConnectAsync(Entry(), null, LaunchMode.Clipboard, Delay, CancellationToken.None);

        Assert.Single(_launcher.Started);
        Assert.Null(_clip.Content);
    }

    [Theory]
    [InlineData(LaunchMode.Auto)]
    [InlineData(LaunchMode.Clipboard)]
    public async Task Password_never_in_process_args(LaunchMode mode)
    {
        await _sut.ConnectAsync(Entry(), Creds, mode, Delay, CancellationToken.None);

        var (exe, args) = Assert.Single(_launcher.Started);
        Assert.DoesNotContain(Pw, exe);
        Assert.All(args, a => Assert.DoesNotContain(Pw, a));
    }

    [Fact]
    public void StoredCredential_ToString_hides_secret()
    {
        Assert.DoesNotContain(Pw, new StoredCredential("t", "u", Pw, null).ToString());
    }

    [Fact]
    public void CleanupLeftovers_deletes_all_targets_with_our_comment()
    {
        _creds.Items["TERMSRV/a"] = new("TERMSRV/a", "u", "p", RdpLauncher.Comment);
        _creds.Items["TERMSRV/b"] = new("TERMSRV/b", "u", "p", RdpLauncher.Comment);
        _creds.Items["TERMSRV/c"] = new("TERMSRV/c", "u", "p", "other");

        _sut.CleanupLeftovers();

        Assert.Equal(new[] { "TERMSRV/c" }, _creds.Items.Keys.ToArray());
    }

    [Fact]
    public async Task CleanupPendingNow_deletes_and_restores_immediately()
    {
        var foreign = new StoredCredential("TERMSRV/server01", "other", "foreignpw", null);
        _creds.Items[foreign.Target] = foreign;
        await _sut.ConnectAsync(Entry(), Creds, LaunchMode.Auto, Delay, CancellationToken.None);
        await _sut.ConnectAsync(Entry(host: "server02", port: null), Creds, LaunchMode.Auto, Delay, CancellationToken.None);
        await _sut.ConnectAsync(Entry(host: "server03", port: null), Creds, LaunchMode.Clipboard, Delay, CancellationToken.None);

        _sut.CleanupPendingNow();

        Assert.Equal(foreign, _creds.Items["TERMSRV/server01"]);
        Assert.False(_creds.Items.ContainsKey("TERMSRV/server02"));
        Assert.Null(_clip.Content);

        // Idempotent: neither a second call nor the elapsed timers run cleanup again.
        var log = _creds.Log.Count;
        _sut.CleanupPendingNow();
        _time.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(log, _creds.Log.Count);
    }

    [Fact]
    public async Task Second_connect_same_target_restarts_delay_and_keeps_original_backup()
    {
        var foreign = new StoredCredential("TERMSRV/server01", "other", "foreignpw", null);
        _creds.Items[foreign.Target] = foreign;

        await _sut.ConnectAsync(Entry(), Creds, LaunchMode.Auto, Delay, CancellationToken.None);
        _time.Advance(TimeSpan.FromSeconds(10));
        await _sut.ConnectAsync(Entry(), new RdpCredentials("u2", "pw2"), LaunchMode.Auto, Delay, CancellationToken.None);

        // First timer would have fired at 15 s; it must not delete the second credential.
        _time.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal("u2", _creds.Items[foreign.Target].UserName);

        _time.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(foreign, _creds.Items[foreign.Target]);
    }

    [Fact]
    public async Task Entry_without_address_throws_InvalidOperationException()
    {
        var e = new ServerEntry("a", "acc", "v", "vault", "i", "T", ConnectionKind.Rdp, null);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _sut.ConnectAsync(e, Creds, LaunchMode.Auto, Delay, CancellationToken.None));
        Assert.Empty(_launcher.Started);
    }
}
