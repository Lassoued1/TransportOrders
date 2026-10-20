using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TransportOrders.App.Services;
using TransportOrders.Core;

namespace TransportOrders.App.ViewModels;

public sealed record StationCard(string Id, string Kind, string Capacity, string Contents, bool IsFull, bool HasReservation,
    string EquipmentState, string Process, double Progress);
public sealed record EquipmentRow(string Id, string Kind, string Status, string Mode, string Operation,
    Guid Version, EquipmentMode ModeValue, EquipmentCondition ConditionValue);
public sealed record ControlOption<T>(T Value, string Label);
public sealed record BasketRow(string Number, string Batch, string Material, int Quantity, string Position,
    string Destination, string State, string OrderNumber, bool IsMoving, bool IsHeld);
public sealed record PlantEvent(string Time, string Basket, string Description, string By);

/// <summary>Projection of persisted physical baskets, locations and transport history.</summary>
public sealed partial class ExtrusionViewModel : ObservableObject
{
    private readonly ExtrusionSimulator _simulator;
    private readonly Func<Task<SimulationResult>> _step;
    private readonly ILocalizer _localizer;
    private readonly IUserMessageService _messages;
    private readonly Func<Task>? _refreshOrders;
    private readonly IEquipmentControlClient? _equipmentControls;
    private readonly IOperatorCommandClient? _operatorCommands;
    private IOperatorCommandClient Commands => _operatorCommands ?? throw new InvalidOperationException(_localizer["OperatorApi_Unavailable"]);
    private bool _refreshingControls;
    private Guid? _controlDraftVersion;
    private string? _serviceState;
    private string _serviceDetail = string.Empty;
    private PlantState _state = new([], [], [], [], []);
    private HashSet<string> _permissions = new(PlantPermissions.All, StringComparer.Ordinal);
    private bool Allows(string permission) => _permissions.Contains(permission);
    public void SetPermissions(IEnumerable<string> permissions)
    {
        _permissions = new(permissions, StringComparer.Ordinal);
        if (!Allows(PlantPermissions.Simulation)) IsRunning = false;
        foreach (var command in new CommunityToolkit.Mvvm.Input.IRelayCommand[] { StepCommand, ToggleRunCommand,
            ToggleFaultCommand, ApplyControlCommand, InspectPackageCommand, ReloadBasketCommand, SetPlcSettingsCommand,
            RetryPlcCommand, ConfirmManualPlcCommand }) command.NotifyCanExecuteChanged();
    }

    public ExtrusionViewModel(ExtrusionSimulator simulator, Func<Task<SimulationResult>> step,
        ILocalizer localizer, IUserMessageService messages, Func<Task>? refreshOrders = null,
        IEquipmentControlClient? equipmentControls = null, IOperatorCommandClient? operatorCommands = null)
    {
        _simulator = simulator;
        _step = step;
        _localizer = localizer;
        _messages = messages;
        _refreshOrders = refreshOrders;
        _equipmentControls = equipmentControls;
        _operatorCommands = operatorCommands;
        _localizer.LanguageChanged += (_, _) =>
        {
            Refresh(_state);
            if (_serviceState is not null) SetServiceStatus(_serviceState, _serviceDetail);
        };
    }

