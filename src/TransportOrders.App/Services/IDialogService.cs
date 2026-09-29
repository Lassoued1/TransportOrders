using TransportOrders.App.ViewModels;

namespace TransportOrders.App.Services;

/// <summary>Opens the app's dialogs; faked in the view-model tests.</summary>
public interface IDialogService
{
    /// <returns>The reason entered, or null when the user cancelled.</returns>
    string? AskForReason(string title, string prompt);

    void ShowLoadPlan(LoadPlanViewModel viewModel);

    void ShowGenealogy(GenealogyViewModel viewModel);
}
