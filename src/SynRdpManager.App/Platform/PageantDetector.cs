using System.Diagnostics;

namespace SynRdpManager.App.Platform;

public static class PageantDetector
{
    public static bool IsRunning()
    {
        var processes = Process.GetProcessesByName("pageant");
        try
        {
            return processes.Length > 0;
        }
        finally
        {
            foreach (var p in processes) p.Dispose();
        }
    }
}
