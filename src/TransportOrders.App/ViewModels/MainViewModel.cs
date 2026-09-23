using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TransportOrders.App.Services;
using TransportOrders.Core;

namespace TransportOrders.App.ViewModels;

/// <summary>
/// Screen logic of the main window. It has no WPF dependency, so it is unit-tested with fakes
/// (repository, clock, dialogs) in TransportOrders.App.Tests.
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    public const string AllStatuses = "All";

    private static readonly (string VerbKey, OrderStatus Target)[] Actions =
    [
        ("Verb_Start", OrderStatus.InTransit),
        ("Verb_Complete", OrderStatus.Done),
        ("Verb_Cancel", OrderStatus.Cancelled),
    ];

    private readonly IOrderRepository _repository;
    private readonly IUserMessageService _messages;
    private readonly ILocalizer _localizer;
    private readonly ICurrentUser _user;
    private readonly TimeProvider _clock;
    private readonly TimeZoneInfo _plantTimeZone;
    private readonly ILogger<MainViewModel> _logger;
    private readonly List<OrderRowViewModel> _allOrders = [];
    private (string OrderNumber, OrderStatus From, OrderStatus To)? _lastChange;

    public MainViewModel(
        IOrderRepository repository,
        IUserMessageService messages,
        ILocalizer localizer,
        ICurrentUser user,
        TimeProvider clock,
        TimeZoneInfo plantTimeZone,
        ILogger<MainViewModel> logger)
    {
        _repository = repository;
        _messages = messages;
        _localizer = localizer;
        _user = user;
        _clock = clock;
        _plantTimeZone = plantTimeZone;
        _logger = logger;

        StatusFilters = [new FilterOption(AllStatuses, ""), .. Enum.GetNames<OrderStatus>().Select(s => new FilterOption(s, ""))];
        Languages = Localizer.SupportedLanguages.Select(l => new FilterOption(l, l.ToUpperInvariant())).ToList();
        _selectedLanguage = localizer.Language;
        localizer.LanguageChanged += (_, _) => OnLanguageChanged();
        ApplyLanguage();
    }

    /// <summary>Orders matching the status filter, the search text and the heat filter.</summary>
    public ObservableCollection<OrderRowViewModel> VisibleOrders { get; } = [];

    public IReadOnlyList<FilterOption> StatusFilters { get; }

    public IReadOnlyList<FilterOption> Languages { get; }

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
    [NotifyCanExecuteChangedFor(nameof(StartOrderCommand))]
    [NotifyCanExecuteChangedFor(nameof(CompleteOrderCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelOrderCommand))]
    [NotifyCanExecuteChangedFor(nameof(ShowHeatCommand))]
    private OrderRowViewModel? _selectedOrder;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartOrderCommand))]
    [NotifyCanExecuteChangedFor(nameof(CompleteOrderCommand))]
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

    public bool HasHeatFilter => HeatFilter is not null;

    public string HeatFilterLabel => HeatFilter is null ? string.Empty : _localizer.Format("Heat_Filter", HeatFilter);

    private DateTime UtcNow => _clock.GetUtcNow().UtcDateTime;

    public async Task LoadAsync()
    {
        var selectedNumber = SelectedOrder?.OrderNumber;
        SetBusy(_localizer["Busy_Loading"]);
        try
        {
            var orders = await Task.Run(() => _repository.GetAllAsync());

            _allOrders.Clear();
            _allOrders.AddRange(orders.Select(o => new OrderRowViewModel(o, _localizer, () => UtcNow)));
            _logger.LogInformation("Loaded {Count} orders", _allOrders.Count);

            RefreshList(_allOrders.FirstOrDefault(r => r.OrderNumber == selectedNumber));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Loading orders failed");
            _messages.ShowError(_localizer.Format("Error_Load", ex.GetBaseException().Message));
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
    private Task ReloadAsync() => LoadAsync();

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
    [RelayCommand(CanExecute = nameof(CanShowHeat))]
    private void ShowHeat()
    {
        var heat = SelectedOrder!.HeatNumber;
        SearchText = string.Empty;
        SelectedStatusFilter = AllStatuses;
        HeatFilter = heat;
    }

    private bool CanShowHeat() => SelectedOrder is not null;

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

        var row = _allOrders.FirstOrDefault(r => string.Equals(r.OrderNumber, code, StringComparison.OrdinalIgnoreCase));
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

    [RelayCommand(CanExecute = nameof(CanStartOrder))]
    private Task StartOrderAsync() => ChangeStatusAsync(OrderStatus.InTransit);

    private bool CanStartOrder() => CanChangeTo(OrderStatus.InTransit);

    [RelayCommand(CanExecute = nameof(CanCompleteOrder))]
    private Task CompleteOrderAsync() => ChangeStatusAsync(OrderStatus.Done);

    private bool CanCompleteOrder() => CanChangeTo(OrderStatus.Done);

    [RelayCommand(CanExecute = nameof(CanCancelOrder))]
    private async Task CancelOrderAsync()
    {
        if (SelectedOrder is { } row && _messages.Confirm(_localizer.Format("Confirm_Cancel", row.OrderNumber)))
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
            // The domain enforces the transition rules and records the history entry;
            // the view model never assigns the status itself.
            row.Order.TransitionTo(target, _user.Name, UtcNow);
        }
        catch (InvalidStatusTransitionException ex)
        {
            _messages.ShowError(_localizer.Format("Error_Transition", row.OrderNumber,
                OrderRowViewModel.StatusName(_localizer, ex.From), OrderRowViewModel.StatusName(_localizer, ex.To)));
            return;
        }

        SetBusy(_localizer.Format("Busy_Saving", row.OrderNumber));
        try
        {
            await Task.Run(() => _repository.SaveAsync(row.Order));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Saving {OrderNumber:l} ({From:l} -> {To:l}) failed",
                row.OrderNumber, previous.ToString(), target.ToString());

            // Stay busy while the message is shown: the in-memory order already changed,
            // so the commands must not offer actions for a status that was never saved.
            _messages.ShowError(_localizer.Format("Error_Save", row.OrderNumber, ex.GetBaseException().Message));
            await LoadAsync();
            return;
        }

        ClearBusy();
        _logger.LogInformation("{OrderNumber:l}: {From:l} -> {To:l} by {User:l}",
            row.OrderNumber, previous.ToString(), target.ToString(), _user.Name);
        _lastChange = (row.OrderNumber, previous, target);
        UpdateLastAction();
        row.Refresh();
        RefreshList(row);
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

    private void OnLanguageChanged()
    {
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
        SelectionHint = DescribeSelection(SelectedOrder);
        ShownText = _localizer.Format("Shown", VisibleOrders.Count, TotalCount);
        UpdateLastAction();
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

    private void UpdateLastAction() =>
        LastAction = _lastChange is { } change
            ? _localizer.Format("LastAction", change.OrderNumber,
                OrderRowViewModel.StatusName(_localizer, change.From), OrderRowViewModel.StatusName(_localizer, change.To))
            : string.Empty;

    private void UpdateShiftSummary()
    {
        var summary = ShiftSummary.Calculate(_allOrders.Select(r => r.Order), UtcNow, _plantTimeZone);
        Shift = summary;
        ShiftTitle = _localizer.Format("Shift_Title",
            _localizer[$"Shift_{summary.Shift.Kind}"], summary.Shift.StartLocal, summary.Shift.EndLocal);
        AverageTransitText = summary.AverageTransitTime is { } average
            ? _localizer.Format("Kpi_Minutes", average.TotalMinutes)
            : _localizer["Kpi_None"];
    }

    private bool IsVisible(OrderRowViewModel row)
    {
        var statusMatches = SelectedStatusFilter == AllStatuses
            || string.Equals(row.Status.ToString(), SelectedStatusFilter, StringComparison.Ordinal);
        var searchMatches = string.IsNullOrWhiteSpace(SearchText)
            || row.OrderNumber.Contains(SearchText.Trim(), StringComparison.OrdinalIgnoreCase);
        var heatMatches = HeatFilter is null || row.HeatNumber == HeatFilter;
        return statusMatches && searchMatches && heatMatches;
    }

    /// <summary>Explains which actions the selected order allows, based on the domain rules.</summary>
    private string DescribeSelection(OrderRowViewModel? row)
    {
        if (row is null)
        {
            return _localizer["Hint_None"];
        }

        var allowed = Actions.Where(a => row.Order.CanTransitionTo(a.Target)).Select(a => _localizer[a.VerbKey]).ToList();
        return allowed.Count == 0
            ? _localizer.Format("Hint_Final", row.OrderNumber, row.StatusText)
            : _localizer.Format("Hint_Actions", row.OrderNumber, row.StatusText, string.Join(_localizer["Hint_Or"], allowed));
    }

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
        InTransitCount = _allOrders.Count(r => r.Status == OrderStatus.InTransit);
        DoneCount = _allOrders.Count(r => r.Status == OrderStatus.Done);
        CancelledCount = _allOrders.Count(r => r.Status == OrderStatus.Cancelled);
        IsEmptyResult = TotalCount > 0 && visible.Count == 0;
        ShownText = _localizer.Format("Shown", visible.Count, TotalCount);
        UpdateShiftSummary();

        // The selected row may have changed status without the selection itself changing.
        SelectionHint = DescribeSelection(SelectedOrder);
        StartOrderCommand.NotifyCanExecuteChanged();
        CompleteOrderCommand.NotifyCanExecuteChanged();
        CancelOrderCommand.NotifyCanExecuteChanged();
    }
}
