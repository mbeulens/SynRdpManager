using System.Text.Json;
using SynRdpManager.Core.Models;
using SynRdpManager.Core.OnePassword;
using SynRdpManager.Core.Settings;

namespace SynRdpManager.Core.Catalog;

public sealed record RefreshResult(
    IReadOnlyList<ServerEntry> Entries,
    IReadOnlyDictionary<string, string?> SshUserNames, // key ItemId; transient, never cached
    IReadOnlyList<string> Warnings);

public class CatalogService(OpClient op)
{
    sealed record AccountOutcome(
        List<ServerEntry> Entries,
        Dictionary<string, string?> SshUserNames,
        OpException? Failure);

    public async Task<RefreshResult> RefreshAsync(AppSettings s, CancellationToken ct)
    {
        var accounts = (await op.ListAccountsAsync(ct)).Where(a => Selected(a, s.Accounts)).ToList();
        if (accounts.Count == 0)
            return new RefreshResult([], new Dictionary<string, string?>(), []);

        var outcomes = await Task.WhenAll(accounts.Select(a => RefreshAccountAsync(a, s, ct)));

        var entries = new List<ServerEntry>();
        var ssh = new Dictionary<string, string?>();
        var warnings = new List<string>();
        OpException? first = null;
        for (var i = 0; i < accounts.Count; i++)
        {
            var o = outcomes[i];
            if (o.Failure is not null)
            {
                first ??= o.Failure;
                warnings.Add($"Account {accounts[i].Email}: {o.Failure.UserMessage}");
                continue;
            }
            entries.AddRange(o.Entries);
            foreach (var kv in o.SshUserNames) ssh[kv.Key] = kv.Value;
        }

        if (first is not null && warnings.Count == accounts.Count) throw first;
        return new RefreshResult(entries, ssh, warnings);
    }

    public async Task<(OpAccount, OpItem)> GetLiveItemAsync(ServerEntry e, CancellationToken ct)
    {
        var account = (await op.ListAccountsAsync(ct)).FirstOrDefault(a => a.AccountUuid == e.AccountId)
            ?? throw new OpException(OpErrorKind.NotSignedIn,
                $"1Password account {e.AccountName} is not signed in to the CLI. Sign in to it in 1Password, then press Refresh.");
        var item = await op.GetItemAsync(account, e.VaultId, e.ItemId, ct);
        return (account, item);
    }

    async Task<AccountOutcome> RefreshAccountAsync(OpAccount account, AppSettings s, CancellationToken ct)
    {
        try
        {
            var rdpTask = op.GetItemsByTagAsync(account, s.Tag, ct);
            var sshTask = op.GetItemsByTagAsync(account, s.PuttyTag, ct);
            var rdp = await rdpTask;
            var sshItems = await sshTask;

            var entries = new List<ServerEntry>();
            var ssh = new Dictionary<string, string?>();
            entries.AddRange(rdp.Select(i => ItemMapper.ToEntry(account, i, ConnectionKind.Rdp)));
            foreach (var i in sshItems)
            {
                entries.Add(ItemMapper.ToEntry(account, i, ConnectionKind.Ssh));
                ssh[i.Id] = ItemMapper.ComposeUserName(i);
            }
            return new AccountOutcome(entries, ssh, null);
        }
        catch (OpException ex)
        {
            return new AccountOutcome([], [], ex);
        }
        catch (JsonException)
        {
            return new AccountOutcome([], [],
                new OpException(OpErrorKind.Other, "1Password returned data SynRdpManager could not read."));
        }
    }

    static bool Selected(OpAccount a, IReadOnlyList<string> filter) =>
        filter.Count == 0 || filter.Any(f =>
            string.Equals(f?.Trim(), a.AccountUuid, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(f?.Trim(), a.Email, StringComparison.OrdinalIgnoreCase));
}
