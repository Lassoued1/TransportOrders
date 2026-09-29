using CommunityToolkit.Mvvm.ComponentModel;
using TransportOrders.App.Services;
using TransportOrders.Core;

namespace TransportOrders.App.ViewModels;

public sealed record HistoryEntryViewModel(string When, string Change, string By, string? Note);

/// <summary>Read-only, localized projection of a <see cref="TransportOrder"/> for the grid and the details panel.</summary>
/// <param name="transportOf">Finds the transport an order is loaded on, for the details panel.</param>
public sealed class OrderRowViewModel(TransportOrder order, ILocalizer localizer, Func<DateTime> utcNow, Func<string, Transport?> transportOf)
    : ObservableObject
{
    public TransportOrder Order { get; } = order;

    public string OrderNumber => Order.OrderNumber;

    public string HeatNumber => Order.HeatNumber;

    public string PieceNumber => Order.PieceNumber;

    public string PieceText => Order.ParentPieceNumber is { } parent
        ? $"{Order.PieceNumber} · {localizer.Format("Details_FromPiece", parent)}"
        : Order.PieceNumber;

    public string Material => Order.Material;

    public decimal Weight => Order.Weight;

    public decimal LoadingLength => Order.LoadingLength;

    public string WeightText => localizer.Format("Number_OneDecimal", Order.Weight);

    public string LengthText => localizer.Format("Number_OneDecimal", Order.LoadingLength);

    public string LoadText => localizer.Format("Details_LoadValue", Order.LoadingLength, Order.Weight, Order.Quantity);

    public string SourceLocation => Order.SourceLocation;

    public string TargetLocation => Order.TargetLocation;

    public string Route => $"{Order.SourceLocation} → {Order.TargetLocation}";

    public OrderStatus Status => Order.Status;

    public string StatusText => StatusName(localizer, Order.Status);

    public OrderPriority Priority => Order.Priority;

    public bool IsUrgent => Order.Priority == OrderPriority.Urgent;

    public string PriorityText => localizer[$"Priority_{Order.Priority}"];

    public bool IsOverdue => Order.IsOverdue(utcNow());

    public string? HoldReason => Order.HoldReason;

    public bool IsOnHold => Order.Status == OrderStatus.OnHold;

    public string? TransportNumber => Order.TransportNumber;

    public bool HasTransport => Order.TransportNumber is not null;

    /// <summary>E.g. "TR-2026-0002 · Heavy-load carrier · 23 % of the loading length".</summary>
    public string TransportText =>
        Order.TransportNumber is { } number && transportOf(number) is { } t
            ? localizer.Format("Details_TransportValue", number, localizer[$"Vehicle_{t.Vehicle}"], t.FillRate)
            : Order.TransportNumber ?? string.Empty;

    public string PartLoadText =>
        Order.TransportNumber is { } number && transportOf(number) is { PartLoadReason: { } reason }
            ? localizer.Format("Details_PartLoad", localizer[$"Reason_{reason}"])
            : string.Empty;

    public DateTime DueAt => Order.DueAt;

    public string DueText => Short(Order.DueAt);

    public string DueTextLong => Long(Order.DueAt);

    public DateTime CreatedAt => Order.CreatedAt;

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
                localizer.Format("History_By", h.ChangedBy),
                h.Note))
            .ToList();

    public static string StatusName(ILocalizer localizer, OrderStatus status) => localizer[$"Status_{status}"];

    /// <summary>Notifies the view that the order, its transport, the clock or the language changed.</summary>
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
