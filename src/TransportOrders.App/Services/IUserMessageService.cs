namespace TransportOrders.App.Services;

public interface IUserMessageService
{
    void ShowError(string message);

    void ShowInfo(string message);

    bool Confirm(string message);
}
