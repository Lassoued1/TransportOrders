using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using TransportOrders.App.Services;
using TransportOrders.App.ViewModels;
using TransportOrders.Core;

namespace TransportOrders.App.Tests;

public class MainViewModelTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 2, 10, 0, 0, TimeSpan.Zero);   // early shift (plant zone = UTC)

    private readonly FakeTimeProvider _clock = new(Now);
    private readonly FakeMessages _messages = new();
    private readonly Localizer _localizer = new();
    private readonly FakeOrderRepository _repository = new FakeOrderRepository(Now.UtcDateTime.AddHours(-2))
        .Add("TO-2026-0001", "H-1")
        .Add("TO-2026-0002", "H-2", OrderStatus.InTransit, OrderPriority.Urgent, dueIn: TimeSpan.FromHours(1))
        .Add("TO-2026-0003", "H-1", OrderStatus.Done)
        .Add("TO-2026-0004", "H-2")
        .Add("TO-2026-0005", "H-1", OrderStatus.Cancelled)
        .Add("TO-2026-0012", "H-3", dueIn: TimeSpan.FromHours(3));

    private async Task<MainViewModel> LoadedViewModelAsync()
    {
        var vm = new MainViewModel(_repository, _messages, _localizer, new FakeUser("driver.test"), _clock,
            TimeZoneInfo.Utc, NullLogger<MainViewModel>.Instance);
        await vm.LoadAsync();
        return vm;
    }

    private static void Select(MainViewModel vm, string number) =>
        vm.SelectedOrder = vm.VisibleOrders.Single(r => r.OrderNumber == number);

    private static string[] Visible(MainViewModel vm) => vm.VisibleOrders.Select(r => r.OrderNumber).ToArray();

    [Fact]
    public async Task Load_shows_every_order_with_counts_and_a_neutral_hint()
    {
        var vm = await LoadedViewModelAsync();

        Assert.Equal(6, vm.VisibleOrders.Count);
        Assert.Equal((6, 3, 1, 1, 1), (vm.TotalCount, vm.PlannedCount, vm.InTransitCount, vm.DoneCount, vm.CancelledCount));
        Assert.Equal("Select an order to change its status.", vm.SelectionHint);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task Status_filter_and_search_combine_and_counts_ignore_them()
    {
        var vm = await LoadedViewModelAsync();

        vm.SelectedStatusFilter = nameof(OrderStatus.Planned);
        vm.SearchText = "001";

        Assert.Equal(["TO-2026-0001", "TO-2026-0012"], Visible(vm));
        Assert.Equal("Shown: 2 of 6", vm.ShownText);
        Assert.Equal(6, vm.TotalCount);

        vm.SelectedStatusFilter = nameof(OrderStatus.Done);
        Assert.True(vm.IsEmptyResult);
        vm.ClearFiltersCommand.Execute(null);
        Assert.Equal(6, vm.VisibleOrders.Count);
    }

    [Theory]
    [InlineData("TO-2026-0001", true, false, true)]   // Planned
    [InlineData("TO-2026-0002", false, true, true)]   // InTransit
    [InlineData("TO-2026-0003", false, false, false)] // Done
    [InlineData("TO-2026-0005", false, false, false)] // Cancelled
    public async Task Commands_follow_the_status_of_the_selection(string number, bool start, bool complete, bool cancel)
    {
        var vm = await LoadedViewModelAsync();
        Assert.False(vm.StartOrderCommand.CanExecute(null));

        Select(vm, number);

        Assert.Equal((start, complete, cancel),
            (vm.StartOrderCommand.CanExecute(null), vm.CompleteOrderCommand.CanExecute(null), vm.CancelOrderCommand.CanExecute(null)));
    }

    [Fact]
    public async Task Start_saves_the_transition_with_user_and_time_and_updates_the_screen()
    {
        var vm = await LoadedViewModelAsync();
        Select(vm, "TO-2026-0001");

        await vm.StartOrderCommand.ExecuteAsync(null);

        var stored = _repository.LastStoredChange("TO-2026-0001");
        Assert.Equal((OrderStatus.Planned, OrderStatus.InTransit, "driver.test", Now.UtcDateTime),
            (stored.FromStatus, stored.ToStatus, stored.ChangedBy, stored.ChangedAt));
        Assert.Equal((2, 2), (vm.PlannedCount, vm.InTransitCount));
        Assert.Equal("TO-2026-0001: Planned → InTransit", vm.LastAction);
        Assert.Equal("TO-2026-0001 is InTransit. You can Complete or Cancel it.", vm.SelectionHint);
        Assert.Equal("TO-2026-0001", vm.SelectedOrder?.OrderNumber);
        Assert.Equal("Planned → InTransit", vm.SelectedOrder!.History[0].Change);
    }

    [Fact]
    public async Task Cancel_only_happens_after_confirmation()
    {
        var vm = await LoadedViewModelAsync();
        Select(vm, "TO-2026-0004");

        _messages.ConfirmAnswer = false;
        await vm.CancelOrderCommand.ExecuteAsync(null);
        Assert.Equal(OrderStatus.Planned, vm.SelectedOrder!.Status);
        Assert.Equal(0, _repository.SaveCount);

        _messages.ConfirmAnswer = true;
        await vm.CancelOrderCommand.ExecuteAsync(null);
        Assert.Equal(OrderStatus.Cancelled, vm.SelectedOrder!.Status);
        Assert.Equal(1, _repository.SaveCount);
        Assert.Equal(2, _messages.ConfirmCount);
    }

    [Fact]
    public async Task Failed_save_shows_an_error_with_commands_disabled_then_restores_the_stored_status()
    {
        var vm = await LoadedViewModelAsync();
        Select(vm, "TO-2026-0001");
        _repository.FailNextSave = new IOException("database is locked");
        (bool Busy, bool CanComplete)? whileMessageShown = null;
        _messages.OnError = () => whileMessageShown = (vm.IsBusy, vm.CompleteOrderCommand.CanExecute(null));

        await vm.StartOrderCommand.ExecuteAsync(null);

        var error = Assert.Single(_messages.Errors);
        Assert.Contains("TO-2026-0001 could not be saved", error);
        Assert.Contains("database is locked", error);
        Assert.Equal((true, false), whileMessageShown);
        Assert.Equal(OrderStatus.Planned, vm.SelectedOrder?.Status);
        Assert.True(vm.StartOrderCommand.CanExecute(null));
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task Scanning_selects_an_order_even_when_the_filters_hide_it()
    {
        var vm = await LoadedViewModelAsync();
        vm.SelectedStatusFilter = nameof(OrderStatus.Done);

        vm.ScanText = " to-2026-0004 ";
        vm.ScanCommand.Execute(null);

        Assert.Equal("TO-2026-0004", vm.SelectedOrder?.OrderNumber);
        Assert.Equal(MainViewModel.AllStatuses, vm.SelectedStatusFilter);
        Assert.Equal((string.Empty, string.Empty), (vm.ScanText, vm.ScanMessage));
    }

    [Fact]
    public async Task Scanning_an_unknown_code_explains_it_and_keeps_the_selection()
    {
        var vm = await LoadedViewModelAsync();
        Select(vm, "TO-2026-0002");

        vm.ScanText = "TO-2026-9999";
        vm.ScanCommand.Execute(null);

        Assert.Equal("No order found for “TO-2026-9999”.", vm.ScanMessage);
        Assert.Equal("TO-2026-0002", vm.SelectedOrder?.OrderNumber);
    }

    [Fact]
    public async Task Heat_filter_traces_every_order_of_the_same_melt()
    {
        var vm = await LoadedViewModelAsync();
        Select(vm, "TO-2026-0002");

        vm.ShowHeatCommand.Execute(null);

        Assert.Equal(["TO-2026-0002", "TO-2026-0004"], Visible(vm));
        Assert.Equal("Heat H-2", vm.HeatFilterLabel);
        Assert.Equal("TO-2026-0002", vm.SelectedOrder?.OrderNumber);

        vm.ClearHeatFilterCommand.Execute(null);
        Assert.Equal(6, vm.VisibleOrders.Count);
    }

    [Fact]
    public async Task Overdue_flags_and_shift_figures_follow_the_clock()
    {
        var vm = await LoadedViewModelAsync();
        Assert.Equal(ShiftKind.Early, vm.Shift!.Shift.Kind);
        Assert.Equal((4, 1, 1), (vm.Shift.OpenOrders, vm.Shift.OverdueOrders, vm.Shift.UrgentOpenOrders));
        Assert.True(vm.VisibleOrders.Single(r => r.OrderNumber == "TO-2026-0002").IsOverdue);

        _clock.Advance(TimeSpan.FromHours(2));
        vm.RefreshClock();

        Assert.Equal(2, vm.Shift.OverdueOrders);
        Assert.True(vm.VisibleOrders.Single(r => r.OrderNumber == "TO-2026-0012").IsOverdue);
    }

    [Fact]
    public async Task Switching_to_german_translates_labels_hint_and_statuses()
    {
        var vm = await LoadedViewModelAsync();
        Select(vm, "TO-2026-0001");

        vm.SelectedLanguage = "de";

        Assert.Equal("de", _localizer.Language);
        Assert.Equal("TO-2026-0001 ist Geplant. Mögliche Aktionen: Starten oder Stornieren.", vm.SelectionHint);
        Assert.Equal(["Alle", "Geplant", "Unterwegs", "Erledigt", "Storniert"], vm.StatusFilters.Select(f => f.Label));
        Assert.Equal("Unterwegs", vm.VisibleOrders.Single(r => r.OrderNumber == "TO-2026-0002").StatusText);
        Assert.StartsWith("Frühschicht", vm.ShiftTitle);
    }
}
