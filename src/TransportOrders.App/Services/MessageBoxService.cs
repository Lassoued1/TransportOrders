using System.Windows;

namespace TransportOrders.App.Services;

public sealed class MessageBoxService : IUserMessageService
{
    private const string Caption = "Transport Orders";

    public void ShowError(string message) =>
        MessageBox.Show(Application.Current.MainWindow, message, Caption, MessageBoxButton.OK, MessageBoxImage.Error);

    public bool Confirm(string message) =>
        MessageBox.Show(Application.Current.MainWindow, message, Caption, MessageBoxButton.YesNo, MessageBoxImage.Question)
            == MessageBoxResult.Yes;
}
