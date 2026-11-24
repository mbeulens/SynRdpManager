using CommunityToolkit.Mvvm.ComponentModel;
using SynRdpManager.Core.Catalog;

namespace SynRdpManager.App.ViewModels;

public sealed class GroupViewModel : ObservableObject
{
    private readonly Action<GroupViewModel> _expandedChanged;
    private bool _isExpanded;

    public GroupViewModel(MainViewModel owner, VaultGroup group, bool isExpanded, Action<GroupViewModel> expandedChanged)
    {
        Owner = owner;
        Group = group;
        _isExpanded = isExpanded;
        _expandedChanged = expandedChanged;
        Entries = group.Entries.Select(e => new EntryViewModel(owner, e)).ToList();
    }

    public MainViewModel Owner { get; }
    public VaultGroup Group { get; }
    public IReadOnlyList<EntryViewModel> Entries { get; }

    public string DisplayName => Group.DisplayName;
    public string CountText => $"({Entries.Count})";

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (SetProperty(ref _isExpanded, value)) _expandedChanged(this);
        }
    }
}
