namespace TransportOrders.App.Services;

/// <summary>Who is making changes, recorded in the order history.</summary>
public interface ICurrentUser
{
    string Name { get; }
}

public sealed class WindowsCurrentUser : ICurrentUser
{
    public string Name { get; } = Environment.UserName;
}
