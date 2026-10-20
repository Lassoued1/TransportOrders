using TransportOrders.Core;

namespace TransportOrders.Tests;

public sealed class ProductionSchedulerTests
{
    private static readonly DateTime Now = new(2026, 10, 12, 8, 0, 0, DateTimeKind.Utc);
    private static PlantState State(params (int Number, string Alloy, OrderPriority Priority, int DueHours, bool InOven)[] specs)
    {
        var baskets = new List<Basket>(); var lots = new List<MaterialLot>(); var orders = new List<TransportOrder>();
        foreach (var s in specs)
        {
            var lot = new MaterialLot($"AL-1-B{s.Number:D2}", "AL-1", $"EN AW-{s.Alloy} profiles");
            var basket = new Basket($"AL-1-K{s.Number:D2}", lot.Number, 10, 1m, 6.4m, s.InOven ? "OVEN-01" : "PRESS-01");
            var order = new TransportOrder($"TO-2026-{s.Number:D4}", lot.BatchNumber, basket.Number, lot.Number, lot.Material,
                basket.Quantity, basket.Weight, basket.LoadingLength, "PRESS-01", "OVEN-01", s.Priority,
                Now.AddHours(s.DueHours), Now.AddDays(-1), "test");
            if (s.InOven)
            {
                var transfer = Transport.Dispatch($"TR-2026-{s.Number:D4}", ExtrusionSimulator.Shuttle, [order], "test", Now.AddHours(-2));
                transfer.Complete([order], "test", Now.AddHours(-1));
            }
            baskets.Add(basket); lots.Add(lot); orders.Add(order);
        }
        var locations = new List<PlantLocation> { new("PRESS-01", StationKind.Press, 10), new("OVEN-01", StationKind.AgingOven, 2),
            new("OVEN-02", StationKind.AgingOven, 2), new("PACK-01", StationKind.Packing, 1), new("SHIP-01", StationKind.Shipping, 12) };
        return new(baskets, lots, locations, orders, [])
        {
            Equipment = [new("PRESS-01", EquipmentKind.Press, "PRESS-01", Now), new("OVEN-01", EquipmentKind.Oven, "OVEN-01", Now),
                new("OVEN-02", EquipmentKind.Oven, "OVEN-02", Now), new("PACK-01", EquipmentKind.Packing, "PACK-01", Now),
                new(PlantEquipment.SharedShuttle, EquipmentKind.Shuttle, null, Now)],
            Recipes = [new("DEMO-6060", "6060", 180m, 2, 3, 2), new("DEMO-6063", "6063", 180m, 2, 3, 2)],
        };
    }
    private static ProductionSchedule Plan(PlantState state) => ProductionScheduler.Plan(state, Now);

