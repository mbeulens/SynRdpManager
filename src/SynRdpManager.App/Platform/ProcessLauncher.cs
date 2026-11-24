using System.Diagnostics;
using SynRdpManager.Core.Platform;

namespace SynRdpManager.App.Platform;

public sealed class ProcessLauncher : IProcessLauncher
{
    public void Start(string exe, IReadOnlyList<string> args)
    {
        var psi = new ProcessStartInfo(exe) { UseShellExecute = false };
        foreach (var a in args) psi.ArgumentList.Add(a);
        // Fire and forget: release our handle, the child keeps running.
        Process.Start(psi)?.Dispose();
    }
}
