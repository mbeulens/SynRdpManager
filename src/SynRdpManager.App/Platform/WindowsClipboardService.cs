using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using SynRdpManager.Core.Platform;

namespace SynRdpManager.App.Platform;

public sealed class WindowsClipboardService : IClipboardService
{
    private const int MaxAttempts = 3;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(50);

    public void SetSecret(string text) => OnUiThread(() => Retry(() =>
    {
        var data = new DataObject();
        data.SetData(DataFormats.UnicodeText, text);
        data.SetData("ExcludeClipboardContentFromMonitorProcessing", new MemoryStream(new byte[4]));
        data.SetData("CanIncludeInClipboardHistory", new MemoryStream(new byte[4])); // DWORD 0 (little-endian)
        data.SetData("CanUploadToCloudClipboard", new MemoryStream(new byte[4]));
        Clipboard.SetDataObject(data, copy: true);
    }));

    public void ClearIfEquals(string text) => OnUiThread(() => Retry(() =>
    {
        if (Clipboard.ContainsText() && Clipboard.GetText() == text)
            Clipboard.Clear();
    }));

    private static void OnUiThread(Action action)
    {
        var dispatcher = Application.Current.Dispatcher;
        if (dispatcher.CheckAccess()) action();
        else dispatcher.Invoke(action);
    }

    private static void Retry(Action action)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                action();
                return;
            }
            catch (COMException) when (attempt < MaxAttempts)
            {
                Thread.Sleep(RetryDelay);
            }
        }
    }
}
