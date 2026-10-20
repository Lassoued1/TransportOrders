using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using TransportOrders.Core;
using TransportOrders.Data;

namespace TransportOrders.IntegrationTests;

public sealed class PlcExchangeTests : IDisposable
{
    private readonly TempDatabase _database = new();
    private readonly ServiceProvider _services;
    private IOrderRepository Repository => _services.GetRequiredService<IOrderRepository>();
    private IPlcRepository Plc => (IPlcRepository)Repository;
    private DatabaseInitializer Initializer => _services.GetRequiredService<DatabaseInitializer>();
    private IDbContextFactory<AppDbContext> Factory => _services.GetRequiredService<IDbContextFactory<AppDbContext>>();
    private readonly MutableClock _clock = new();
    private sealed class MutableClock : TimeProvider
    {
        public DateTimeOffset Now = DateTimeOffset.UtcNow.AddHours(1);
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class ReplyChannel(PlcAcknowledgement? send) : IPlcChannel
    {
        public PlcAcknowledgement? Reply;
        public Task<PlcAcknowledgement?> SendAsync(PlcCommand command, CancellationToken cancellationToken = default) => Task.FromResult(send);
        public Task<PlcAcknowledgement?> ReceiveAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(Reply);
    }
    public PlcExchangeTests() => _services = new ServiceCollection().AddTransportOrdersData(_database.Path).BuildServiceProvider();
    private async Task<PlcCommand> InitializeAsync()
    { await Initializer.InitializeAsync(); return Assert.Single((await Repository.GetPlantStateAsync()).PlcCommands); }
    private PlcExchangeService Exchange(IPlcChannel? channel = null) => new(Repository, channel ?? Plc.PlcChannel, _clock);
    private async Task SettingsAsync(bool connected, bool suppressed)
    {
        var settings = (await Repository.GetPlantStateAsync()).PlcSimulation!;
        await Plc.SetPlcSimulationAsync(settings.Version, connected, suppressed, "test", "Communication test", _clock.Now.UtcDateTime);
    }
    private static PlcAcknowledgement Ack(PlcCommand c, PlcAckKind kind = PlcAckKind.Completed, int sequence = 2) =>
        new(Guid.NewGuid(), c.Id, c.EquipmentId, sequence, kind, c.Target, c.PayloadHash);
    private async Task<int> ExecutionsAsync()
    { await using var context = await Factory.CreateDbContextAsync(); return await context.PlcExecutions.CountAsync(); }

    [Fact]
    public async Task Suppressed_reply_timeout_and_retry_reuse_id_without_reexecuting_demo_move()
    {
        var command = await InitializeAsync();
        await SettingsAsync(true, true);
        Assert.Equal(PlcProgress.Waiting, await Exchange().ProcessAsync(command.Id));
        Assert.Equal(1, await ExecutionsAsync());
        await Initializer.InitializeAsync();
        _clock.Now = _clock.Now.AddSeconds(16);
        Assert.Equal(PlcProgress.TimedOut, await Exchange().ProcessAsync(command.Id));
        var state = await Repository.GetPlantStateAsync();
        command = Assert.Single(state.PlcCommands);
        Assert.Equal("PACK-01", state.Baskets.Single(b => b.IsMoving).ReservedLocationId);
        Assert.Equal(1, command.Attempts);
        await Plc.RetryPlcCommandAsync(command.Id, command.Version, "operator", "Connection checked", _clock.Now.UtcDateTime);
        await SettingsAsync(true, false);
        Assert.Equal(PlcProgress.Applied, await Exchange().ProcessAsync(command.Id));
        state = await Repository.GetPlantStateAsync();
        Assert.Equal(command.Id, Assert.Single(state.PlcCommands).Id);
        Assert.Equal(2, Assert.Single(state.PlcCommands).Attempts);
        Assert.Equal(1, await ExecutionsAsync());
        Assert.DoesNotContain(state.Baskets, b => b.IsMoving);
    }

