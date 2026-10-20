using System.IO;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using TransportOrders.App.Services;
using TransportOrders.App.ViewModels;
using TransportOrders.Core;

namespace TransportOrders.App.Tests;

public class MainViewModelTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 2, 10, 0, 0, TimeSpan.Zero);   // early shift (plant zone = UTC)
    private static readonly Vehicle Carrier = Fleet.Default.Single(v => v.Type == VehicleType.HeavyLoadCarrier);

    private readonly FakeTimeProvider _clock = new(Now);
    private readonly FakeMessages _messages = new();
    private readonly FakeDialogs _dialogs = new();
    private readonly Localizer _localizer = new();

    // Generic routes exercise the UI independently of the extrusion layout. 0001 + 0002 fill a crane to 90 %.
    private readonly FakeOrderRepository _repository = new FakeOrderRepository(Now.UtcDateTime.AddHours(-2))
        .Add("TO-2026-0001", "H-1", "H-1-1A", "H-1-1", 6.3m, 2m)
        .Add("TO-2026-0002", "H-1", "H-1-1B", "H-1-1", 6.3m, 2m)
        .Add("TO-2026-0003", "H-2", "H-2-1", null, 2.8m, 1.8m, "ESR plant", "Forge", OrderPriority.Urgent, TimeSpan.FromHours(1))
        .Add("TO-2026-0004", "H-2", "H-2-2", null, 2.8m, 18m, "ESR plant", "Forge", OrderPriority.Urgent)
        .Add("TO-2026-0005", "H-3", "H-3-1", null, 1.2m, 2m, "Heat treatment", "UT testing")
        .Add("TO-2026-0006", "H-4", "H-4-1", null, 2.6m, 16m, "Melt shop", "ESR plant")
        .Ship("TR-2026-0001", Carrier, PartLoadReason.UrgentMaterial, "TO-2026-0004")
        .Change("TO-2026-0005", OrderStatus.OnHold, "Hardness below specification")
        .Change("TO-2026-0006", OrderStatus.Cancelled);

    private async Task<MainViewModel> LoadedViewModelAsync()
    {
        var vm = new MainViewModel(_repository, _messages, _dialogs, _localizer, new FakeUser("driver.test"), _clock,
            TimeZoneInfo.Utc, NullLogger<MainViewModel>.Instance, new TestOperatorClient(_repository, _clock, "driver.test"));
        await vm.LoadAsync();
        return vm;
    }

    private static void Select(MainViewModel vm, string number) =>
        vm.SelectedOrder = vm.VisibleOrders.Single(r => r.OrderNumber == number);

    private static OrderRowViewModel Row(MainViewModel vm, string number) => vm.VisibleOrders.Single(r => r.OrderNumber == number);

    private static string[] Visible(MainViewModel vm) => vm.VisibleOrders.Select(r => r.OrderNumber).ToArray();

    [Fact]
    public async Task Load_shows_every_order_with_counts_and_a_neutral_hint()
    {
        var vm = await LoadedViewModelAsync();

        Assert.Equal(6, vm.VisibleOrders.Count);
        Assert.Equal((6, 3, 1, 1, 0, 1), (vm.TotalCount, vm.PlannedCount, vm.OnHoldCount, vm.InTransitCount, vm.DoneCount, vm.CancelledCount));
        Assert.Equal("Select an order to see what can happen next.", vm.SelectionHint);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task Status_filter_and_search_combine_and_counts_ignore_them()
    {
        var vm = await LoadedViewModelAsync();

        vm.SelectedStatusFilter = nameof(OrderStatus.Planned);
        vm.SearchText = "000";

        Assert.Equal(["TO-2026-0001", "TO-2026-0002", "TO-2026-0003"], Visible(vm));
        Assert.Equal("Shown: 3 of 6", vm.ShownText);

        vm.SelectedStatusFilter = nameof(OrderStatus.Done);
        Assert.True(vm.IsEmptyResult);
        vm.ClearFiltersCommand.Execute(null);
        Assert.Equal(6, vm.VisibleOrders.Count);
    }

    [Theory]
    [InlineData("TO-2026-0001", true, "_Hold", false, true)]      // Planned: waits for a full load
    [InlineData("TO-2026-0005", true, "_Release", false, true)]   // On hold
    [InlineData("TO-2026-0004", false, "_Hold", true, true)]      // In transit on TR-2026-0001
    [InlineData("TO-2026-0006", false, "_Hold", false, false)]    // Cancelled
    public async Task Commands_follow_the_status_of_the_selection(string number, bool hold, string holdText, bool complete, bool cancel)
    {
        var vm = await LoadedViewModelAsync();
        Assert.False(vm.HoldOrReleaseCommand.CanExecute(null));

        Select(vm, number);

        Assert.Equal((hold, holdText, complete, cancel),
            (vm.HoldOrReleaseCommand.CanExecute(null), vm.HoldButtonText, vm.CompleteTransportCommand.CanExecute(null),
                vm.CancelOrderCommand.CanExecute(null)));
        Assert.True(vm.PlanLoadsCommand.CanExecute(null));
    }

    [Fact]
    public async Task Plan_loads_dispatches_a_full_load_and_records_it_on_every_order()
    {
        var vm = await LoadedViewModelAsync();
        Select(vm, "TO-2026-0004");
        await vm.CompleteTransportCommand.ExecuteAsync(null);

        vm.PlanLoadsCommand.Execute(null);
        var plan = _dialogs.LoadPlan!;
        var full = plan.Proposals.Single(p => p.IsFull);
        Assert.Equal(("Overhead crane", "Forge → Heat treatment", "90%"), (full.VehicleText, full.RouteText, full.FillText));
        Assert.True(full.DispatchCommand.CanExecute(null));

        await full.DispatchCommand.ExecuteAsync(null);

        Assert.Equal("TR-2026-0002 dispatched: 2 order(s), 90% of the loading length.", plan.Message);
        Assert.All(new[] { "TO-2026-0001", "TO-2026-0002" }, n =>
        {
            Assert.Equal((OrderStatus.InTransit, "TR-2026-0002"), (Row(vm, n).Status, Row(vm, n).TransportNumber));
            Assert.Equal(("driver.test", "TR-2026-0002"), (_repository.LastStoredChange(n).ChangedBy, _repository.LastStoredChange(n).Note));
        });
        Assert.Equal((1, 2), (vm.PlannedCount, vm.InTransitCount));
        Assert.Equal("TR-2026-0002 dispatched: 2 order(s), 90%", vm.LastAction);
        Assert.Single(plan.Proposals);   // the plan was recalculated: only the urgent ingot is left
    }

    [Fact]
    public async Task A_part_load_can_only_be_dispatched_with_a_reason_which_is_recorded()
    {
        var vm = await LoadedViewModelAsync();
        Select(vm, "TO-2026-0004");
        await vm.CompleteTransportCommand.ExecuteAsync(null);
        vm.PlanLoadsCommand.Execute(null);
        var partLoad = _dialogs.LoadPlan!.Proposals.Single(p => p.IsPartLoad);
        Assert.Equal(nameof(PartLoadReason.UrgentMaterial), partLoad.SelectedReason);   // suggested: urgent, overdue ingot

        partLoad.SelectedReason = null;
        Assert.False(partLoad.DispatchCommand.CanExecute(null));

        partLoad.SelectedReason = nameof(PartLoadReason.EndOfShift);
        await partLoad.DispatchCommand.ExecuteAsync(null);

        var transport = _repository.Transports.Single(t => t.TransportNumber == "TR-2026-0002");
        Assert.Equal((true, PartLoadReason.EndOfShift), (transport.IsPartLoad, transport.PartLoadReason));
        Assert.Equal(OrderStatus.InTransit, Row(vm, "TO-2026-0003").Status);
    }

    [Fact]
    public async Task Hold_needs_a_reason_and_release_makes_the_material_plannable_again()
    {
        var vm = await LoadedViewModelAsync();
        Select(vm, "TO-2026-0001");

        _dialogs.ReasonAnswer = null;                                      // user cancelled the prompt
        await vm.HoldOrReleaseCommand.ExecuteAsync(null);
        Assert.Equal((OrderStatus.Planned, 0), (Row(vm, "TO-2026-0001").Status, _repository.SaveCount));

        _dialogs.ReasonAnswer = "Surface cracks";
        await vm.HoldOrReleaseCommand.ExecuteAsync(null);
        Assert.Equal((OrderStatus.OnHold, "Surface cracks"), (vm.SelectedOrder!.Status, vm.SelectedOrder.HoldReason));
        Assert.Equal("_Release", vm.HoldButtonText);
        Assert.Equal("Surface cracks", _repository.LastStoredChange("TO-2026-0001").Note);

        await vm.HoldOrReleaseCommand.ExecuteAsync(null);
        Assert.Equal(OrderStatus.Planned, vm.SelectedOrder!.Status);
        Assert.Equal(2, _dialogs.ReasonRequests);
    }

    [Fact]
    public async Task Completing_marks_every_order_of_the_transport_done()
    {
        var vm = await LoadedViewModelAsync();
        Select(vm, "TO-2026-0004");

        await vm.CompleteTransportCommand.ExecuteAsync(null);

        Assert.Equal(OrderStatus.Done, Row(vm, "TO-2026-0004").Status);
        Assert.Equal(TransportStatus.Done, _repository.Transports.Single().Status);
        Assert.Equal("TR-2026-0001 arrived", vm.LastAction);
        Assert.False(vm.CompleteTransportCommand.CanExecute(null));
    }

    [Fact]
    public async Task Failed_save_shows_an_error_with_commands_disabled_then_restores_the_stored_status()
    {
        var vm = await LoadedViewModelAsync();
        Select(vm, "TO-2026-0001");
        _dialogs.ReasonAnswer = "Surface cracks";
        _repository.FailNextSave = new IOException("database is locked");
        (bool Busy, bool CanHold)? whileMessageShown = null;
        _messages.OnError = () => whileMessageShown = (vm.IsBusy, vm.HoldOrReleaseCommand.CanExecute(null));

        await vm.HoldOrReleaseCommand.ExecuteAsync(null);

        var error = Assert.Single(_messages.Errors);
        Assert.Contains("TO-2026-0001 could not be saved", error);
        Assert.Contains("database is locked", error);
        Assert.Equal((true, false), whileMessageShown);
        Assert.Equal(OrderStatus.Planned, vm.SelectedOrder?.Status);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task Scanning_selects_an_order_even_when_the_filters_hide_it()
    {
        var vm = await LoadedViewModelAsync();
        vm.SelectedStatusFilter = nameof(OrderStatus.Planned);

        vm.ScanText = " to-2026-0005 ";
        vm.ScanCommand.Execute(null);

        Assert.Equal("TO-2026-0005", vm.SelectedOrder?.OrderNumber);
        Assert.Equal(MainViewModel.AllStatuses, vm.SelectedStatusFilter);

        vm.ScanText = "TO-2026-9999";
        vm.ScanCommand.Execute(null);
        Assert.Equal("No order found for “TO-2026-9999”.", vm.ScanMessage);
        Assert.Equal("TO-2026-0005", vm.SelectedOrder?.OrderNumber);
    }

    [Fact]
    public async Task Heat_filter_and_genealogy_trace_the_material()
    {
        var vm = await LoadedViewModelAsync();
        Select(vm, "TO-2026-0001");

        vm.ShowGenealogyCommand.Execute(null);
        var tree = _dialogs.Genealogy!;
        Assert.Equal("Material traceability · H-1", tree.Title);
        var ingot = Assert.Single(tree.Roots);
        Assert.Equal(("H-1-1", "no transport recorded"), (ingot.PieceNumber, ingot.Material));
        Assert.Equal([true, false], ingot.Children.Select(c => c.IsCurrent));

        vm.ShowHeatCommand.Execute(null);
        Assert.Equal(["TO-2026-0001", "TO-2026-0002"], Visible(vm));
        Assert.Equal("Batch H-1", vm.HeatFilterLabel);
    }

    [Fact]
    public async Task Shift_figures_count_trips_fill_rate_and_follow_the_clock()
    {
        var vm = await LoadedViewModelAsync();

        Assert.Equal(ShiftKind.Early, vm.Shift!.Shift.Kind);
        Assert.Equal((1, 1, 1), (vm.Shift.Trips, vm.Shift.PartLoads, vm.Shift.OnHoldOrders));
        Assert.Equal("23%", vm.AverageFillText);                         // 2.8 m on a 12 m carrier
        Assert.Equal(1, vm.Shift.OverdueOrders);

        _clock.Advance(TimeSpan.FromHours(3));
        vm.RefreshClock();

        Assert.Equal(5, vm.Shift.OverdueOrders);                         // every open order, including held and moving ones
        Assert.True(Row(vm, "TO-2026-0001").IsOverdue);
    }

    [Fact]
    public async Task Switching_to_german_translates_labels_hint_and_statuses()
    {
        var vm = await LoadedViewModelAsync();
        Select(vm, "TO-2026-0005");

        vm.SelectedLanguage = "de";

        Assert.Equal("de", _localizer.Language);
        Assert.Equal("TO-2026-0005 ist gesperrt: Hardness below specification. Erst nach der Freigabe kann er verladen werden, oder er wird storniert.",
            vm.SelectionHint);
        Assert.Equal(["Alle", "Geplant", "Gesperrt", "Unterwegs", "Erledigt", "Storniert"], vm.StatusFilters.Select(f => f.Label));
        Assert.Equal("_Freigeben", vm.HoldButtonText);
        Assert.StartsWith("Frühschicht", vm.ShiftTitle);
        Assert.Equal("23 %", vm.AverageFillText);
    }
}
