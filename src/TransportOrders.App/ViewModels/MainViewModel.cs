using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TransportOrders.App.Services;
using TransportOrders.Core;

namespace TransportOrders.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    public const string AllStatuses = "All";

    private readonly IOrderRepository _repository;
    private readonly IUserMessageService _messages;
    private readonly ObservableCollection<OrderRowViewModel> _orders = [];

    public MainViewModel(IOrderRepository repository, IUserMessageService messages)
    {
        _repository = repository;
        _messages = messages;
        OrdersView = CollectionViewSource.GetDefaultView(_orders);
        OrdersView.Filter = item => item is OrderRowViewModel row && IsVisible(row);
    }

    public ICollectionView OrdersView { get; }

    public IReadOnlyList<string> StatusFilters { get; } = [AllStatuses, .. Enum.GetNames<OrderStatus>()];

    [ObservableProperty]
    private string _selectedStatusFilter = AllStatuses;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartOrderCommand))]
    [NotifyCanExecuteChangedFor(nameof(CompleteOrderCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelOrderCommand))]
    private OrderRowViewModel? _selectedOrder;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartOrderCommand))]
    [NotifyCanExecuteChangedFor(nameof(CompleteOrderCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelOrderCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string _lastAction = string.Empty;

    [ObservableProperty]
    private int _visibleCount;

    [ObservableProperty]
    private int _totalCount;

    [ObservableProperty]
    private int _plannedCount;

    [ObservableProperty]
    private int _inTransitCount;

    [ObservableProperty]
    private int _doneCount;

    [ObservableProperty]
    private int _cancelledCount;

    public async Task LoadAsync()
    {
        var selectedId = SelectedOrder?.Order.Id;
        IsBusy = true;
        try
        {
            var orders = await Task.Run(() => _repository.GetAllAsync());

            _orders.Clear();
            foreach (var order in orders)
            {
                _orders.Add(new OrderRowViewModel(order));
            }

            RefreshList(_orders.FirstOrDefault(r => r.Order.Id == selectedId));
        }
        catch (Exception ex)
        {
            _messages.ShowError($"The transport orders could not be loaded.\n\n{ex.GetBaseException().Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanStartOrder))]
    private Task StartOrderAsync() => ChangeStatusAsync(OrderStatus.InTransit);

    private bool CanStartOrder() => CanChangeTo(OrderStatus.InTransit);

    [RelayCommand(CanExecute = nameof(CanCompleteOrder))]
    private Task CompleteOrderAsync() => ChangeStatusAsync(OrderStatus.Done);

    private bool CanCompleteOrder() => CanChangeTo(OrderStatus.Done);

    [RelayCommand(CanExecute = nameof(CanCancelOrder))]
    private async Task CancelOrderAsync()
    {
        if (SelectedOrder is { } row && _messages.Confirm($"Cancel order {row.OrderNumber}? This cannot be undone."))
        {
            await ChangeStatusAsync(OrderStatus.Cancelled);
        }
    }

    private bool CanCancelOrder() => CanChangeTo(OrderStatus.Cancelled);

    private bool CanChangeTo(OrderStatus target) =>
        !IsBusy && SelectedOrder?.Order.CanTransitionTo(target) == true;

    private async Task ChangeStatusAsync(OrderStatus target)
    {
        if (SelectedOrder is not { } row)
        {
            return;
        }

        var previous = row.Status;
        try
        {
            // The domain enforces the transition rules; the view model never assigns the status itself.
            row.Order.TransitionTo(target);
        }
        catch (InvalidStatusTransitionException ex)
        {
            _messages.ShowError(ex.Message);
            return;
        }

        IsBusy = true;
        try
        {
            await Task.Run(() => _repository.SaveAsync(row.Order));
        }
        catch (Exception ex)
        {
            IsBusy = false;
            _messages.ShowError(
                $"The change to order {row.OrderNumber} could not be saved and has been discarded.\n\n{ex.GetBaseException().Message}");

            // The in-memory order already changed: reload so the screen matches the database again.
            await LoadAsync();
            return;
        }

        IsBusy = false;
        row.Refresh();
        LastAction = $"Last change: {row.OrderNumber} {previous} → {target}";
        RefreshList(row);
    }

    partial void OnSelectedStatusFilterChanged(string value) => RefreshList(SelectedOrder);

    partial void OnSearchTextChanged(string value) => RefreshList(SelectedOrder);

    private bool IsVisible(OrderRowViewModel row)
    {
        var statusMatches = SelectedStatusFilter == AllStatuses
            || string.Equals(row.Status.ToString(), SelectedStatusFilter, StringComparison.Ordinal);
        var searchMatches = string.IsNullOrWhiteSpace(SearchText)
            || row.OrderNumber.Contains(SearchText.Trim(), StringComparison.OrdinalIgnoreCase);
        return statusMatches && searchMatches;
    }

    /// <summary>
    /// Re-applies filter and search, recomputes the counters and keeps <paramref name="preferredSelection"/>
    /// selected when it is still visible.
    /// </summary>
    private void RefreshList(OrderRowViewModel? preferredSelection)
    {
        OrdersView.Refresh();
        SelectedOrder = preferredSelection is not null && IsVisible(preferredSelection) ? preferredSelection : null;

        VisibleCount = _orders.Count(IsVisible);
        TotalCount = _orders.Count;
        PlannedCount = _orders.Count(r => r.Status == OrderStatus.Planned);
        InTransitCount = _orders.Count(r => r.Status == OrderStatus.InTransit);
        DoneCount = _orders.Count(r => r.Status == OrderStatus.Done);
        CancelledCount = _orders.Count(r => r.Status == OrderStatus.Cancelled);

        // The selected row may have changed status without the selection itself changing.
        StartOrderCommand.NotifyCanExecuteChanged();
        CompleteOrderCommand.NotifyCanExecuteChanged();
        CancelOrderCommand.NotifyCanExecuteChanged();
    }
}
