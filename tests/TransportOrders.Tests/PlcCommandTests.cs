using TransportOrders.Core;

namespace TransportOrders.Tests;

public sealed class PlcCommandTests
{
    private static readonly DateTime Now = new(2026, 10, 12, 8, 0, 0, DateTimeKind.Utc);
    private static PlcCommand Command() => new(PlcCommandKind.MaterialTransfer, "TR-2026-0042", "PRESS-01", "OVEN-01", [new("K-01", 1, 10, 1m, 6.4m)], Now);
    [Fact]
    public void Hash_is_independent_of_decimal_scale_and_item_order()
    {
        var one = new PlcCommand(PlcCommandKind.MaterialTransfer, "TR-1", "A", "B", [new("K2", 1, 10, 1.00m, 6.40m), new("K1", 1, 10, 2m, 6m)], Now);
        var two = new PlcCommand(PlcCommandKind.MaterialTransfer, "TR-1", "A", "B", [new("K1", 1, 10, 2.00m, 6.00m), new("K2", 1, 10, 1m, 6.4m)], Now);
        Assert.Equal(one.Payload, two.Payload); Assert.Equal(one.PayloadHash, two.PayloadHash);
    }
    [Fact]
    public void Timeout_cannot_release_or_retry_before_deadline()
    {
        var command = Command(); command.BeginAttempt(Now);
        Assert.Throws<TransportRuleException>(() => command.Expire(Now.AddSeconds(14)));
        Assert.Throws<TransportRuleException>(() => command.Retry("test", "Too early", Now));
        command.Expire(Now.AddSeconds(15)); var id = command.Id; var hash = command.PayloadHash;
        command.Retry("test", "Communication restored", Now.AddSeconds(16)); command.BeginAttempt(Now.AddSeconds(16));
        Assert.Equal(id, command.Id); Assert.Equal(hash, command.PayloadHash); Assert.Equal(2, command.Attempts);
    }
    [Fact]
    public void Acceptance_does_not_allow_marking_arrival_as_applied()
    {
        var command = Command(); command.BeginAttempt(Now);
        command.Receive(new(Guid.NewGuid(), command.Id, command.EquipmentId, 1, PlcAckKind.Accepted, command.Target, command.PayloadHash), Now);
        Assert.Throws<TransportRuleException>(() => command.MarkApplied(Now, "PLC"));
        Assert.Equal(PlcCommandStatus.Accepted, command.Status);
    }
}
