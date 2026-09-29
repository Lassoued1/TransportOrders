namespace TransportOrders.Core;

/// <param name="Material">Null when no order of this piece is known (e.g. an intermediate ingot).</param>
/// <param name="Orders">Transport orders that moved this piece, oldest first.</param>
public sealed record PieceNode(string PieceNumber, string? Material, IReadOnlyList<TransportOrder> Orders, IReadOnlyList<PieceNode> Children);

/// <summary>
/// Everything made from one heat, as a tree: ingots at the top, the bars forged from them below.
/// This is the basis of the traceability customers expect, e.g. for an EN 10204 3.1 inspection certificate.
/// </summary>
public sealed record HeatGenealogy(string HeatNumber, IReadOnlyList<PieceNode> Roots)
{
    public int PieceCount => Count(Roots);

    public static HeatGenealogy Build(string heatNumber, IEnumerable<TransportOrder> orders)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(heatNumber);
        ArgumentNullException.ThrowIfNull(orders);

        var heat = heatNumber.Trim().ToUpperInvariant();
        var ofHeat = orders.Where(o => o.HeatNumber == heat).ToList();
        var byPiece = ofHeat.GroupBy(o => o.PieceNumber).ToDictionary(g => g.Key, g => g.OrderBy(o => o.CreatedAt).ToList());

        // Parents without an order of their own still appear, so the tree has no gaps.
        var parentOf = ofHeat.Where(o => o.ParentPieceNumber is not null)
            .GroupBy(o => o.PieceNumber)
            .ToDictionary(g => g.Key, g => g.First().ParentPieceNumber!);
        var pieces = byPiece.Keys.Union(parentOf.Values).ToHashSet();

        PieceNode Node(string piece) => new(
            piece,
            byPiece.TryGetValue(piece, out var moves) ? moves[^1].Material : null,
            byPiece.TryGetValue(piece, out var list) ? list : [],
            pieces.Where(p => parentOf.GetValueOrDefault(p) == piece).OrderBy(p => p).Select(Node).ToList());

        var roots = pieces.Where(p => !parentOf.ContainsKey(p)).OrderBy(p => p).Select(Node).ToList();
        return new HeatGenealogy(heat, roots);
    }

    private static int Count(IReadOnlyList<PieceNode> nodes) => nodes.Sum(n => 1 + Count(n.Children));
}
