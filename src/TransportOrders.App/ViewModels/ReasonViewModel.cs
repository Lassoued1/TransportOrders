using CommunityToolkit.Mvvm.ComponentModel;

namespace TransportOrders.App.ViewModels;

/// <summary>Asks for a mandatory free-text reason, e.g. why material is put on hold.</summary>
public sealed partial class ReasonViewModel(string title, string prompt) : ObservableObject
{
    public string Title { get; } = title;

    public string Prompt { get; } = prompt;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConfirm))]
    private string _reason = string.Empty;

    public bool CanConfirm => !string.IsNullOrWhiteSpace(Reason);
}
