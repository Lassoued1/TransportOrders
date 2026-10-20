using TransportOrders.Core;

namespace TransportOrders.Tests;

public sealed class PackingDomainTests
{
    private static readonly DateTime Now = DateTime.SpecifyKind(new DateTime(2026, 10, 12), DateTimeKind.Utc);
    private static Basket Loaded() => new("K-01", "AL-1-B01", 12, 1.2m, 6.4m, "PACK-01");
    [Fact]
    public void Only_approved_packages_get_a_label_and_can_ship()
    {
        var basket = Loaded();
        var package = new PackedPackage("PK-2026-000001", basket, new("AL-1-B01", "AL-1", "EN AW-6060 profiles"), "PACK-01", "test", Now);
        Assert.Throws<TransportRuleException>(() => package.Dispatch("SHIP-01", "test", Now));
        package.CompletePacking("test", Now);
        package.Inspect(false, true, "QA", "Surface damage", Now);
        Assert.Null(package.LabelText);
        Assert.Throws<TransportRuleException>(() => package.Dispatch("SHIP-01", "test", Now));
        package.Inspect(true, true, "QA", "Repacked", Now);
        package.Dispatch("SHIP-01", "test", Now);
        Assert.Null(package.LocationId);
        package.Arrive("test", Now);
        Assert.Equal(PackageStatus.Shipped, package.Status);
        Assert.Equal("SHIP-01", package.LocationId);
        Assert.Contains("BASKET K-01 / LOAD 1", package.LabelText!);
    }
    [Fact]
    public void Basket_identity_survives_unload_return_and_new_batch()
    {
        var basket = Loaded();
        basket.Unload("PK-1", "test", Now);
        Assert.True(basket.IsEmpty);
        Assert.Null(basket.CurrentMaterialLotNumber);
        Assert.Throws<TransportRuleException>(() => basket.Depart("PACK-01", "OVEN-01", "TR-1"));
        var returning = new BasketReturn(basket, "PRESS-01", "test", Now);
        basket.DepartEmpty(returning, "test", Now);
        Assert.Throws<TransportRuleException>(() => basket.Reload(new("AL-2-B01", "AL-2", "EN AW-6063 profiles"), 10, 1m, 6m, "test", Now));
        returning.Complete("test", Now); basket.ArriveEmpty(returning, "test", Now);
        var lot = new MaterialLot("AL-2-B01", "AL-2", "EN AW-6063 profiles");
        basket.Reload(lot, 10, 1m, 6m, "test", Now);
        Assert.Equal("K-01", basket.Number);
        Assert.Equal(2, basket.ContentSequence);
        var order = TransportOrder.ForBasket("TO-2026-0042", basket, lot, "OVEN-01", OrderPriority.Normal, Now.AddHours(1), Now, "test");
        Assert.Equal("AL-2", order.HeatNumber);
        Assert.Equal("K-01", order.PieceNumber);
        Assert.Equal(2, order.BasketContentSequence);
    }
}
