using CommunityToolkit.Mvvm.ComponentModel;

namespace TransportOrders.App.ViewModels;

/// <summary>A selectable value with a label that follows the UI language (status filter, language switch).</summary>
public sealed partial class FilterOption(string key, string label) : ObservableObject
{
    public string Key { get; } = key;

    [ObservableProperty]
    private string _label = label;

    // UI Automation and screen readers use this as the item name.
    public override string ToString() => Label;
}
