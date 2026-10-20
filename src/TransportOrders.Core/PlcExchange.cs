using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Globalization;

namespace TransportOrders.Core;

public enum PlcCommandKind { MaterialTransfer, EmptyReturn, PackageTransfer }
public enum PlcCommandStatus { Pending, AwaitingAck, Accepted, Completed, Applied, TimedOut, Rejected }
public enum PlcAckKind { Accepted, Completed, Rejected }
public enum PlcReceiptResult { Recorded, Duplicate, Stale }
public enum PlcProgress { Applied, Waiting, Accepted, TimedOut, Rejected, Offline, Deferred }
public sealed record PlcLoadItem(string Entity, int Load, int Quantity, decimal Weight, decimal Length);
public sealed record PlcAcknowledgement(Guid EventId, Guid CommandId, string EquipmentId, int Sequence,
    PlcAckKind Kind, string Target, string PayloadHash, string? Error = null);

public sealed partial class PlcEvent
{
    private PlcEvent() { }
    public PlcEvent(DateTime at, string actor, string detail) { At = at; Actor = actor; Detail = detail; }
    public int Id { get; private set; }
    public DateTime At { get; private set; }
    public string Actor { get; private set; } = string.Empty;
    public string Detail { get; private set; } = string.Empty;
}

/// <summary>Durable immutable transfer envelope, retrying the same correlation id at most three times.</summary>
public sealed partial class PlcCommand
{
    public const int MaxAttempts = 3;
    public const int TimeoutSeconds = 15;
    private readonly List<PlcEvent> _history = [];
    private PlcCommand() { }
    public PlcCommand(PlcCommandKind kind, string operation, string source, string target, IEnumerable<PlcLoadItem> items, DateTime now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation); ArgumentException.ThrowIfNullOrWhiteSpace(source); ArgumentException.ThrowIfNullOrWhiteSpace(target);
        if (!Enum.IsDefined(kind) || source == target) throw new ArgumentException("A command needs a valid kind and different locations.");
        Id = Guid.NewGuid(); Kind = kind; Operation = operation; Source = source; Target = target;
        EquipmentId = PlantEquipment.SharedShuttle;
        Payload = SerializePayload(items);
        PayloadHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"1|{kind}|{operation}|{EquipmentId}|{source}|{target}|{Payload}")));
        CreatedAt = now; Record(now, "OUTBOX", "Queued atomically with transfer reservation");
    }
    public Guid Id { get; private set; }
    public int ContractVersion { get; private set; } = 1;
    public PlcCommandKind Kind { get; private set; }
    public string Operation { get; private set; } = string.Empty;
    public string EquipmentId { get; private set; } = string.Empty;
    public string Source { get; private set; } = string.Empty;
    public string Target { get; private set; } = string.Empty;
    public string Payload { get; private set; } = string.Empty;
    public string PayloadHash { get; private set; } = string.Empty;
    public PlcCommandStatus Status { get; private set; } = PlcCommandStatus.Pending;
    public int Attempts { get; private set; }
    public int LastSequence { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? LastAttemptAt { get; private set; }
    public DateTime? DeadlineAt { get; private set; }
    public DateTime? AppliedAt { get; private set; }
    public string? AppliedBy { get; private set; }
    public string? LastError { get; private set; }
    public Guid Version { get; private set; }
    public IReadOnlyList<PlcEvent> History => _history.AsReadOnly();
    public static string SerializePayload(IEnumerable<PlcLoadItem> items) => JsonSerializer.Serialize(items
        .OrderBy(i => i.Entity, StringComparer.Ordinal).ThenBy(i => i.Load).Select(i => new
        { i.Entity, i.Load, i.Quantity, Weight = i.Weight.ToString("G29", CultureInfo.InvariantCulture), Length = i.Length.ToString("G29", CultureInfo.InvariantCulture) }));
    public void BeginAttempt(DateTime now)
    {
        if (Status != PlcCommandStatus.Pending || Attempts >= MaxAttempts) throw new TransportRuleException("Command is not eligible to send.");
        Attempts++; LastAttemptAt = now; DeadlineAt = now.AddSeconds(TimeoutSeconds); Status = PlcCommandStatus.AwaitingAck;
        LastError = null; Record(now, "EXCHANGE", $"Send attempt {Attempts}; same command id");
    }
    public void Expire(DateTime now)
    {
        if (Status is not (PlcCommandStatus.AwaitingAck or PlcCommandStatus.Accepted) || DeadlineAt is null || now < DeadlineAt)
            throw new TransportRuleException("The acknowledgement deadline has not expired.");
        Status = PlcCommandStatus.TimedOut; LastError = "Acknowledgement timeout; transfer reservation retained";
        Record(now, "EXCHANGE", LastError);
    }
    public void Retry(string user, string reason, DateTime now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(user); ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (Status != PlcCommandStatus.TimedOut || Attempts >= MaxAttempts) throw new TransportRuleException("Only a timed-out command below the retry limit can be retried.");
        Status = PlcCommandStatus.Pending; Record(now, user, $"Retry requested: {reason}");
    }
    public bool Receive(PlcAcknowledgement ack, DateTime now)
    {
        Validate(ack);
        if (Status == PlcCommandStatus.Applied || ack.Sequence <= LastSequence || Status == PlcCommandStatus.Completed) return false;
        if (Status == PlcCommandStatus.Rejected) throw new TransportRuleException("A rejected command requires operator reconciliation.");
        LastSequence = ack.Sequence;
        Status = ack.Kind switch { PlcAckKind.Accepted => PlcCommandStatus.Accepted, PlcAckKind.Completed => PlcCommandStatus.Completed, _ => PlcCommandStatus.Rejected };
        LastError = ack.Kind == PlcAckKind.Rejected ? ack.Error ?? "PLC rejected the command" : null;
        Record(now, "PLC", $"{ack.Kind}; sequence {ack.Sequence}"); return true;
    }
    public void Validate(PlcAcknowledgement ack)
    {
        if (ack.EventId == Guid.Empty || ack.CommandId != Id || ack.EquipmentId != EquipmentId || ack.Target != Target
            || ack.PayloadHash != PayloadHash || ack.Sequence <= 0 || !Enum.IsDefined(ack.Kind))
            throw new TransportRuleException("Acknowledgement correlation, equipment, route or payload does not match the command.");
    }
    public void MarkApplied(DateTime now, string actor)
    {
        if (Status != PlcCommandStatus.Completed) throw new TransportRuleException("A completed acknowledgement is required.");
        Status = PlcCommandStatus.Applied; AppliedAt = now; AppliedBy = actor; LastError = null;
        Record(now, actor, "Arrival and acknowledgement applied atomically");
    }
    public void ConfirmManually(string actor, DateTime now, string? reason = null)
    {
        if (Status == PlcCommandStatus.Applied) return;
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        Status = PlcCommandStatus.Applied; AppliedAt = now; AppliedBy = actor; LastError = null;
        Record(now, actor, $"Explicit manual arrival confirmation; no machine acknowledgement asserted: {reason ?? "Operator confirmed arrival"}");
    }
    private void Record(DateTime now, string actor, string detail) { Version = Guid.NewGuid(); _history.Add(new(now, actor, detail)); }
}

