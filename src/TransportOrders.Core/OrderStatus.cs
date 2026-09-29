namespace TransportOrders.Core;

public enum OrderStatus
{
    Planned,

    /// <summary>Blocked by quality assurance: the material must not be moved until it is released.</summary>
    OnHold,

    InTransit,
    Done,
    Cancelled
}