    [Fact]
    public async Task Late_valid_reply_can_resolve_timeout_without_resending()
    {
        var command = await InitializeAsync(); await SettingsAsync(true, true);
        await Exchange().ProcessAsync(command.Id);
        _clock.Now = _clock.Now.AddSeconds(16);
        await Exchange().ProcessAsync(command.Id);
        await SettingsAsync(true, false);
        Assert.Equal(PlcProgress.Applied, await Exchange().ProcessAsync(command.Id));
        Assert.Equal(1, Assert.Single((await Repository.GetPlantStateAsync()).PlcCommands).Attempts);
    }

    [Fact]
    public async Task Accepted_is_not_arrival_and_completed_reply_is_required()
    {
        var command = await InitializeAsync();
        var channel = new ReplyChannel(Ack(command, PlcAckKind.Accepted, 1));
        Assert.Equal(PlcProgress.Accepted, await Exchange(channel).ProcessAsync(command.Id));
        Assert.Single((await Repository.GetPlantStateAsync()).Baskets, b => b.IsMoving);
        channel.Reply = Ack(command);
        Assert.Equal(PlcProgress.Applied, await Exchange(channel).ProcessAsync(command.Id));
        Assert.DoesNotContain((await Repository.GetPlantStateAsync()).Baskets, b => b.IsMoving);
    }

    [Fact]
    public async Task Duplicate_and_out_of_order_acknowledgements_never_apply_arrival_twice()
    {
        var command = await InitializeAsync(); var completed = Ack(command);
        Assert.Equal(PlcReceiptResult.Recorded, await Plc.RecordPlcAckAsync(completed, _clock.Now.UtcDateTime));
        Assert.Equal(PlcReceiptResult.Duplicate, await Plc.RecordPlcAckAsync(completed, _clock.Now.UtcDateTime));
        Assert.Equal(PlcReceiptResult.Stale, await Plc.RecordPlcAckAsync(Ack(command, PlcAckKind.Accepted, 1), _clock.Now.UtcDateTime));
        await Plc.ApplyPlcCompletionAsync(command.Id, _clock.Now.UtcDateTime);
        var before = await Repository.GetPlantStateAsync();
        Assert.Equal(PlcReceiptResult.Stale, await Plc.RecordPlcAckAsync(Ack(command, PlcAckKind.Completed, 3), _clock.Now.UtcDateTime));
        await Plc.ApplyPlcCompletionAsync(command.Id, _clock.Now.UtcDateTime);
        var after = await Repository.GetPlantStateAsync();
        Assert.Equal(before.Baskets.Select(b => b.Version), after.Baskets.Select(b => b.Version));
        Assert.Equal(before.Orders.Select(o => o.History.Count), after.Orders.Select(o => o.History.Count));
    }

    [Theory]
    [InlineData("route")]
    [InlineData("equipment")]
    [InlineData("payload")]
    [InlineData("unknown")]
    public async Task Mismatched_telegrams_cannot_change_position_or_command(string mismatch)
    {
        var command = await InitializeAsync(); var ack = Ack(command);
        ack = mismatch switch { "route" => ack with { Target = "PRESS-01" }, "equipment" => ack with { EquipmentId = "OTHER" },
            "payload" => ack with { PayloadHash = "OTHER" }, _ => ack with { CommandId = Guid.NewGuid() } };
        await Assert.ThrowsAsync<TransportRuleException>(() => Plc.RecordPlcAckAsync(ack, _clock.Now.UtcDateTime));
        var state = await Repository.GetPlantStateAsync();
        Assert.Equal(PlcCommandStatus.Pending, Assert.Single(state.PlcCommands).Status);
        Assert.Single(state.Baskets, b => b.IsMoving);
    }

    [Fact]
    public async Task Reusing_ack_id_with_different_content_is_rejected()
    {
        var command = await InitializeAsync(); var ack = Ack(command);
        await Plc.RecordPlcAckAsync(ack, _clock.Now.UtcDateTime);
        await Assert.ThrowsAsync<TransportRuleException>(() => Plc.RecordPlcAckAsync(ack with { Error = "Changed content" }, _clock.Now.UtcDateTime));
    }

