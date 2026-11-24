using SynRdpManager.Core.Models;

namespace SynRdpManager.App.ViewModels;

public sealed class EntryViewModel(MainViewModel owner, ServerEntry entry)
{
    public MainViewModel Owner { get; } = owner;
    public ServerEntry Entry { get; } = entry;

    public string Icon => Entry.Kind == ConnectionKind.Rdp ? "🖥" : "🐧";
    public string Title => Entry.Title;
    public string HostText => Entry.Address?.MstscTarget ?? "no hostname";
    public bool HasAddress => Entry.Address is not null;
    public bool IsSsh => Entry.Kind == ConnectionKind.Ssh;
}
