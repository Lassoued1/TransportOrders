using TransportOrders.App.Services;
using TransportOrders.Core;

namespace TransportOrders.App.ViewModels;

/// <summary>The heat's genealogy as a tree, with the currently selected piece highlighted.</summary>
public sealed class GenealogyViewModel(HeatGenealogy genealogy, string? currentPiece, ILocalizer localizer)
{
    public string Title { get; } = localizer.Format("Genealogy_Title", genealogy.HeatNumber);

    public string Summary { get; } = localizer.Format("Genealogy_Summary", genealogy.PieceCount);

    public IReadOnlyList<PieceNodeViewModel> Roots { get; } =
        genealogy.Roots.Select(r => new PieceNodeViewModel(r, currentPiece, localizer)).ToList();
}

public sealed class PieceNodeViewModel(PieceNode node, string? currentPiece, ILocalizer localizer)
{
    public string PieceNumber { get; } = node.PieceNumber;

    public string Material { get; } = node.Material ?? localizer["Genealogy_NoOrder"];

    public bool IsCurrent { get; } = node.PieceNumber == currentPiece;

    public IReadOnlyList<string> OrderLines { get; } = node.Orders
        .Select(o => localizer.Format("Genealogy_OrderLine", o.OrderNumber, o.SourceLocation, o.TargetLocation,
            OrderRowViewModel.StatusName(localizer, o.Status)))
        .ToList();

    public IReadOnlyList<PieceNodeViewModel> Children { get; } =
        node.Children.Select(c => new PieceNodeViewModel(c, currentPiece, localizer)).ToList();
}
