using SynRdpManager.Core.Models;
using SynRdpManager.Core.Platform;
using SynRdpManager.Core.Settings;

namespace SynRdpManager.Core.Rdp;

public sealed class RdpLauncher(
    ICredentialStore creds,
    IProcessLauncher launcher,
    IClipboardService clipboard,
    TimeProvider time,
    string mstscExe = "mstsc.exe",
    Action<string>? onError = null)
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
    // Failed restores of foreign credentials; retried once by CleanupPendingNow.
    private readonly List<Pending> _retry = new();

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
            // The clipboard adapter may need the UI thread: never call it under _lock.
            clipboard.SetSecret(c.Password);
            var password = c.Password;
            lock (_lock)
                Schedule(ClipboardKey, new Pending(() => clipboard.ClearIfEquals(password), null), ClipboardClearDelay);
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
        Pending p;
        lock (_lock)
        {
            if (!_pending.TryGetValue(key, out var found) || (expected is not null && !ReferenceEquals(found, expected)))
                return;
            p = found;
            _pending.Remove(key);
            p.Timer?.Dispose();
        }

        // Run outside the lock and never let an exception escape (timer threads would crash the process).
        try
        {
            p.Cleanup();
        }
        catch (Exception ex)
        {
            Report($"Cleanup for '{key}' failed: {ex.GetType().Name}");
            if (p.Backup is not null)
                lock (_lock) _retry.Add(p);
        }
    }

    private void Report(string message)
    {
        try { onError?.Invoke(message); }
        catch { /* the error hook must not break cleanup */ }
    }

    public void CleanupLeftovers()
    {
        foreach (var target in creds.TargetsWithComment(Comment))
            creds.Delete(target);
    }

    public void CleanupPendingNow()
    {
        Pending[] retries;
        string[] keys;
        lock (_lock)
        {
            retries = _retry.ToArray();
            _retry.Clear();
            keys = _pending.Keys.ToArray();
        }

        foreach (var p in retries)
        {
            try { p.Cleanup(); }
            catch (Exception ex) { Report($"Retried restore failed: {ex.GetType().Name}"); }
        }

        foreach (var key in keys) RunPending(key);
    }
}
