using System.Windows;
using TransportOrders.App.ViewModels;
using TransportOrders.App.Views;

namespace TransportOrders.App.Services;

public sealed class WpfDialogService : IDialogService
{
    public string? AskForReason(string title, string prompt)
    {
        var viewModel = new ReasonViewModel(title, prompt);
        var window = new ReasonWindow { Owner = Application.Current.MainWindow, DataContext = viewModel };
        return window.ShowDialog() == true ? viewModel.Reason.Trim() : null;
    }

    public void ShowLoadPlan(LoadPlanViewModel viewModel) =>
        new LoadPlanWindow { Owner = Application.Current.MainWindow, DataContext = viewModel }.ShowDialog();

    public void ShowGenealogy(GenealogyViewModel viewModel) =>
        new GenealogyWindow { Owner = Application.Current.MainWindow, DataContext = viewModel }.ShowDialog();
}
