using SynRdpManager.Core.Models;
using SynRdpManager.Core.Platform;
using SynRdpManager.Core.Settings;

namespace SynRdpManager.Core.Rdp;

public sealed class RdpLauncher(
    ICredentialStore creds,
    IProcessLauncher launcher,
    IClipboardService clipboard,
    TimeProvider time,
    string mstscExe = "mstsc.exe")
{
    public const string Comment = "SynRdpManager";
    private static readonly TimeSpan ClipboardClearDelay = TimeSpan.FromSeconds(30);
    private const string ClipboardKey = "clipboard";

    private sealed class Pending(Action cleanup, StoredCredential? backup)
    {
        public Action Cleanup { get; } = cleanup;
        public StoredCredential? Backup { get; } = backup;
        public ITimer? Timer { get; set; }
    }

    private readonly object _lock = new();
    private readonly Dictionary<string, Pending> _pending = new();

    public Task ConnectAsync(ServerEntry e, RdpCredentials? c, LaunchMode mode, TimeSpan cleanupDelay, CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            Connect(e, c, mode, cleanupDelay);
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            return Task.FromException(ex);
        }
    }

    private void Connect(ServerEntry e, RdpCredentials? c, LaunchMode mode, TimeSpan cleanupDelay)
    {
        var address = e.Address ?? throw new InvalidOperationException("Server entry has no address.");
        string[] args = ["/v:" + address.MstscTarget];

        if (c is null)
        {
            launcher.Start(mstscExe, args);
        }
        else if (mode == LaunchMode.Clipboard)
        {
            lock (_lock)
            {
                clipboard.SetSecret(c.Password);
                var password = c.Password;
                Schedule(ClipboardKey, new Pending(() => clipboard.ClearIfEquals(password), null), ClipboardClearDelay);
            }
            launcher.Start(mstscExe, args);
        }
        else
        {
            var target = address.CredentialTarget;
            lock (_lock)
            {
                // A pending cleanup for the same target owns the original backup; keep it.
                StoredCredential? backup;
                if (_pending.TryGetValue(target, out var previous))
                    backup = previous.Backup;
                else
                {
                    var existing = creds.Read(target);
                    backup = existing is not null && existing.Comment != Comment ? existing : null;
                }

                creds.Write(new StoredCredential(target, c.UserName, c.Password, Comment));
                Schedule(target, new Pending(() => Restore(target, backup), backup), cleanupDelay);
            }

            try
            {
                launcher.Start(mstscExe, args);
            }
            catch
            {
                RunPending(target);
                throw;
            }
        }
    }

    // Caller holds _lock. Replaces any pending cleanup for the key and (re)starts the delay.
    private void Schedule(string key, Pending p, TimeSpan delay)
    {
        if (_pending.TryGetValue(key, out var old)) old.Timer?.Dispose();
        _pending[key] = p;
        p.Timer = time.CreateTimer(_ => RunPending(key, p), null, delay, Timeout.InfiniteTimeSpan);
    }

    private void Restore(string target, StoredCredential? backup)
    {
        if (backup is not null) creds.Write(backup);
        else creds.Delete(target);
    }

    private void RunPending(string key, Pending? expected = null)
    {
        lock (_lock)
        {
            if (!_pending.TryGetValue(key, out var p) || (expected is not null && !ReferenceEquals(p, expected)))
                return;
            _pending.Remove(key);
            p.Timer?.Dispose();
            p.Cleanup();
        }
    }

    public void CleanupLeftovers()
    {
        foreach (var target in creds.TargetsWithComment(Comment))
            creds.Delete(target);
    }

    public void CleanupPendingNow()
    {
        string[] keys;
        lock (_lock) keys = _pending.Keys.ToArray();
        foreach (var key in keys) RunPending(key);
    }
}