public interface IPlcChannel
{
    Task<PlcAcknowledgement?> SendAsync(PlcCommand command, CancellationToken cancellationToken = default);
    Task<PlcAcknowledgement?> ReceiveAsync(Guid commandId, CancellationToken cancellationToken = default);
}
public sealed class PlcConnectionException(string message) : Exception(message);
public interface IPlcRepository
{
    IPlcChannel PlcChannel { get; }
    Task<PlcCommand> BeginPlcAttemptAsync(Guid commandId, Guid version, DateTime now, CancellationToken cancellationToken = default);
    Task<PlcReceiptResult> RecordPlcAckAsync(PlcAcknowledgement ack, DateTime now, CancellationToken cancellationToken = default);
    Task<PlcProgress> ApplyPlcCompletionAsync(Guid commandId, DateTime now, CancellationToken cancellationToken = default);
    Task ExpirePlcCommandAsync(Guid commandId, Guid version, DateTime now, CancellationToken cancellationToken = default);
    Task RetryPlcCommandAsync(Guid commandId, Guid version, string user, string reason, DateTime now, CancellationToken cancellationToken = default);
    Task SetPlcSimulationAsync(Guid version, bool connected, bool suppressAcknowledgements, string user, string reason, DateTime now, CancellationToken cancellationToken = default);
    Task<PlcProgress> ConfirmPlcArrivalManuallyAsync(Guid commandId, Guid version, string user, string reason, DateTime now, CancellationToken cancellationToken = default);
}

