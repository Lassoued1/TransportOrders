using CommunityToolkit.Mvvm.ComponentModel;
using TransportOrders.Core;

namespace TransportOrders.App.ViewModels;

/// <summary>Read-only projection of a <see cref="TransportOrder"/> for the grid.</summary>
public sealed class OrderRowViewModel(TransportOrder order) : ObservableObject
{
    public TransportOrder Order { get; } = order;

    public string OrderNumber => Order.OrderNumber;

    public string Material => Order.Material;

    public int Quantity => Order.Quantity;

    public string SourceLocation => Order.SourceLocation;

    public string TargetLocation => Order.TargetLocation;

    public OrderStatus Status => Order.Status;

    public DateTime CreatedAtLocal => Order.CreatedAt.ToLocalTime();

    public DateTime UpdatedAtLocal => Order.UpdatedAt.ToLocalTime();

    /// <summary>Notifies the view that the underlying order changed.</summary>
    public void Refresh() => OnPropertyChanged(string.Empty);
}
