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
