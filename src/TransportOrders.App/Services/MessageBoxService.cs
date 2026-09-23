using System.Windows;

namespace TransportOrders.App.Services;

public sealed class MessageBoxService(ILocalizer localizer) : IUserMessageService
{
    private string Caption => localizer["App_Title"];

    public void ShowError(string message) =>
        MessageBox.Show(Application.Current.MainWindow, message, Caption, MessageBoxButton.OK, MessageBoxImage.Error);

    public void ShowInfo(string message) =>
        MessageBox.Show(Application.Current.MainWindow, message, Caption, MessageBoxButton.OK, MessageBoxImage.Information);

    public bool Confirm(string message) =>
        MessageBox.Show(Application.Current.MainWindow, message, Caption, MessageBoxButton.YesNo, MessageBoxImage.Question)
            == MessageBoxResult.Yes;
}
