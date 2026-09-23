using CommunityToolkit.Mvvm.ComponentModel;
using TransportOrders.App.Services;
using TransportOrders.Core;

namespace TransportOrders.App.ViewModels;

public sealed record HistoryEntryViewModel(string When, string Change, string By);

/// <summary>Read-only, localized projection of a <see cref="TransportOrder"/> for the grid and the details panel.</summary>
public sealed class OrderRowViewModel(TransportOrder order, ILocalizer localizer, Func<DateTime> utcNow) : ObservableObject
{
    public TransportOrder Order { get; } = order;

    public string OrderNumber => Order.OrderNumber;

    public string HeatNumber => Order.HeatNumber;

    public string Material => Order.Material;

    public int Quantity => Order.Quantity;

    public string QuantityText => localizer.Format("Details_Quantity", Order.Quantity);

    public string SourceLocation => Order.SourceLocation;

    public string TargetLocation => Order.TargetLocation;

    public string Route => $"{Order.SourceLocation} → {Order.TargetLocation}";

    public OrderStatus Status => Order.Status;

    public string StatusText => StatusName(localizer, Order.Status);

    public OrderPriority Priority => Order.Priority;

    public bool IsUrgent => Order.Priority == OrderPriority.Urgent;

    public string PriorityText => localizer[$"Priority_{Order.Priority}"];

    public bool IsOverdue => Order.IsOverdue(utcNow());

    public DateTime DueAt => Order.DueAt;

    public string DueText => Short(Order.DueAt);

    public string DueTextLong => Long(Order.DueAt);

    public DateTime CreatedAt => Order.CreatedAt;

    public string CreatedText => Short(Order.CreatedAt);

    public string CreatedTextLong => Long(Order.CreatedAt);

    public DateTime UpdatedAt => Order.UpdatedAt;

    public string UpdatedText => Short(Order.UpdatedAt);

    /// <summary>Newest first, as in an audit log.</summary>
    public IReadOnlyList<HistoryEntryViewModel> History =>
        Order.History
            .Reverse()
            .Select(h => new HistoryEntryViewModel(
                Long(h.ChangedAt),
                h.FromStatus is { } from
                    ? localizer.Format("History_Change", StatusName(localizer, from), StatusName(localizer, h.ToStatus))
                    : localizer.Format("History_Created", StatusName(localizer, h.ToStatus)),
                localizer.Format("History_By", h.ChangedBy)))
            .ToList();

    public static string StatusName(ILocalizer localizer, OrderStatus status) => localizer[$"Status_{status}"];

    /// <summary>Notifies the view that the order, the clock or the language changed.</summary>
    public void Refresh() => OnPropertyChanged(string.Empty);

    /// <summary>Time only for today (the usual case on a shift), date and time otherwise.</summary>
    private string Short(DateTime utc)
    {
        var local = utc.ToLocalTime();
        return local.Date == utcNow().ToLocalTime().Date
            ? local.ToString("HH:mm")
            : local.ToString(localizer["Format_Short"]);
    }

    private string Long(DateTime utc) => utc.ToLocalTime().ToString(localizer["Format_Long"]);
}
