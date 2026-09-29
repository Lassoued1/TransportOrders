using TransportOrders.Core;
using static TransportOrders.Tests.TestOrders;

namespace TransportOrders.Tests;

public class LoadPlannerTests
{
    private static readonly DateTime Now = CreatedAt.AddHours(2);
    private static int _next;

    private static TransportOrder Order(decimal metres, decimal tonnes, string from = "Forge", string to = "Heat treatment",
        OrderPriority priority = OrderPriority.Normal, int dueInHours = 4) =>
        New($"TO-2026-{Interlocked.Increment(ref _next) % 10000:D4}", priority, CreatedAt.AddHours(dueInHours), tonnes, metres, from, to);

    [Fact]
    public void Plans_full_loads_per_route_and_leaves_held_material_alone()
    {
        var forge = new[] { Order(4.2m, 12.4m), Order(3.6m, 9.8m), Order(2.6m, 8.6m, priority: OrderPriority.Urgent), Order(1.4m, 6.3m) };
        var testing = new[] { Order(1.9m, 3.1m, "Heat treatment", "UT testing"), Order(1.8m, 4.2m, "Heat treatment", "UT testing") };
        var held = Order(1.2m, 2.4m, "Heat treatment", "UT testing");
        held.TransitionTo(OrderStatus.OnHold, "qa", CreatedAt.AddMinutes(1), "Hardness");
        var ingot = Order(2.8m, 18.4m, "ESR plant", "Forge", OrderPriority.Urgent);

        var plan = LoadPlanner.Plan([.. forge, .. testing, held, ingot], Fleet.Default, Now);

        Assert.Collection(
            plan.Proposals,
            p =>
            {   // one urgent ingot: only the carrier can take 18.4 t, so it is a part load with a suggested reason
                Assert.Equal((VehicleType.HeavyLoadCarrier, "ESR plant", false), (p.Vehicle.Type, p.SourceLocation, p.IsFull));
                Assert.Equal(PartLoadReason.UrgentMaterial, p.SuggestedReason);
            },
            p =>
            {   // four forged bars, 11.8 m on a 12 m bed
                Assert.Equal((VehicleType.HeavyLoadCarrier, true, 4), (p.Vehicle.Type, p.IsFull, p.Orders.Count));
                Assert.Equal(11.8m, p.LoadedLength);
            },
            p =>
            {   // two blocks, 3.7 m on a 4 m forklift
                Assert.Equal((VehicleType.Forklift, true), (p.Vehicle.Type, p.IsFull));
                Assert.DoesNotContain(held, p.Orders);
            });
        Assert.Empty(plan.Unplannable);
    }

    [Fact]
    public void Urgent_orders_are_loaded_even_when_another_combination_would_be_fuller()
    {
        var urgent = Order(3.5m, 5m, priority: OrderPriority.Urgent);
        var exactFit = new[] { Order(6m, 5m), Order(6m, 5m) };           // 12 m together, but without the urgent order

        var proposal = LoadPlanner.Plan([urgent, .. exactFit], [Carrier], Now).Proposals[0];

        Assert.Contains(urgent, proposal.Orders);
        Assert.Equal(9.5m, proposal.LoadedLength);
    }

    [Fact]
    public void A_full_small_vehicle_beats_a_half_empty_big_one()
    {
        var plan = LoadPlanner.Plan([Order(1.9m, 3m), Order(1.9m, 3m)], Fleet.Default, Now);

        Assert.Equal(VehicleType.Forklift, Assert.Single(plan.Proposals).Vehicle.Type);
    }

    [Fact]
    public void Short_heavy_pieces_are_flagged_as_limited_by_the_payload()
    {
        var plan = LoadPlanner.Plan([Order(2.9m, 19.6m, "Melt shop", "ESR plant"), Order(2.7m, 17.8m, "Melt shop", "ESR plant")],
            [Carrier], Now);

        var proposal = Assert.Single(plan.Proposals);
        Assert.False(proposal.IsFull);
        Assert.True(proposal.IsWeightLimited);
        Assert.Equal(PartLoadReason.PayloadLimit, proposal.SuggestedReason);
    }

    [Fact]
    public void Pieces_that_fit_no_vehicle_are_reported_instead_of_planned()
    {
        var tooLong = Order(25m, 10m);

        var plan = LoadPlanner.Plan([tooLong], Fleet.Default, Now);

        Assert.Empty(plan.Proposals);
        Assert.Equal(tooLong, Assert.Single(plan.Unplannable));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void Every_plan_respects_the_vehicle_limits_and_plans_each_order_exactly_once(int seed)
    {
        var random = new Random(seed);
        var routes = new[] { ("Forge", "Heat treatment"), ("Heat treatment", "UT testing"), ("ESR plant", "Forge") };
        var orders = Enumerable.Range(0, 40).Select(_ =>
        {
            var (from, to) = routes[random.Next(routes.Length)];
            return Order(Math.Round(0.5m + (decimal)random.NextDouble() * 6m, 1), Math.Round(1m + (decimal)random.NextDouble() * 20m, 1),
                from, to, random.Next(5) == 0 ? OrderPriority.Urgent : OrderPriority.Normal, random.Next(1, 8));
        }).ToList();

        var plan = LoadPlanner.Plan(orders, Fleet.Default, Now);

        Assert.All(plan.Proposals, p =>
        {
            Assert.NotEmpty(p.Orders);
            Assert.True(p.LoadedLength <= p.Vehicle.LoadingLength);
            Assert.True(p.LoadedWeight <= p.Vehicle.Payload);
            Assert.All(p.Orders, o => Assert.Equal((p.SourceLocation, p.TargetLocation), (o.SourceLocation, o.TargetLocation)));
        });
        var planned = plan.Proposals.SelectMany(p => p.Orders).Concat(plan.Unplannable).ToList();
        Assert.Equal(orders.Count, planned.Count);
        Assert.Equal(orders.Count, planned.Distinct().Count());
    }
}