public sealed partial class PlcExchangeService(IOrderRepository repository, IPlcChannel channel, TimeProvider clock)
{
    public async Task<PlcProgress> ProcessAsync(Guid commandId, CancellationToken cancellationToken = default)
    {
        var store = repository as IPlcRepository ?? throw new InvalidOperationException("PLC persistence is required.");
        var command = (await repository.GetPlantStateAsync(cancellationToken)).PlcCommands.Single(c => c.Id == commandId);
        if (command.Status == PlcCommandStatus.Applied) return PlcProgress.Applied;
        if (command.Status == PlcCommandStatus.Completed) return await store.ApplyPlcCompletionAsync(command.Id, clock.GetUtcNow().UtcDateTime, cancellationToken);
        if (command.Status == PlcCommandStatus.Rejected) return PlcProgress.Rejected;
        try
        {
            PlcAcknowledgement? ack;
            if (command.Status == PlcCommandStatus.Pending)
            {
                command = await store.BeginPlcAttemptAsync(command.Id, command.Version, clock.GetUtcNow().UtcDateTime, cancellationToken);
                ack = await channel.SendAsync(command, cancellationToken).WaitAsync(TimeSpan.FromSeconds(PlcCommand.TimeoutSeconds), cancellationToken);
            }
            else ack = await channel.ReceiveAsync(command.Id, cancellationToken).WaitAsync(TimeSpan.FromSeconds(PlcCommand.TimeoutSeconds), cancellationToken);
            if (ack is not null)
            {
                await store.RecordPlcAckAsync(ack, clock.GetUtcNow().UtcDateTime, cancellationToken);
                command = (await repository.GetPlantStateAsync(cancellationToken)).PlcCommands.Single(c => c.Id == commandId);
                if (command.Status == PlcCommandStatus.Completed) return await store.ApplyPlcCompletionAsync(command.Id, clock.GetUtcNow().UtcDateTime, cancellationToken);
                if (command.Status == PlcCommandStatus.Rejected) return PlcProgress.Rejected;
            }
            var now = clock.GetUtcNow().UtcDateTime;
            if (command.Status is PlcCommandStatus.AwaitingAck or PlcCommandStatus.Accepted && command.DeadlineAt <= now)
            { await store.ExpirePlcCommandAsync(command.Id, command.Version, now, cancellationToken); return PlcProgress.TimedOut; }
            return command.Status == PlcCommandStatus.TimedOut ? PlcProgress.TimedOut
                : command.Status == PlcCommandStatus.Accepted ? PlcProgress.Accepted : PlcProgress.Waiting;
        }
        catch (PlcConnectionException)
        {
            var now = clock.GetUtcNow().UtcDateTime;
            if (command.Status is PlcCommandStatus.AwaitingAck or PlcCommandStatus.Accepted && command.DeadlineAt <= now)
            { await store.ExpirePlcCommandAsync(command.Id, command.Version, now, cancellationToken); return PlcProgress.TimedOut; }
            return PlcProgress.Offline;
        }
        catch (TimeoutException)
        {
            if (command.Status is PlcCommandStatus.AwaitingAck or PlcCommandStatus.Accepted)
            {
                var now = clock.GetUtcNow().UtcDateTime;
                await store.ExpirePlcCommandAsync(command.Id, command.Version,
                    now < command.DeadlineAt ? command.DeadlineAt!.Value : now, cancellationToken);
            }
            return PlcProgress.TimedOut;
        }
    }
}
