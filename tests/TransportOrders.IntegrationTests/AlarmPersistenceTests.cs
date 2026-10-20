using Microsoft.Extensions.DependencyInjection;
using TransportOrders.Core;
using TransportOrders.Data;

namespace TransportOrders.IntegrationTests;

public class AlarmPersistenceTests
{
    [Fact]
    public async Task Acknowledgement_clear_and_recurrence_are_distinct_and_survive_restart()
    {
        using var database = new TempDatabase();
        var now = DateTime.UtcNow;
        var condition = new AlarmCondition("equipment:OVEN-01", AlarmCode.EquipmentFault, "OVEN-01", "Fault 12");
        Guid original;
        using (var services = new ServiceCollection().AddTransportOrdersData(database.Path).BuildServiceProvider())
        {
            await services.GetRequiredService<DatabaseInitializer>().InitializeAsync();
            var alarms = services.GetRequiredService<IAlarmRepository>();
            await alarms.SynchronizeAsync([condition], now);
            await alarms.SynchronizeAsync([condition], now.AddSeconds(2));
            var alarm = Assert.Single(await alarms.ReadAsync()); original = alarm.Id;
            Assert.Single(alarm.History);
            await alarms.AcknowledgeAsync(alarm.Id, alarm.Version, "PLANT\\Maintenance", "Checked oven", now.AddSeconds(3));
            alarm = Assert.Single(await alarms.ReadAsync());
            Assert.True(alarm.IsActive); Assert.NotNull(alarm.AcknowledgedUtc);
            await alarms.SynchronizeAsync([], now.AddSeconds(4));
        }
        using (var services = new ServiceCollection().AddTransportOrdersData(database.Path).BuildServiceProvider())
        {
            var alarms = services.GetRequiredService<IAlarmRepository>();
            var cleared = Assert.Single(await alarms.ReadAsync());
            Assert.False(cleared.IsActive); Assert.Equal(original, cleared.Id); Assert.Equal(DateTimeKind.Utc, cleared.RaisedUtc.Kind);
            Assert.Equal(new[] { "Raised", "Acknowledged", "Cleared" }, cleared.History.Select(e => e.Action));
            await alarms.SynchronizeAsync([condition], now.AddSeconds(5));
            var after = await alarms.ReadAsync();
            Assert.Equal(2, after.Count);
            var active = Assert.Single(after, a => a.IsActive);
            Assert.NotEqual(original, active.Id); Assert.Null(active.AcknowledgedUtc);
        }
    }
    [Fact]
    public async Task Changed_or_acknowledged_alarm_rejects_stale_operator_versions()
    {
        using var database = new TempDatabase();
        using var services = new ServiceCollection().AddTransportOrdersData(database.Path).BuildServiceProvider();
        await services.GetRequiredService<DatabaseInitializer>().InitializeAsync();
        var alarms = services.GetRequiredService<IAlarmRepository>(); var now = DateTime.UtcNow;
        var condition = new AlarmCondition("runtime:error", AlarmCode.RuntimeError, "RUNTIME", "First error");
        await alarms.SynchronizeAsync([condition], now);
        var before = Assert.Single(await alarms.ReadAsync());
        await alarms.SynchronizeAsync([condition with { Detail = "Changed error" }], now.AddSeconds(1));
        await Assert.ThrowsAsync<TransportRuleException>(() => alarms.AcknowledgeAsync(before.Id, before.Version, "Operator", "Checked", now));
        var current = Assert.Single(await alarms.ReadAsync());
        await Assert.ThrowsAsync<ArgumentException>(() => alarms.AcknowledgeAsync(current.Id, current.Version, "Operator", "", now));
        await alarms.AcknowledgeAsync(current.Id, current.Version, "Operator", "Checked", now);
        await Assert.ThrowsAsync<TransportRuleException>(() => alarms.AcknowledgeAsync(current.Id, current.Version, "Operator", "Again", now));
        Assert.Single((await alarms.ReadAsync()).Single().History, e => e.Action == "Acknowledged");
    }
    [Fact]
    public async Task Alarm_migration_preserves_existing_material_and_PLC_history()
    {
        using var database = new TempDatabase();
        using var services = new ServiceCollection().AddTransportOrdersData(database.Path).BuildServiceProvider();
        await services.GetRequiredService<DatabaseInitializer>().InitializeAsync();
        var repo = services.GetRequiredService<IOrderRepository>(); var state = await repo.GetPlantStateAsync();
        var alarms = services.GetRequiredService<IAlarmRepository>();
        await alarms.SynchronizeAsync(AlarmDetection.Detect(state, "Runtime failure", "Backup failure"), DateTime.UtcNow);
        var after = await repo.GetPlantStateAsync();
        Assert.Equal(state.Orders.Select(OrderRevision.For), after.Orders.Select(OrderRevision.For));
        Assert.Equal(state.PlcCommands.Select(c => (c.Id, c.Status, c.Attempts)), after.PlcCommands.Select(c => (c.Id, c.Status, c.Attempts)));
        Assert.Equal(2, (await alarms.ReadAsync()).Count);
    }
}