    public ObservableCollection<StationCard> Stations { get; } = [];
    public ObservableCollection<BasketRow> Baskets { get; } = [];
    public ObservableCollection<PlantEvent> Events { get; } = [];
    public ObservableCollection<EquipmentRow> Equipment { get; } = [];
    public ObservableCollection<ControlOption<EquipmentMode>> Modes { get; } = [];
    public ObservableCollection<ControlOption<EquipmentCondition>> Conditions { get; } = [];
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyControlCommand))]
    private EquipmentRow? _selectedEquipment;
    [ObservableProperty] private ControlOption<EquipmentMode>? _selectedMode;
    [ObservableProperty] private ControlOption<EquipmentCondition>? _selectedCondition;
    [ObservableProperty] private string _controlReason = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RunLabel), nameof(CanEditOrders))]
    [NotifyCanExecuteChangedFor(nameof(InspectPackageCommand), nameof(ReloadBasketCommand), nameof(SetPlcSettingsCommand), nameof(RetryPlcCommand), nameof(ConfirmManualPlcCommand))]
    private bool _isRunning;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StepCommand), nameof(ReloadControlsCommand), nameof(ToggleRunCommand), nameof(ToggleFaultCommand), nameof(ApplyControlCommand), nameof(InspectPackageCommand), nameof(ReloadBasketCommand), nameof(SetPlcSettingsCommand), nameof(RetryPlcCommand), nameof(ConfirmManualPlcCommand))]
    [NotifyPropertyChangedFor(nameof(CanEditOrders))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FaultLabel), nameof(EquipmentState))]
    private bool _isFaulted;

    [ObservableProperty] private int _basketCount;
    [ObservableProperty] private int _movingCount;
    [ObservableProperty] private int _heldCount;
    [ObservableProperty] private int _shippedCount;
    [ObservableProperty] private string _lastEvent = string.Empty;
    [ObservableProperty] private string _serviceStatus = string.Empty;
    public void SetServiceStatus(string state, string detail)
    {
        _serviceState = state;
        _serviceDetail = detail;
        ServiceStatus = _localizer[$"Service_{state}"] + (string.IsNullOrEmpty(detail) ? "" : $" · {detail}");
    }
    public void ResetOperatorDrafts()
    {
        _qualityDraftVersion = null; _basketDraftVersion = null;
        InspectionReason = string.Empty; ClearPlcDraft();
    }

    public bool CanEditOrders => !IsRunning && !IsBusy;
    public string RunLabel => _localizer[IsRunning ? "Plant_Pause" : "Plant_Run"];
    public string FaultLabel => _localizer[IsFaulted ? "Plant_Restore" : "Plant_Fault"];
    public string EquipmentState
    {
        get
        {
            var equipment = _state.Equipment.FirstOrDefault(e => e.Id == PlantEquipment.SharedShuttle);
            return equipment is null ? _localizer["Plant_Ready"]
                : $"{_localizer[$"Plant_EquipmentStatus_{equipment.Status}"]} · {_localizer[$"Plant_Mode_{equipment.Mode}"]}";
        }
    }
    private bool CanStep() => !IsBusy && Allows(PlantPermissions.Simulation);
    private bool CanRefresh() => !IsBusy;
    private bool CanFault() => !IsBusy && Allows(PlantPermissions.Equipment);
    private bool CanApplyControl() => !IsBusy && Allows(PlantPermissions.Equipment) && SelectedEquipment is not null;

    partial void OnSelectedEquipmentChanged(EquipmentRow? value)
    {
        if (!_refreshingControls) _controlDraftVersion = null;
        SelectedMode = Modes.FirstOrDefault(m => m.Value == value?.ModeValue);
        SelectedCondition = Conditions.FirstOrDefault(c => c.Value == value?.ConditionValue);
    }
    partial void OnSelectedModeChanged(ControlOption<EquipmentMode>? value)
    {
        if (!_refreshingControls && SelectedEquipment is { } selected && value is not null)
            _controlDraftVersion ??= selected.Version;
    }
    partial void OnSelectedConditionChanged(ControlOption<EquipmentCondition>? value)
    {
        if (!_refreshingControls && SelectedEquipment is { } selected && value is not null)
            _controlDraftVersion ??= selected.Version;
    }
    partial void OnControlReasonChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(value) && SelectedEquipment is { } selected)
            _controlDraftVersion ??= selected.Version;
    }

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task ReloadControlsAsync()
    {
        IsBusy = true;
        try
        {
            _controlDraftVersion = null;
            ControlReason = string.Empty;
            Refresh(await _simulator.GetPlantStateAsync());
        }
        catch (Exception ex) { _messages.ShowError(ex.Message); }
        finally { IsBusy = false; }
    }

    [RelayCommand(CanExecute = nameof(CanApplyControl))]
    private async Task ApplyControlAsync()
    {
        if (SelectedEquipment is not { } selected || SelectedMode is null || SelectedCondition is null) return;
        if (string.IsNullOrWhiteSpace(ControlReason)) { _messages.ShowError(_localizer["Plant_ReasonRequired"]); return; }
        await SaveControlAsync(selected.Id, _controlDraftVersion ?? selected.Version, SelectedMode.Value, SelectedCondition.Value, ControlReason);
    }

    private async Task SaveControlAsync(string id, Guid version, EquipmentMode mode, EquipmentCondition condition, string reason)
    {
        IsBusy = true; IsRunning = false;
        try
        {
            await (_equipmentControls ?? throw new InvalidOperationException(_localizer["ControlApi_Unavailable"]))
                .SetControlAsync(id, version, mode, condition, reason);
            ControlReason = string.Empty;
            _controlDraftVersion = null;
            Refresh(await _simulator.GetPlantStateAsync());
        }
        catch (Exception ex) { _messages.ShowError(ex.Message); }
        finally { IsBusy = false; }
    }

    [RelayCommand(CanExecute = nameof(CanStep))]
    private void ToggleRun() => IsRunning = !IsRunning;

    [RelayCommand(CanExecute = nameof(CanFault))]
    private async Task ToggleFaultAsync()
    {
        var shuttle = _state.Equipment.FirstOrDefault(e => e.Id == PlantEquipment.SharedShuttle);
        if (shuttle is null) return;
        await SaveControlAsync(shuttle.Id, shuttle.Version, shuttle.Mode,
            !shuttle.CanOperate ? EquipmentCondition.Operational : EquipmentCondition.Fault,
            _localizer[!shuttle.CanOperate ? "Plant_Restore" : "Plant_Fault"]);
    }

    [RelayCommand(CanExecute = nameof(CanStep))]
    private async Task StepAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            var result = await _step();
            LastEvent = _localizer.Format($"Plant_Result_{result.Action}", result.Reference ?? string.Empty);
            if (result.Action is SimulationAction.Waiting or SimulationAction.CentralService or SimulationAction.Faulted or SimulationAction.PlcTimedOut
                or SimulationAction.PlcRejected or SimulationAction.PlcOffline or SimulationAction.PlcDeferred) IsRunning = false;
        }
        catch (Exception ex)
        {
            IsRunning = false;
            LastEvent = _localizer["Plant_Error"];
            _messages.ShowError($"{LastEvent}\n{ex.GetBaseException().Message}");
        }
        finally { IsBusy = false; }
    }

    public void Refresh(PlantState plant)
    {
        _state = plant;
        IsFaulted = plant.Equipment.Any(e => e.Id == PlantEquipment.SharedShuttle && !e.CanOperate);
        var selectedId = SelectedEquipment?.Id;
        var draftMode = SelectedMode?.Value;
        var draftCondition = SelectedCondition?.Value;
        _refreshingControls = true;
        Modes.Clear(); Conditions.Clear();
        foreach (var mode in Enum.GetValues<EquipmentMode>()) Modes.Add(new(mode, _localizer[$"Plant_Mode_{mode}"]));
        foreach (var condition in Enum.GetValues<EquipmentCondition>()) Conditions.Add(new(condition, _localizer[$"Plant_Condition_{condition}"]));
        Equipment.Clear();
        foreach (var equipment in plant.Equipment.OrderBy(e => e.Id))
            Equipment.Add(new(equipment.Id, _localizer[$"Plant_EquipmentKind_{equipment.Kind}"],
                _localizer[$"Plant_EquipmentStatus_{equipment.Status}"], _localizer[$"Plant_Mode_{equipment.Mode}"],
                equipment.ActiveOperation is null ? "—" : equipment.Kind == EquipmentKind.Oven ? _localizer["Plant_OvenCycle"] : equipment.ActiveOperation,
                equipment.Version, equipment.Mode, equipment.Condition));
        SelectedEquipment = Equipment.FirstOrDefault(e => e.Id == selectedId) ?? Equipment.FirstOrDefault();
        if (_controlDraftVersion is not null && SelectedEquipment?.Id == selectedId)
        {
            SelectedMode = Modes.FirstOrDefault(m => m.Value == draftMode);
            SelectedCondition = Conditions.FirstOrDefault(c => c.Value == draftCondition);
        }
        else
        {
            SelectedMode = Modes.FirstOrDefault(m => m.Value == SelectedEquipment?.ModeValue);
            SelectedCondition = Conditions.FirstOrDefault(c => c.Value == SelectedEquipment?.ConditionValue);
        }
        _refreshingControls = false;
        var positions = plant.Locate();
        Stations.Clear();
        foreach (var station in plant.Locations.OrderBy(s => s.Kind).ThenBy(s => s.Id))
        {
            var occupied = plant.Occupied(station.Id);
            var reserved = plant.Reserved(station.Id);
            var contents = positions.Where(b => !b.IsMoving && b.Location == station.Id).Select(b => b.BasketNumber).ToList();
            contents.AddRange(plant.Packages.Where(p => p.LocationId == station.Id && p.Status >= PackageStatus.Ready).Select(p => p.Number));
            var equipment = plant.Equipment.FirstOrDefault(e => e.LocationId == station.Id);
            var cycle = plant.Cycles.Where(c => c.EquipmentId == equipment?.Id).OrderByDescending(c => c.StartedAt).FirstOrDefault();
            var hasCycle = cycle is not null && cycle.Load.Any(l => positions.Any(b => b.BasketNumber == l.BasketNumber && b.Basket.ContentSequence == l.ContentSequence && b.Location == station.Id));
            Stations.Add(new(station.Id, _localizer[$"Plant_Kind_{station.Kind}"],
                _localizer.Format("Plant_Capacity", occupied, station.Capacity, reserved),
                contents.Count == 0 ? _localizer["Plant_Empty"] : string.Join("\n", contents),
                occupied + reserved >= station.Capacity, reserved > 0,
                equipment is null ? "" : $"{_localizer[$"Plant_EquipmentStatus_{equipment.Status}"]} · {_localizer[$"Plant_Mode_{equipment.Mode}"]}",
                hasCycle ? $"{cycle!.RecipeId} · r{cycle.RecipeRevision}\n{_localizer[$"Plant_Phase_{cycle.Phase}"]}\n{cycle.SimulatedTemperature:0} °C / {cycle.TargetTemperature:0} °C\n{cycle.ElapsedMinutes}/{cycle.TotalMinutes} min" : "",
                hasCycle ? cycle!.ElapsedMinutes * 100d / cycle.TotalMinutes : 0));
        }
        Baskets.Clear();
        foreach (var basket in positions)
        {
            var order = basket.Order;
            var state = basket.IsMoving ? OrderRowViewModel.StatusName(_localizer, OrderStatus.InTransit)
                : order is null ? _localizer["Plant_Ready"] : order.Status == OrderStatus.Done
                ? _localizer[basket.IsShipped ? "Plant_Shipped" : "Plant_Processing"]
                : OrderRowViewModel.StatusName(_localizer, order.Status);
            var oven = plant.Equipment.FirstOrDefault(e => e.Kind == EquipmentKind.Oven && e.LocationId == basket.Location);
            if (oven is not null && order?.IsOpen != true)
            {
                var cycle = plant.Cycles.FirstOrDefault(c => c.EquipmentId == oven.Id && c.Load.Any(l => l.BasketNumber == basket.BasketNumber && l.ContentSequence == basket.Basket.ContentSequence));
                state = cycle is null ? _localizer["Plant_AwaitingCycle"] : _localizer[$"Plant_Phase_{cycle.Phase}"];
            }
            if (basket.Basket.IsEmpty) state = _localizer[basket.IsMoving ? "Plant_EmptyReturning" : "Plant_EmptyBasket"];
            Baskets.Add(new(basket.BasketNumber, basket.Basket.IsEmpty ? "—" : basket.MaterialLot.BatchNumber,
                basket.Basket.IsEmpty ? _localizer["Plant_EmptyBasket"] : basket.MaterialLot.Material, basket.Basket.Quantity,
                basket.IsMoving ? $"→ {basket.Basket.ReservedLocationId}" : basket.Location ?? "—",
                basket.Basket.ReservedLocationId ?? (order?.IsOpen == true ? order.TargetLocation : "—"),
                state, order?.OrderNumber ?? "—", basket.IsMoving, order?.Status == OrderStatus.OnHold));
        }
        Events.Clear();
        foreach (var entry in plant.Equipment.SelectMany(e => e.History.Select(h => (Equipment: e, Entry: h)))
            .OrderByDescending(e => e.Entry.At).Take(8))
            Events.Add(new(entry.Entry.At.ToLocalTime().ToString("HH:mm:ss"), entry.Equipment.Id, entry.Entry.Description, entry.Entry.By));
        foreach (var entry in plant.Orders.SelectMany(o => o.History.Select(h => (Order: o, Change: h)))
                     .OrderByDescending(e => e.Change.ChangedAt).ThenByDescending(e => e.Order.OrderNumber).Take(25))
        {
            Events.Add(new(entry.Change.ChangedAt.ToLocalTime().ToString("HH:mm:ss"), entry.Order.PieceNumber,
                $"{OrderRowViewModel.StatusName(_localizer, entry.Change.ToStatus)} · {entry.Order.SourceLocation} → {entry.Order.TargetLocation}",
                entry.Change.ChangedBy));
        }
        BasketCount = positions.Count;
        MovingCount = positions.Count(b => b.IsMoving) + plant.Packages.Count(p => p.Status == PackageStatus.InTransit);
        HeldCount = positions.Count(b => b.Order?.Status == OrderStatus.OnHold);
        ShippedCount = plant.Packages.Count(p => p.Status == PackageStatus.Shipped);
        RefreshPacking(plant);
        RefreshSchedule(plant);
        RefreshPlc(plant);
        OnPropertyChanged(nameof(EquipmentState));
        OnPropertyChanged(nameof(RunLabel));
        OnPropertyChanged(nameof(FaultLabel));
    }
}
