using SynRdpManager.Core.Platform;

namespace SynRdpManager.Core.OnePassword;

public enum OpErrorKind { NotFound, IntegrationDisabled, Locked, NotSignedIn, TimedOut, Other }

public class OpException(OpErrorKind kind, string userMessage) : Exception(userMessage)
{
    public OpErrorKind Kind { get; } = kind;
    public string UserMessage { get; } = userMessage;
}

public static class OpErrorClassifier
{
    public static OpErrorKind Classify(ProcessResult r)
    {
        if (r.TimedOut) return OpErrorKind.TimedOut;
        var err = r.StdErr ?? "";
        if (Has(err, "No accounts configured")) return OpErrorKind.NotSignedIn;
        if (Has(err, "authorization prompt dismissed")) return OpErrorKind.Locked;
        if (Has(err, "connecting to desktop app")) return OpErrorKind.IntegrationDisabled;
        return OpErrorKind.Other;
    }

    static bool Has(string s, string needle) => s.Contains(needle, StringComparison.OrdinalIgnoreCase);

    internal static string MessageFor(OpErrorKind kind, ProcessResult? r = null) => kind switch
    {
        OpErrorKind.NotFound => "1Password CLI (op.exe) was not found. Install it or set its path in Settings.",
        OpErrorKind.IntegrationDisabled => "Cannot connect to the 1Password desktop app. Enable Settings > Developer > Integrate with 1Password CLI, then press Refresh.",
        OpErrorKind.Locked => "1Password is locked or the request was not approved. Unlock 1Password and press Refresh.",
        OpErrorKind.NotSignedIn => "No 1Password accounts are configured for the CLI. Add or sign in to an account in 1Password, then press Refresh.",
        OpErrorKind.TimedOut => "1Password did not respond in time. Unlock 1Password and press Refresh.",
        _ => r is null ? "1Password CLI failed." : $"1Password CLI failed (exit code {r.ExitCode})."
    };
}

public class OpClient(string opPath, IProcessRunner runner)
{
    static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(120);

    public async Task<IReadOnlyList<OpAccount>> ListAccountsAsync(CancellationToken ct) =>
        OpJson.ParseAccounts(await RunAsync(["account", "list", "--format", "json"], null, ct));

    public async Task<IReadOnlyList<OpItem>> GetItemsByTagAsync(OpAccount account, string tag, CancellationToken ct)
    {
        var listJson = await RunAsync(
            ["item", "list", "--tags", tag, "--format", "json", "--account", account.AccountUuid], null, ct);
        if (OpJson.ParseItemList(listJson).Count == 0) return [];
        var itemsJson = await RunAsync(
            ["item", "get", "-", "--format", "json", "--account", account.AccountUuid], listJson, ct);
        return OpJson.ParseItems(itemsJson);
    }

    public async Task<OpItem> GetItemAsync(OpAccount account, string vaultId, string itemId, CancellationToken ct)
    {
        var json = await RunAsync(
            ["item", "get", itemId, "--vault", vaultId, "--format", "json", "--account", account.AccountUuid], null, ct);
        return OpJson.ParseItems(json).FirstOrDefault()
            ?? throw new OpException(OpErrorKind.Other, "1Password returned no item.");
    }

    async Task<string> RunAsync(IReadOnlyList<string> args, string? stdin, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(opPath))
            throw new OpException(OpErrorKind.NotFound, OpErrorClassifier.MessageFor(OpErrorKind.NotFound));

        ProcessResult r;
        try
        {
            r = await runner.RunAsync(opPath, args, stdin, CallTimeout, ct);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            throw new OpException(OpErrorKind.NotFound, OpErrorClassifier.MessageFor(OpErrorKind.NotFound));
        }

        if (r.TimedOut || r.ExitCode != 0)
        {
            var kind = OpErrorClassifier.Classify(r);
            throw new OpException(kind, OpErrorClassifier.MessageFor(kind, r));
        }
        return r.StdOut;
    }
}
