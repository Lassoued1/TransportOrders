namespace TransportOrders.Core;

public sealed class InvalidStatusTransitionException : InvalidOperationException
{
    public InvalidStatusTransitionException(string orderNumber, OrderStatus from, OrderStatus to)
        : base($"Order {orderNumber} cannot change status from {from} to {to}.")
    {
        From = from;
        To = to;
    }

    public OrderStatus From { get; }

    public OrderStatus To { get; }
}
