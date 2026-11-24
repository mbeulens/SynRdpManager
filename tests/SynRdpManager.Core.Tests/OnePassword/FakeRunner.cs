using SynRdpManager.Core.Platform;

namespace SynRdpManager.Core.Tests.OnePassword;

internal sealed class FakeRunner(Func<IReadOnlyList<string>, ProcessResult> respond) : IProcessRunner
{
    public List<(string Exe, string[] Args, string? Stdin, TimeSpan Timeout)> Calls { get; } = [];

    public Task<ProcessResult> RunAsync(string exe, IReadOnlyList<string> args, string? stdin, TimeSpan timeout, CancellationToken ct)
    {
        lock (Calls) Calls.Add((exe, args.ToArray(), stdin, timeout));
        return Task.FromResult(respond(args));
    }
}
