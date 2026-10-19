using CommunityToolkit.Mvvm.ComponentModel;
using PanelTracker.Core.Models;

namespace PanelTracker.App.ViewModels;

/// <summary>Счётчик одного статуса в верхней панели.</summary>
public sealed partial class StatusCounterViewModel : ObservableObject
{
    public StatusCounterViewModel(StatusDefinition status)
    {
        Status = status;
    }

    public StatusDefinition Status { get; }

    public string Name => Status.Name;

    public string Color => Status.Color;

    [ObservableProperty]
    public partial int Count { get; set; }

    [ObservableProperty]
    public partial double Percent { get; set; }

    [ObservableProperty]
    public partial string Label { get; set; } = "0";

    public void Update(StatusCount count)
    {
        Count = count.Count;
        Percent = count.Percent;
        Label = $"{count.Count} ({count.Percent:0}%)";
    }
}