    [Fact]
    public async Task Recorded_reply_survives_equipment_fault_and_restart_until_it_can_be_applied()
    {
        var command = await InitializeAsync(); await Plc.RecordPlcAckAsync(Ack(command), _clock.Now.UtcDateTime);
        var equipment = (await Repository.GetPlantStateAsync()).Equipment.Single(e => e.Id == "PACK-01");
        await Repository.SetEquipmentControlAsync(equipment.Id, equipment.Version, equipment.Mode, EquipmentCondition.Fault, "test", "Fault", _clock.Now.UtcDateTime);
        await Initializer.InitializeAsync();
        Assert.Equal(PlcProgress.Deferred, await Exchange().ProcessAsync(command.Id));
        Assert.Single((await Repository.GetPlantStateAsync()).Baskets, b => b.IsMoving);
        equipment = (await Repository.GetPlantStateAsync()).Equipment.Single(e => e.Id == "PACK-01");
        await Repository.SetEquipmentControlAsync(equipment.Id, equipment.Version, equipment.Mode, EquipmentCondition.Operational, "test", "Restored", _clock.Now.UtcDateTime);
        Assert.Equal(PlcProgress.Applied, await Exchange().ProcessAsync(command.Id));
        Assert.Equal(0, await ExecutionsAsync()); // Recovered inbox evidence; no send was needed.
    }

    [Fact]
    public async Task Rejected_command_remains_reserved_and_needs_operator_review()
    {
        var command = await InitializeAsync();
        var channel = new ReplyChannel(Ack(command, PlcAckKind.Rejected, 1) with { Error = "Destination interlock" });
        Assert.Equal(PlcProgress.Rejected, await Exchange(channel).ProcessAsync(command.Id));
        var state = await Repository.GetPlantStateAsync(); command = Assert.Single(state.PlcCommands);
        Assert.Single(state.Baskets, b => b.IsMoving);
        await Assert.ThrowsAsync<TransportRuleException>(() => Plc.RetryPlcCommandAsync(command.Id, command.Version, "test", "Retry", _clock.Now.UtcDateTime));
    }

    [Fact]
    public async Task Retry_limit_stops_after_three_send_attempts_and_keeps_reservation()
    {
        var command = await InitializeAsync(); await SettingsAsync(true, true);
        for (var attempt = 1; attempt <= PlcCommand.MaxAttempts; attempt++)
        {
            await Exchange().ProcessAsync(command.Id); _clock.Now = _clock.Now.AddSeconds(16);
            Assert.Equal(PlcProgress.TimedOut, await Exchange().ProcessAsync(command.Id));
            command = Assert.Single((await Repository.GetPlantStateAsync()).PlcCommands);
            if (attempt < PlcCommand.MaxAttempts) await Plc.RetryPlcCommandAsync(command.Id, command.Version, "test", "Rechecked", _clock.Now.UtcDateTime);
        }
        await Assert.ThrowsAsync<TransportRuleException>(() => Plc.RetryPlcCommandAsync(command.Id, command.Version, "test", "Fourth retry", _clock.Now.UtcDateTime));
        Assert.Equal(1, await ExecutionsAsync()); Assert.Single((await Repository.GetPlantStateAsync()).Baskets, b => b.IsMoving);
    }

