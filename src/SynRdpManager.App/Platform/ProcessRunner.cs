using System.IO;
using System.Diagnostics;
using System.Text;
using SynRdpManager.Core.Platform;

namespace SynRdpManager.App.Platform;

public sealed class ProcessRunner : IProcessRunner
{
    public async Task<ProcessResult> RunAsync(string exe, IReadOnlyList<string> args, string? stdin, TimeSpan timeout, CancellationToken ct)
    {
        var utf8 = new UTF8Encoding(false);
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = utf8,
            StandardOutputEncoding = utf8,
            StandardErrorEncoding = utf8,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var process = new Process { StartInfo = psi };
        process.Start(); // Win32Exception propagates to the caller.

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);

        // Start readers first so a full pipe can never block the child.
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        try
        {
            try
            {
                if (stdin is not null) await process.StandardInput.WriteAsync(stdin.AsMemory(), timeoutCts.Token);
            }
            catch (IOException)
            {
                // Child closed stdin early; its exit code/output tell the story.
            }
            finally
            {
                try { process.StandardInput.Close(); } catch (IOException) { }
            }

            await process.WaitForExitAsync(timeoutCts.Token);
            // Ensure redirected streams are fully drained (a grandchild holding the pipes open must not hang us).
            await Task.WhenAll(stdoutTask, stderrTask).WaitAsync(timeoutCts.Token);
            return new ProcessResult(process.ExitCode, await stdoutTask, await stderrTask, false);
        }
        catch (OperationCanceledException)
        {
            KillTree(process);
            if (ct.IsCancellationRequested) throw;
            var stdout = await DrainAsync(stdoutTask);
            var stderr = await DrainAsync(stderrTask);
            return new ProcessResult(-1, stdout, stderr, true);
        }
    }

    private static void KillTree(Process p)
    {
        try { p.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { } // already exited
        catch (System.ComponentModel.Win32Exception) { } // access denied / exiting
    }

    private static async Task<string> DrainAsync(Task<string> readTask)
    {
        try { return await readTask.WaitAsync(TimeSpan.FromSeconds(2)); }
        catch (Exception) { return string.Empty; }
    }
}
