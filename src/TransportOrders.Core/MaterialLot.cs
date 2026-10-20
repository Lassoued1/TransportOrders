namespace TransportOrders.Core;

/// <summary>Material identity independent of its basket and transport orders.</summary>
public sealed partial class MaterialLot
{
    private MaterialLot() { }
    public MaterialLot(string number, string batchNumber, string material)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(number);
        ArgumentException.ThrowIfNullOrWhiteSpace(batchNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(material);
        Number = number.Trim();
        BatchNumber = batchNumber.Trim();
        Material = material.Trim();
    }
    public string Number { get; private set; } = string.Empty;
    public string BatchNumber { get; private set; } = string.Empty;
    public string Material { get; private set; } = string.Empty;
}