    [Fact]
    public void Urgency_selects_alloy_family_before_an_earlier_normal_order()
    {
        var plan = Plan(State((1, "6060", OrderPriority.Normal, 1, false), (2, "6063", OrderPriority.Urgent, 5, false)));
        Assert.Equal("AL-1-K02", plan.Next!.Reference);
        Assert.Equal("DEMO-6063", plan.Next.RecipeId);
        Assert.Contains(plan.Blocked, b => b.Reference == "AL-1-K01" && b.Reason == ScheduleReason.AwaitingBatch);
    }
    [Fact]
    public void Earliest_deadline_wins_when_urgency_is_equal()
    {
        var plan = Plan(State((1, "6060", OrderPriority.Normal, 5, false), (2, "6063", OrderPriority.Normal, 1, false)));
        Assert.Equal("AL-1-K02", plan.Next!.Reference);
    }
    [Fact]
    public void More_compatible_baskets_break_equal_deadline_ties()
    {
        var plan = Plan(State((1, "6060", OrderPriority.Normal, 1, false), (2, "6063", OrderPriority.Normal, 1, false), (3, "6063", OrderPriority.Normal, 1, false)));
        Assert.Equal("DEMO-6063", plan.Next!.RecipeId);
        Assert.Equal(2, plan.Actions.Count(a => a.Kind == ScheduledActionKind.DispatchBasket));
    }
    [Fact]
    public void Compatible_followers_fill_partial_oven_before_starting_normal_cycle()
    {
        var plan = Plan(State((1, "6060", OrderPriority.Normal, 3, true), (2, "6060", OrderPriority.Normal, 2, false), (3, "6063", OrderPriority.Normal, 1, false)));
        Assert.Equal(ScheduledActionKind.DispatchBasket, plan.Next!.Kind);
        Assert.Equal("AL-1-K02", plan.Next.Reference);
        Assert.DoesNotContain(plan.Actions, a => a.Kind == ScheduledActionKind.StartOvenCycle && a.Resource == "OVEN-01");
        Assert.Contains(plan.Blocked, b => b.Reference == "AL-1-K03" && b.Reason == ScheduleReason.IncompatibleRecipe);
    }
    [Fact]
    public void Full_compatible_load_is_started_as_one_batch()
    {
        var plan = Plan(State((1, "6060", OrderPriority.Normal, 2, true), (2, "6060", OrderPriority.Normal, 2, true)));
        Assert.Equal(ScheduledActionKind.StartOvenCycle, plan.Next!.Kind);
        Assert.Equal(ScheduleReason.FullBatch, plan.Next.Reason);
        Assert.Equal(2, plan.Next.BasketNumbers.Count);
    }
    [Fact]
    public void Urgent_partial_load_does_not_wait_for_normal_compatible_follower()
    {
        var plan = Plan(State((1, "6060", OrderPriority.Urgent, 2, true), (2, "6060", OrderPriority.Normal, 1, false)));
        Assert.Equal(ScheduledActionKind.StartOvenCycle, plan.Next!.Kind);
        Assert.Equal(ScheduleReason.UrgentPartialBatch, plan.Next.Reason);
    }
    [Fact]
    public void Partial_load_can_start_when_no_compatible_incoming_basket_is_eligible()
    {
        var plan = Plan(State((1, "6060", OrderPriority.Normal, 2, true)));
        Assert.Equal(ScheduleReason.NoCompatibleIncoming, plan.Next!.Reason);
    }
    [Theory]
    [InlineData(EquipmentMode.Manual, EquipmentCondition.Operational, ScheduleReason.EquipmentManual)]
    [InlineData(EquipmentMode.Automatic, EquipmentCondition.Fault, ScheduleReason.EquipmentFault)]
    public void Unavailable_equipment_produces_explicit_block(EquipmentMode mode, EquipmentCondition condition, ScheduleReason reason)
    {
        var state = State((1, "6060", OrderPriority.Normal, 2, false));
        state.Equipment.Single(e => e.Id == "PRESS-01").SetControl(mode, condition, "test", "Unavailable", Now);
        var plan = Plan(state);
        Assert.Null(plan.Next);
        Assert.Contains(plan.Blocked, b => b.Reference == "AL-1-K01" && b.Reason == reason);
    }
    [Fact]
    public void Quality_held_basket_never_enters_a_batch()
    {
        var state = State((1, "6060", OrderPriority.Urgent, 2, false));
        state.Orders.Single().TransitionTo(OrderStatus.OnHold, "QA", Now, "Inspection");
        var plan = Plan(state);
        Assert.Empty(plan.Actions);
        Assert.Contains(plan.Blocked, b => b.Reason == ScheduleReason.QualityHold);
    }
    [Fact]
    public void Planning_is_read_only_and_deterministic_even_if_input_order_changes()
    {
        var state = State((1, "6060", OrderPriority.Normal, 1, false), (2, "6060", OrderPriority.Normal, 1, false));
        var versions = state.Baskets.Select(b => b.Version).ToArray();
        var historyCounts = state.Orders.Select(o => o.History.Count).ToArray();
        var first = Plan(state);
        var second = Plan(state with { Baskets = state.Baskets.Reverse().ToArray(), Orders = state.Orders.Reverse().ToArray() });
        Assert.Equal(first.Actions.Select(a => (a.Kind, a.Reference, a.RecipeId)), second.Actions.Select(a => (a.Kind, a.Reference, a.RecipeId)));
        Assert.Equal(versions, state.Baskets.Select(b => b.Version));
        Assert.Equal(historyCounts, state.Orders.Select(o => o.History.Count));
    }
    [Fact]
    public void Active_transfer_is_acknowledged_before_new_operations()
    {
        var state = State((1, "6060", OrderPriority.Normal, 1, false), (2, "6060", OrderPriority.Normal, 1, true));
        var order = state.Orders.First();
        var transport = Transport.Dispatch("TR-2026-0042", ExtrusionSimulator.Shuttle, [order], "test", Now);
        state.Baskets.First().Depart("PRESS-01", "OVEN-01", transport.TransportNumber);
        state.Equipment.Single(e => e.Kind == EquipmentKind.Shuttle).Start(transport.TransportNumber, "test", Now);
        var plan = Plan(state with { Transports = [transport] });
        Assert.Equal(ScheduledActionKind.ArriveTransport, plan.Next!.Kind);
        Assert.Equal(transport.TransportNumber, plan.Next.Reference);
    }
    [Fact]
    public void Both_active_ovens_make_progress_even_with_a_fixed_clock()
    {
        var state = State((1, "6060", OrderPriority.Normal, 1, true));
        var second = new Basket("AL-1-K02", state.MaterialLots.Single().Number, 10, 1m, 6.4m, "OVEN-02");
        var firstCycle = new OvenCycle("OVEN-01", state.Recipes.First(), state.Baskets.ToArray(), state.MaterialLots.ToArray(), Now);
        var secondCycle = new OvenCycle("OVEN-02", state.Recipes.First(), [second], state.MaterialLots.ToArray(), Now);
        foreach (var cycle in new[] { firstCycle, secondCycle }) state.Equipment.Single(e => e.Id == cycle.EquipmentId).Start(cycle.Id.ToString(), "test", Now);
        state = state with { Baskets = [.. state.Baskets, second], Cycles = [firstCycle, secondCycle] };
        Assert.Equal("OVEN-01", Plan(state).Next!.Resource);
        firstCycle.Advance(1, Now);
        Assert.Equal("OVEN-02", Plan(state).Next!.Resource);
    }
}
