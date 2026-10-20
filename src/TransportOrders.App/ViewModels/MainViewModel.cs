using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TransportOrders.App.Services;
using TransportOrders.Core;

namespace TransportOrders.App.ViewModels;

/// <summary>
/// Screen logic of the main window. It has no WPF dependency, so it is unit-tested with fakes
/// (repository, clock, dialogs) in TransportOrders.App.Tests. Orders only start moving through
/// "Plan loads", where the domain's full-load rule applies.
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    public const string AllStatuses = "All";

    private readonly IOrderRepository _repository;
    private readonly IUserMessageService _messages;
    private readonly IDialogService _dialogs;
    private readonly ILocalizer _localizer;
    private readonly ICurrentUser _user;
    private readonly TimeProvider _clock;
    private TimeZoneInfo _plantTimeZone;
    private readonly ILogger<MainViewModel> _logger;
    private readonly IOperatorCommandClient? _operatorCommands;
    private HashSet<string> _permissions = new(PlantPermissions.All, StringComparer.Ordinal);
    private OperatorAccessSnapshot? _operatorAccess;
    private bool _accessChecked;
    private IOperatorCommandClient Commands => _operatorCommands ?? throw new InvalidOperationException(_localizer["OperatorApi_Unavailable"]);
    private readonly List<OrderRowViewModel> _allOrders = [];
    private readonly Dictionary<string, Transport> _transports = [];
    private Func<string>? _describeLastAction;

    public MainViewModel(
        IOrderRepository repository,
        IUserMessageService messages,
        IDialogService dialogs,
        ILocalizer localizer,
        ICurrentUser user,
        TimeProvider clock,
        TimeZoneInfo plantTimeZone,
        ILogger<MainViewModel> logger, IOperatorCommandClient? operatorCommands = null)
    {
        _repository = repository;
        _messages = messages;
        _dialogs = dialogs;
        _localizer = localizer;
        _user = user;
        _clock = clock;
        _plantTimeZone = plantTimeZone;
        _logger = logger;
        _operatorCommands = operatorCommands;

        StatusFilters = [new FilterOption(AllStatuses, ""), .. Enum.GetNames<OrderStatus>().Select(s => new FilterOption(s, ""))];
        Languages = Localizer.SupportedLanguages.Select(l => new FilterOption(l, l == "de" ? "Deutsch" : "English")).ToList();
        _selectedLanguage = localizer.Language;
        localizer.LanguageChanged += (_, _) => OnLanguageChanged();
        ApplyLanguage();
    }

    /// <summary>Orders matching the status filter, the search text and the heat filter.</summary>
    public ObservableCollection<OrderRowViewModel> VisibleOrders { get; } = [];

    public IReadOnlyList<FilterOption> StatusFilters { get; }

    public IReadOnlyList<FilterOption> Languages { get; }

    public ExtrusionViewModel? Extrusion { get; private set; }
    public AlarmViewModel? Alarms { get; private set; }
    public RecipeViewModel? Recipes { get; private set; }
    public void InitializeRecipes(RecipeViewModel recipes) { Recipes = recipes; OnPropertyChanged(nameof(Recipes)); }
    public void InitializeAlarms(AlarmViewModel alarms) { Alarms = alarms; OnPropertyChanged(nameof(Alarms)); }
    [ObservableProperty] private string _operatorStatus = string.Empty;
    public void SetPlantTimeZone(string id)
    {
        _plantTimeZone = TimeZoneInfo.FindSystemTimeZoneById(id);
        RefreshClock();
    }
    public void SetOperatorAccess(OperatorAccessSnapshot? access)
    {
        _accessChecked = true; _operatorAccess = access;
        _permissions = new(access?.Permissions ?? [], StringComparer.Ordinal);
        OperatorStatus = access is null ? _localizer["Access_Unavailable"] : _localizer.Format("Access_Status", access.User,
            access.Permissions.Count == 0 ? _localizer["Access_ReadOnly"] : string.Join(", ", access.Roles.Select(r => _localizer[$"Access_{r}"])));
        Extrusion?.SetPermissions(_permissions);
        PlanLoadsCommand.NotifyCanExecuteChanged(); HoldOrReleaseCommand.NotifyCanExecuteChanged();
        CancelOrderCommand.NotifyCanExecuteChanged(); CompleteTransportCommand.NotifyCanExecuteChanged();
    }

    public void InitializeExtrusion(ExtrusionSimulator simulator, IEquipmentControlClient? equipmentControls = null)
    {
        Extrusion = new ExtrusionViewModel(simulator, async () =>
        {
            if (IsBusy) return new SimulationResult(SimulationAction.Waiting);
            SetBusy(_localizer["Plant_Stepping"]);
            try
            {
                return await Commands.StepAsync();
            }
            finally
            {
                await LoadAsync();
            }
        }, _localizer, _messages, LoadAsync, equipmentControls, _operatorCommands);
        OnPropertyChanged(nameof(Extrusion));
        if (_accessChecked) SetOperatorAccess(_operatorAccess);
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ClearFiltersCommand))]
    private string _selectedStatusFilter = AllStatuses;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ClearSearchCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearFiltersCommand))]
    private string _searchText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHeatFilter), nameof(HeatFilterLabel))]
    [NotifyCanExecuteChangedFor(nameof(ClearHeatFilterCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearFiltersCommand))]
    private string? _heatFilter;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HoldButtonText))]
    [NotifyCanExecuteChangedFor(nameof(HoldOrReleaseCommand))]
    [NotifyCanExecuteChangedFor(nameof(CompleteTransportCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelOrderCommand))]
    [NotifyCanExecuteChangedFor(nameof(ShowHeatCommand))]
    [NotifyCanExecuteChangedFor(nameof(ShowGenealogyCommand))]
    private OrderRowViewModel? _selectedOrder;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PlanLoadsCommand))]
    [NotifyCanExecuteChangedFor(nameof(HoldOrReleaseCommand))]
    [NotifyCanExecuteChangedFor(nameof(CompleteTransportCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelOrderCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReloadCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string _busyMessage = string.Empty;

    [ObservableProperty]
    private string _selectionHint = string.Empty;

    [ObservableProperty]
    private string _lastAction = string.Empty;

    [ObservableProperty]
    private string _scanText = string.Empty;

    [ObservableProperty]
    private string _scanMessage = string.Empty;

    [ObservableProperty]
    private string _selectedLanguage;

    [ObservableProperty]
    private bool _isEmptyResult;

    [ObservableProperty]
    private string _shownText = string.Empty;

    [ObservableProperty]
    private int _totalCount;

    [ObservableProperty]
    private int _plannedCount;

    [ObservableProperty]
    private int _onHoldCount;

    [ObservableProperty]
    private int _inTransitCount;

    [ObservableProperty]
    private int _doneCount;

    [ObservableProperty]
    private int _cancelledCount;

    [ObservableProperty]
    private string _shiftTitle = string.Empty;

    [ObservableProperty]
    private ShiftSummary? _shift;

    [ObservableProperty]
    private string _averageTransitText = string.Empty;

    [ObservableProperty]
    private string _averageFillText = string.Empty;

    public bool HasHeatFilter => HeatFilter is not null;

    public string HeatFilterLabel => HeatFilter is null ? string.Empty : _localizer.Format("Heat_Filter", HeatFilter);

    /// <summary>"Release" for material on hold, "Hold" otherwise.</summary>
    public string HoldButtonText => _localizer[SelectedOrder?.Status == OrderStatus.OnHold ? "Action_Release" : "Action_Hold"];

    private DateTime UtcNow => _clock.GetUtcNow().UtcDateTime;

    public Task LoadAsync() => LoadAsync(false);

    public async Task LoadAsync(bool silent)
    {
        var selectedNumber = SelectedOrder?.OrderNumber;
        SetBusy(_localizer["Busy_Loading"]);
        try
        {
            var state = await Task.Run(() => _repository.GetPlantStateAsync());
            var orders = state.Orders;
            var transports = state.Transports;

            _transports.Clear();
            foreach (var transport in transports)
            {
                _transports[transport.TransportNumber] = transport;
            }

            _allOrders.Clear();
            _allOrders.AddRange(orders.Select(o => new OrderRowViewModel(o, _localizer, () => UtcNow, TransportOf)));
            Extrusion?.Refresh(state);
            _logger.LogInformation("Loaded {Count} orders and {Transports} transports", _allOrders.Count, _transports.Count);

            RefreshList(_allOrders.FirstOrDefault(r => r.OrderNumber == selectedNumber));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Loading orders failed");
            if (!silent) _messages.ShowError(_localizer.Format("Error_Load", ex.GetBaseException().Message));
        }
        finally
        {
            ClearBusy();
        }
    }

    /// <summary>Called periodically: due times pass and shifts change even when no data changes.</summary>
    public void RefreshClock()
    {
        foreach (var row in _allOrders)
        {
            row.Refresh();
        }

        UpdateShiftSummary();
    }

    [RelayCommand(CanExecute = nameof(CanReload))]
    private Task ReloadAsync()
    {
        Extrusion?.ResetOperatorDrafts();
        return LoadAsync();
    }

    private bool CanReload() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanClearSearch))]
    private void ClearSearch() => SearchText = string.Empty;

    private bool CanClearSearch() => SearchText.Length > 0;

    [RelayCommand(CanExecute = nameof(CanClearFilters))]
    private void ClearFilters()
    {
        SearchText = string.Empty;
        SelectedStatusFilter = AllStatuses;
        HeatFilter = null;
    }

    private bool CanClearFilters() => SearchText.Length > 0 || SelectedStatusFilter != AllStatuses || HeatFilter is not null;

    /// <summary>Traceability: show every order made from the selected order's heat.</summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void ShowHeat()
    {
        var heat = SelectedOrder!.HeatNumber;
        SearchText = string.Empty;
        SelectedStatusFilter = AllStatuses;
        HeatFilter = heat;
    }

    /// <summary>Traceability: the heat's ingots and the pieces made from them, as a tree.</summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void ShowGenealogy()
    {
        var row = SelectedOrder!;
        var genealogy = HeatGenealogy.Build(row.HeatNumber, _allOrders.Select(r => r.Order));
        _dialogs.ShowGenealogy(new GenealogyViewModel(genealogy, row.PieceNumber, _localizer));
    }

    private bool HasSelection() => SelectedOrder is not null;

    [RelayCommand(CanExecute = nameof(CanClearHeatFilter))]
    private void ClearHeatFilter() => HeatFilter = null;

    private bool CanClearHeatFilter() => HeatFilter is not null;

    /// <summary>A barcode scanner types the order number followed by Enter.</summary>
    [RelayCommand]
    private void Scan()
    {
        var code = ScanText.Trim();
        if (code.Length == 0)
        {
            return;
        }

        var row = _allOrders.FirstOrDefault(r => string.Equals(r.OrderNumber, code, StringComparison.OrdinalIgnoreCase))
            ?? _allOrders.Where(r => string.Equals(r.PieceNumber, code, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.OrderNumber, StringComparer.Ordinal).FirstOrDefault();
        if (row is null)
        {
            _logger.LogWarning("Scanned code {Code:l} matches no order", code);
            ScanMessage = _localizer.Format("Scan_NotFound", code);
            return;
        }

        ScanText = string.Empty;
        ScanMessage = string.Empty;
        if (!IsVisible(row))
        {
            SearchText = string.Empty;
            SelectedStatusFilter = AllStatuses;
            HeatFilter = null;
        }

        RefreshList(row);
    }

    /// <summary>Opens the load plan: the only way orders start moving, so every trip follows the full-load rule.</summary>
    [RelayCommand(CanExecute = nameof(CanPlanLoads))]
    private void PlanLoads() =>
        _dialogs.ShowLoadPlan(new LoadPlanViewModel(
            () => LoadPlanner.Plan(_allOrders.Select(r => r.Order), Fleet.Extrusion, UtcNow),
            DispatchAsync,
            _localizer));

    private bool CanPlanLoads() => !IsBusy && _permissions.Contains(PlantPermissions.Dispatch);

    /// <summary>Dispatches one proposal; returns the transport number, or null when it failed (the user was told why).</summary>
    public async Task<string?> DispatchAsync(LoadProposal proposal, PartLoadReason? reason)
    {
        if (IsBusy || !_permissions.Contains(PlantPermissions.Dispatch)) return null;
        SetBusy(_localizer["Busy_Dispatching"]);
        try
        {
            if (Extrusion?.IsFaulted == true)
            {
                _messages.ShowError(_localizer["Plant_Result_Faulted"]);
                return null;
            }
            // The manual planner follows the same physical reservations as the simulator.
            var current = await _repository.GetPlantStateAsync();
            var positions = current.Locate();
            if (current.Locations.FirstOrDefault(s => s.Id == proposal.TargetLocation) is { } station
                && (positions.Any(b => b.IsMoving)
                    || current.Occupied(station.Id) + current.Reserved(station.Id)
                        + proposal.Orders.Count > station.Capacity))
            {
                _messages.ShowError(_localizer["Plant_DestinationBlocked"]);
                return null;
            }
            string number;
            try
            {
                number = await Commands.DispatchAsync(proposal, reason);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Central transport dispatch failed");
                _messages.ShowError(_localizer.Format("Error_Dispatch", ex.Message));
                await LoadAsync();
                return null;
            }

            _logger.LogInformation(
                "{Transport:l} dispatched by {User:l}: {Vehicle:l} {From:l} -> {To:l}, {Orders} orders, {Fill:P0} of the loading length{Reason:l}",
                number, _user.Name, proposal.Vehicle.Type.ToString(), proposal.SourceLocation, proposal.TargetLocation,
                proposal.Orders.Count, proposal.FillRate, reason is null ? string.Empty : $", part load: {reason}");
            _describeLastAction = () => _localizer.Format("LastAction_Dispatch", number, proposal.Orders.Count, proposal.FillRate);
            await LoadAsync();
            return number;
        }
        finally
        {
            ClearBusy();
        }
    }

    [RelayCommand(CanExecute = nameof(CanHoldOrRelease))]
    private async Task HoldOrReleaseAsync()
    {
        if (SelectedOrder is not { } row)
        {
            return;
        }

        if (row.Status == OrderStatus.OnHold)
        {
            await ChangeStatusAsync(OrderStatus.Planned, null, row.Order);
            return;
        }

        var reason = _dialogs.AskForReason(_localizer["Hold_Title"], _localizer.Format("Hold_Prompt", row.OrderNumber));
        if (reason is not null)
        {
            await ChangeStatusAsync(OrderStatus.OnHold, reason, row.Order);
        }
    }

    private bool CanHoldOrRelease() => _permissions.Contains(PlantPermissions.OrderQuality)
        && (CanChangeTo(OrderStatus.OnHold) || CanChangeTo(OrderStatus.Planned));

    /// <summary>The selected order's transport arrived: every order on it becomes Done.</summary>
    [RelayCommand(CanExecute = nameof(CanCompleteTransport))]
    private async Task CompleteTransportAsync()
    {
        if (Extrusion?.IsFaulted == true)
        {
            _messages.ShowError(_localizer["Plant_Result_Faulted"]);
            return;
        }
        if (SelectedOrder?.TransportNumber is not { } number || !_transports.TryGetValue(number, out var transport))
        {
            return;
        }

        if (!_messages.Confirm(_localizer.Format("Confirm_Complete", number, transport.TargetLocation, transport.OrderCount)))
        {
            return;
        }

        var orders = _allOrders.Where(r => r.TransportNumber == number).Select(r => r.Order).ToList();
        SetBusy(_localizer.Format("Busy_Saving", number));
        try
        {
            await Commands.ConfirmTransportAsync(number, orders,
                _localizer.Format("Confirm_Complete", number, transport.TargetLocation, transport.OrderCount));
            _logger.LogInformation("{Transport:l} arrived, confirmed by {User:l}", number, _user.Name);
            _describeLastAction = () => _localizer.Format("LastAction_Arrived", number);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Completing transport {Transport:l} failed", number);
            _messages.ShowError(_localizer.Format("Error_SaveTransport", number, ex.Message));
        }

        // Reload in both cases: after a failure the in-memory transport and orders already changed.
        await LoadAsync();
        ClearBusy();
    }

    private bool CanCompleteTransport() =>
        !IsBusy
        && _permissions.Contains(PlantPermissions.Arrival)
        && SelectedOrder is { Status: OrderStatus.InTransit, TransportNumber: { } number }
        && _transports.TryGetValue(number, out var transport)
        && transport.Status == TransportStatus.InTransit;

    [RelayCommand(CanExecute = nameof(CanCancelOrder))]
    private async Task CancelOrderAsync()
    {
        if (SelectedOrder is { } row && _messages.Confirm(_localizer.Format("Confirm_Cancel", row.OrderNumber)))
        {
            await ChangeStatusAsync(OrderStatus.Cancelled, null, row.Order);
        }
    }

    private bool CanCancelOrder() => _permissions.Contains(PlantPermissions.OrderCancel) && CanChangeTo(OrderStatus.Cancelled)
        && !(SelectedOrder is { Status: OrderStatus.InTransit } moving
            && ExtrusionPlant.Stations.Any(s => s.Id == moving.SourceLocation));

    private bool CanChangeTo(OrderStatus target) =>
        !IsBusy && SelectedOrder?.Order.CanTransitionTo(target) == true;

    private async Task ChangeStatusAsync(OrderStatus target, string? note, TransportOrder? expectedOrder = null)
    {
        if (SelectedOrder is not { } row)
        {
            return;
        }

        var previous = row.Status;
        if (!row.Order.CanTransitionTo(target))
        {
            _messages.ShowError(_localizer.Format("Error_Transition", row.OrderNumber,
                OrderRowViewModel.StatusName(_localizer, previous), OrderRowViewModel.StatusName(_localizer, target)));
            return;
        }

        SetBusy(_localizer.Format("Busy_Saving", row.OrderNumber));
        try
        {
            await Commands.ChangeOrderAsync(expectedOrder ?? row.Order, target, note ?? OrderRowViewModel.StatusName(_localizer, target));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Saving {OrderNumber:l} ({From:l} -> {To:l}) failed",
                row.OrderNumber, previous.ToString(), target.ToString());

            // Stay busy while the message is shown: the in-memory order already changed,
            // so the commands must not offer actions for a status that was never saved.
            _messages.ShowError(_localizer.Format("Error_Save", row.OrderNumber, ex.Message));
            await LoadAsync();
            return;
        }

        ClearBusy();
        _logger.LogInformation("{OrderNumber:l}: {From:l} -> {To:l} by {User:l}{Note:l}",
            row.OrderNumber, previous.ToString(), target.ToString(), _user.Name, note is null ? string.Empty : $" ({note})");
        _describeLastAction = () => _localizer.Format("LastAction", row.OrderNumber,
            OrderRowViewModel.StatusName(_localizer, previous), OrderRowViewModel.StatusName(_localizer, target));
        await LoadAsync();
    }

    partial void OnSelectedStatusFilterChanged(string value) => RefreshList(SelectedOrder);

    partial void OnSearchTextChanged(string value) => RefreshList(SelectedOrder);

    partial void OnHeatFilterChanged(string? value) => RefreshList(SelectedOrder);

    partial void OnSelectedOrderChanged(OrderRowViewModel? value) => SelectionHint = DescribeSelection(value);

    partial void OnScanTextChanged(string value)
    {
        if (value.Length > 0)
        {
            ScanMessage = string.Empty;
        }
    }

    partial void OnSelectedLanguageChanged(string value) => _localizer.SetLanguage(value);

    private Transport? TransportOf(string number) => _transports.GetValueOrDefault(number);

    private void OnLanguageChanged()
    {
        if (_accessChecked) SetOperatorAccess(_operatorAccess);
        SelectedLanguage = _localizer.Language;
        ApplyLanguage();
        foreach (var row in _allOrders)
        {
            row.Refresh();
        }
    }

    private void ApplyLanguage()
    {
        foreach (var option in StatusFilters)
        {
            option.Label = option.Key == AllStatuses ? _localizer["Filter_All"] : _localizer[$"Status_{option.Key}"];
        }

        OnPropertyChanged(nameof(HeatFilterLabel));
        OnPropertyChanged(nameof(HoldButtonText));
        SelectionHint = DescribeSelection(SelectedOrder);
        ShownText = _localizer.Format("Shown", VisibleOrders.Count, TotalCount);
        LastAction = _describeLastAction?.Invoke() ?? string.Empty;
        UpdateShiftSummary();
    }

    private void SetBusy(string message)
    {
        BusyMessage = message;
        IsBusy = true;
    }

    private void ClearBusy()
    {
        IsBusy = false;
        BusyMessage = string.Empty;
    }

    private void UpdateShiftSummary()
    {
        var summary = ShiftSummary.Calculate(_allOrders.Select(r => r.Order), _transports.Values, UtcNow, _plantTimeZone);
        Shift = summary;
        ShiftTitle = _localizer.Format("Shift_Title",
            _localizer[$"Shift_{summary.Shift.Kind}"], summary.Shift.StartLocal, summary.Shift.EndLocal);
        AverageTransitText = summary.AverageTransitTime is { } average
            ? _localizer.Format("Kpi_Minutes", average.TotalMinutes)
            : _localizer["Kpi_None"];
        AverageFillText = summary.AverageFillRate is { } fill
            ? _localizer.Format("Kpi_Percent", fill)
            : _localizer["Kpi_None"];
    }

    private bool IsVisible(OrderRowViewModel row)
    {
        var statusMatches = SelectedStatusFilter == AllStatuses
            || string.Equals(row.Status.ToString(), SelectedStatusFilter, StringComparison.Ordinal);
        var searchMatches = string.IsNullOrWhiteSpace(SearchText)
            || row.OrderNumber.Contains(SearchText.Trim(), StringComparison.OrdinalIgnoreCase)
            || row.PieceNumber.Contains(SearchText.Trim(), StringComparison.OrdinalIgnoreCase)
            || row.HeatNumber.Contains(SearchText.Trim(), StringComparison.OrdinalIgnoreCase);
        var heatMatches = HeatFilter is null || row.HeatNumber == HeatFilter;
        return statusMatches && searchMatches && heatMatches;
    }

    /// <summary>Explains what can happen next to the selected order.</summary>
    private string DescribeSelection(OrderRowViewModel? row) => row switch
    {
        null => _localizer["Hint_None"],
        { Status: OrderStatus.Planned } => _localizer.Format("Hint_Planned", row.OrderNumber),
        { Status: OrderStatus.OnHold } => _localizer.Format("Hint_OnHold", row.OrderNumber, row.HoldReason),
        { Status: OrderStatus.InTransit } => _localizer.Format("Hint_InTransit", row.OrderNumber, row.TransportNumber),
        _ => _localizer.Format("Hint_Final", row.OrderNumber, row.StatusText),
    };

    /// <summary>
    /// Re-applies the filters, recomputes the counters and keeps <paramref name="preferredSelection"/>
    /// selected when it is still visible.
    /// </summary>
    private void RefreshList(OrderRowViewModel? preferredSelection)
    {
        var visible = _allOrders.Where(IsVisible).ToList();

        // Rebuild only when the rows change, so the grid keeps its scroll position and sort order.
        if (!visible.SequenceEqual(VisibleOrders))
        {
            VisibleOrders.Clear();
            foreach (var row in visible)
            {
                VisibleOrders.Add(row);
            }
        }

        SelectedOrder = preferredSelection is not null && visible.Contains(preferredSelection) ? preferredSelection : null;

        TotalCount = _allOrders.Count;
        PlannedCount = _allOrders.Count(r => r.Status == OrderStatus.Planned);
        OnHoldCount = _allOrders.Count(r => r.Status == OrderStatus.OnHold);
        InTransitCount = _allOrders.Count(r => r.Status == OrderStatus.InTransit);
        DoneCount = _allOrders.Count(r => r.Status == OrderStatus.Done);
        CancelledCount = _allOrders.Count(r => r.Status == OrderStatus.Cancelled);
        IsEmptyResult = TotalCount > 0 && visible.Count == 0;
        ShownText = _localizer.Format("Shown", visible.Count, TotalCount);
        LastAction = _describeLastAction?.Invoke() ?? string.Empty;
        UpdateShiftSummary();

        // The selected row may have changed status without the selection itself changing.
        SelectionHint = DescribeSelection(SelectedOrder);
        OnPropertyChanged(nameof(HoldButtonText));
        HoldOrReleaseCommand.NotifyCanExecuteChanged();
        CompleteTransportCommand.NotifyCanExecuteChanged();
        CancelOrderCommand.NotifyCanExecuteChanged();
    }
}
