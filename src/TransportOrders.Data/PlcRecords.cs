using System.Text.Json;
using TransportOrders.Core;

namespace TransportOrders.Data;

public sealed class PlcInboxEntry
{
    private PlcInboxEntry() { }
    public PlcInboxEntry(PlcAcknowledgement ack, PlcReceiptResult result, DateTime now)
    { Id = ack.EventId; CommandId = ack.CommandId; Payload = JsonSerializer.Serialize(ack); Result = result; ReceivedAt = now; }
    public Guid Id { get; private set; }
    public Guid CommandId { get; private set; }
    public string Payload { get; private set; } = string.Empty;
    public PlcReceiptResult Result { get; private set; }
    public DateTime ReceivedAt { get; private set; }
}

/// <summary>The demo PLC remembers its execution independently of delivery of the reply.</summary>
public sealed class PlcSimulatorExecution
{
    private PlcSimulatorExecution() { }
    public PlcSimulatorExecution(PlcCommand command)
    {
        CommandId = command.Id; PayloadHash = command.PayloadHash; EquipmentId = command.EquipmentId;
        Target = command.Target; EventId = Guid.NewGuid();
    }
    public Guid CommandId { get; private set; }
    public Guid EventId { get; private set; }
    public string PayloadHash { get; private set; } = string.Empty;
    public string EquipmentId { get; private set; } = string.Empty;
    public string Target { get; private set; } = string.Empty;
    public PlcAcknowledgement Reply() => new(EventId, CommandId, EquipmentId, 2, PlcAckKind.Completed, Target, PayloadHash);
}
