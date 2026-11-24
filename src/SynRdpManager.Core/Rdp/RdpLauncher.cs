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

    private sealed class Pending(Action cleanup, StoredCredential? backup, bool underLock)
    {
        // Credential cleanup runs under _lock (atomic with connects); clipboard cleanup must not.
        public bool UnderLock { get; } = underLock;
        public Action Cleanup { get; } = cleanup;
        public StoredCredential? Backup { get; } = backup;
        public ITimer? Timer { get; set; }
    }

    private readonly object _lock = new();
    private readonly Dictionary<string, Pending> _pending = new();
    // Failed restores of foreign credentials by target; retried once by CleanupPendingNow.
    private readonly Dictionary<string, Pending> _retry = new();

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
                Schedule(ClipboardKey, new Pending(() => clipboard.ClearIfEquals(password), null, underLock: false), ClipboardClearDelay);
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
                else if (_retry.TryGetValue(target, out var failed))
                    backup = failed.Backup; // our credential is still stored; the foreign original is only in memory
                else
                {
                    var existing = creds.Read(target);
                    backup = existing is not null && existing.Comment != Comment ? existing : null;
                }

                creds.Write(new StoredCredential(target, c.UserName, c.Password, Comment));
                Schedule(target, new Pending(() => Restore(target, backup), backup, underLock: true), cleanupDelay);
                _retry.Remove(target); // only after Write and Schedule succeeded
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
        string? error = null;
        Pending? outside = null;
        lock (_lock)
        {
            if (!_pending.TryGetValue(key, out var p) || (expected is not null && !ReferenceEquals(p, expected)))
                return;
            _pending.Remove(key);
            p.Timer?.Dispose();
            if (p.UnderLock) error = RunLocked(key, p);
            else outside = p;
        }

        if (error is not null) Report(error);
        if (outside is null) return;

        // Never let an exception escape (timer threads would crash the process).
        try { outside.Cleanup(); }
        catch (Exception ex) { Report($"Cleanup for '{key}' failed: {ex.GetType().Name}"); }
    }

    // Caller holds _lock. Returns an error message on failure and queues foreign restores for retry.
    private string? RunLocked(string key, Pending p)
    {
        try
        {
            p.Cleanup();
            return null;
        }
        catch (Exception ex)
        {
            if (p.Backup is not null) _retry[key] = p;
            return $"Cleanup for '{key}' failed: {ex.GetType().Name}";
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
        {
            try { creds.Delete(target); }
            catch (Exception ex) { Report($"Leftover cleanup for '{target}' failed: {ex.GetType().Name}"); }
        }
    }

    public void CleanupPendingNow()
    {
        var errors = new List<string>();
        string[] keys;
        lock (_lock)
        {
            var retries = _retry.ToArray();
            _retry.Clear();
            foreach (var (key, p) in retries)
            {
                try { p.Cleanup(); }
                catch (Exception ex) { errors.Add($"Retried restore for '{key}' failed: {ex.GetType().Name}"); }
            }
            keys = _pending.Keys.ToArray();
        }

        foreach (var e in errors) Report(e);
        foreach (var key in keys) RunPending(key);
    }
}
