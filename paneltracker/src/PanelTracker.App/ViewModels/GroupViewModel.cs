using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using PanelTracker.Core.Groups;

namespace PanelTracker.App.ViewModels;

public sealed partial class GroupViewModel : ObservableObject
{
    public GroupViewModel(PanelGroup group)
    {
        Group = group;
        Refresh();
    }

    public PanelGroup Group { get; }

    public string Key => Group.Key;

    [ObservableProperty]
    public partial string DisplayName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial int Count { get; set; }

    public ObservableCollection<string> Members { get; } = [];

    public void Refresh()
    {
        DisplayName = Group.DisplayName;
        Count = Group.Panels.Count;
        Members.Clear();
        foreach (var id in Group.Panels)
        {
            Members.Add(id);
        }
    }
}