    [Fact]
    public async Task Failed_arrival_transaction_leaves_completed_reply_and_all_positions_unchanged()
    {
        var command = await InitializeAsync(); await Plc.RecordPlcAckAsync(Ack(command), _clock.Now.UtcDateTime);
        await using (var context = await Factory.CreateDbContextAsync())
        {
            var shuttle = await context.Equipment.SingleAsync(e => e.Id == PlantEquipment.SharedShuttle);
            context.Entry(shuttle).Property(e => e.ActiveOperation).CurrentValue = "MISMATCH";
            await context.SaveChangesAsync();
        }
        var before = await Repository.GetPlantStateAsync();
        await Assert.ThrowsAsync<TransportRuleException>(() => Plc.ApplyPlcCompletionAsync(command.Id, _clock.Now.UtcDateTime));
        var after = await Repository.GetPlantStateAsync();
        Assert.Equal(before.Baskets.Select(b => b.Version), after.Baskets.Select(b => b.Version));
        Assert.Equal(before.Orders.Select(o => o.History.Count), after.Orders.Select(o => o.History.Count));
        Assert.Equal(PlcCommandStatus.Completed, Assert.Single(after.PlcCommands).Status);
    }

    [Fact]
    public async Task Manual_confirmation_is_audited_separately_and_late_machine_reply_is_ignored()
    {
        var command = await InitializeAsync(); var state = await Repository.GetPlantStateAsync();
        var transport = state.Transports.Single(t => t.Status == TransportStatus.InTransit);
        var orders = state.Orders.Where(o => o.TransportNumber == transport.TransportNumber).ToList();
        transport.Complete(orders, "operator", _clock.Now.UtcDateTime); await Repository.SaveTransportAsync(transport, orders);
        var before = await Repository.GetPlantStateAsync();
        command = Assert.Single(before.PlcCommands);
        Assert.Equal("operator", command.AppliedBy);
        Assert.Contains(command.History, h => h.Detail.Contains("manual arrival"));
        Assert.Equal(PlcReceiptResult.Stale, await Plc.RecordPlcAckAsync(Ack(command), _clock.Now.UtcDateTime));
        Assert.Equal(before.Baskets.Select(b => b.Version), (await Repository.GetPlantStateAsync()).Baskets.Select(b => b.Version));
    }

    [Fact]
    public async Task Explicit_manual_reconciliation_of_rejection_requires_reason_and_records_operator()
    {
        var command = await InitializeAsync();
        await Plc.RecordPlcAckAsync(Ack(command, PlcAckKind.Rejected, 1), _clock.Now.UtcDateTime);
        command = Assert.Single((await Repository.GetPlantStateAsync()).PlcCommands);
        await Assert.ThrowsAsync<ArgumentException>(() => Plc.ConfirmPlcArrivalManuallyAsync(command.Id, command.Version, "operator", "", _clock.Now.UtcDateTime));
        Assert.Equal(PlcProgress.Applied, await Plc.ConfirmPlcArrivalManuallyAsync(command.Id, command.Version, "operator", "Actual arrival checked on site", _clock.Now.UtcDateTime));
        var state = await Repository.GetPlantStateAsync(); command = Assert.Single(state.PlcCommands);
        Assert.Equal("operator", command.AppliedBy);
        Assert.Contains(command.History, h => h.Detail.Contains("Actual arrival checked on site"));
        Assert.Null(command.LastError);
        Assert.DoesNotContain(state.Baskets, b => b.IsMoving);
    }

    [Fact]
    public async Task Additive_migration_backfills_active_transfer_without_resetting_positions_or_history()
    {
        await InitializeAsync(); var before = await Repository.GetPlantStateAsync();
        await using (var context = await Factory.CreateDbContextAsync())
            await context.GetService<IMigrator>().MigrateAsync("20261012052655_PackingAndReusableBaskets");
        await Initializer.InitializeAsync(); var after = await Repository.GetPlantStateAsync();
        Assert.Equal(before.Baskets.Select(b => b.Version), after.Baskets.Select(b => b.Version));
        Assert.Equal(before.Orders.Select(o => o.History.Count), after.Orders.Select(o => o.History.Count));
        Assert.Equal(PlcCommandStatus.Pending, Assert.Single(after.PlcCommands).Status);
        Assert.Equal(0, await ExecutionsAsync());
    }
    public void Dispose() { _services.Dispose(); _database.Dispose(); }
}
