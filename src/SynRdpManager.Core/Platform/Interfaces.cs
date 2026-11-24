using System.Text;

namespace SynRdpManager.Core.Platform;

public sealed record ProcessResult(int ExitCode, string StdOut, string StdErr, bool TimedOut)
{
    // Never print StdOut: it may contain secrets.
    bool PrintMembers(StringBuilder sb)
    {
        sb.Append($"ExitCode = {ExitCode}, TimedOut = {TimedOut}");
        return true;
    }
}

public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(string exe, IReadOnlyList<string> args, string? stdin, TimeSpan timeout, CancellationToken ct);
}

public interface IDataProtector
{
    byte[] Protect(byte[] data);
    byte[] Unprotect(byte[] data);
}

public interface IPuttySessionStore
{
    bool Exists(string name);
    IReadOnlyDictionary<string, object> Read(string name);
    // Upserts the given values only; never deletes other values. Names are unescaped (the store escapes).
    void Write(string name, IReadOnlyDictionary<string, object> values);
}

/// <param name="Persist">Native CRED_PERSIST_* value as read; null = the store's default (session).</param>
public sealed record StoredCredential(string Target, string UserName, string Secret, string? Comment, uint? Persist = null)
{
    // Never print Secret.
    bool PrintMembers(StringBuilder sb)
    {
        sb.Append($"Target = {Target}, UserName = {UserName}, Comment = {Comment}, Persist = {Persist}");
        return true;
    }
}

/// <remarks>
/// Implementations must not block on the UI thread: RdpLauncher calls them while holding an internal lock.
/// </remarks>
public interface ICredentialStore
{
    StoredCredential? Read(string target);
    void Write(StoredCredential c);
    void Delete(string target);
    IReadOnlyList<string> TargetsWithComment(string comment);
}

public interface IProcessLauncher
{
    void Start(string exe, IReadOnlyList<string> args);
}

public interface IClipboardService
{
    void SetSecret(string text);
    void ClearIfEquals(string text);
}
