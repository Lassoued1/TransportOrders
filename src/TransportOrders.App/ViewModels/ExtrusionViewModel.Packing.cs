using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TransportOrders.Core;

namespace TransportOrders.App.ViewModels;

public sealed record PackageRow(string Number, string Basket, int Load, string Lot, string Material, int Quantity,
    string Position, string Status, string Label, Guid Version, PackageStatus StatusValue);
public sealed record EmptyBasketRow(string Number, string Location, int Load, Guid Version);

public sealed partial class ExtrusionViewModel
{
    private bool _refreshingPacking;
    private Guid? _qualityDraftVersion;
    private Guid? _basketDraftVersion;
    partial void OnSelectedPackageChanged(PackageRow? value)
    { if (!_refreshingPacking) _qualityDraftVersion = null; }
    partial void OnSelectedEmptyBasketChanged(EmptyBasketRow? value)
    { if (!_refreshingPacking) _basketDraftVersion = null; }
    partial void OnVisualPassedChanged(bool value) => CaptureQualityDraft();
    partial void OnQuantityVerifiedChanged(bool value) => CaptureQualityDraft();
    partial void OnInspectionReasonChanged(string value)
    { if (!string.IsNullOrWhiteSpace(value)) CaptureQualityDraft(); }
    private void CaptureQualityDraft()
    { if (!_refreshingPacking && SelectedPackage is { } package) _qualityDraftVersion ??= package.Version; }
    partial void OnReloadLotChanged(MaterialLot? value) => CaptureBasketDraft();
    partial void OnReloadOvenChanged(string? value) => CaptureBasketDraft();
    partial void OnReloadQuantityChanged(int value) => CaptureBasketDraft();
    partial void OnReloadWeightChanged(decimal value) => CaptureBasketDraft();
    partial void OnReloadLengthChanged(decimal value) => CaptureBasketDraft();
    private void CaptureBasketDraft()
    { if (!_refreshingPacking && SelectedEmptyBasket is { } basket) _basketDraftVersion ??= basket.Version; }
    public ObservableCollection<PackageRow> Packages { get; } = [];
    public ObservableCollection<EmptyBasketRow> EmptyBaskets { get; } = [];
    public ObservableCollection<MaterialLot> PackingLots { get; } = [];
    public ObservableCollection<string> ReloadOvens { get; } = [];
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InspectPackageCommand))]
    private PackageRow? _selectedPackage;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ReloadBasketCommand))]
    private EmptyBasketRow? _selectedEmptyBasket;
    [ObservableProperty] private MaterialLot? _reloadLot;
    [ObservableProperty] private string? _reloadOven;
    [ObservableProperty] private int _reloadQuantity = 10;
    [ObservableProperty] private decimal _reloadWeight = 1m;
    [ObservableProperty] private decimal _reloadLength = 6.4m;
    [ObservableProperty] private bool _visualPassed = true;
    [ObservableProperty] private bool _quantityVerified = true;
    [ObservableProperty] private string _inspectionReason = string.Empty;

    private bool CanInspectPackage() => !IsBusy && !IsRunning && Allows(PlantPermissions.Quality)
        && SelectedPackage?.StatusValue is PackageStatus.AwaitingInspection or PackageStatus.Rejected;
    private bool CanReloadBasket() => !IsBusy && !IsRunning && Allows(PlantPermissions.Basket) && SelectedEmptyBasket is not null;
    [RelayCommand(CanExecute = nameof(CanInspectPackage))]
    private async Task InspectPackageAsync()
    {
        if (SelectedPackage is not { } package) return;
        if (string.IsNullOrWhiteSpace(InspectionReason)) { _messages.ShowError(_localizer["Plant_ReasonRequired"]); return; }
        await SavePackingActionAsync(() => Commands.InspectAsync(package.Number, _qualityDraftVersion ?? package.Version, VisualPassed,
            QuantityVerified, InspectionReason), () => { _qualityDraftVersion = null; InspectionReason = string.Empty; });
    }
    [RelayCommand(CanExecute = nameof(CanReloadBasket))]
    private async Task ReloadBasketAsync()
    {
        if (SelectedEmptyBasket is not { } basket || ReloadLot is null || ReloadOven is null) return;
        await SavePackingActionAsync(() => Commands.ReloadBasketAsync(basket.Number, _basketDraftVersion ?? basket.Version, ReloadLot.Number,
            ReloadQuantity, ReloadWeight, ReloadLength, ReloadOven), () => _basketDraftVersion = null);
    }
    private async Task SavePackingActionAsync(Func<Task> action, Action? onSuccess = null)
    {
        IsBusy = true; IsRunning = false;
        try
        {
            await action();
            onSuccess?.Invoke();
            if (_refreshOrders is not null) await _refreshOrders();
            else Refresh(await _simulator.GetPlantStateAsync());
        }
        catch (Exception ex) { _messages.ShowError(ex.Message); }
        finally { IsBusy = false; }
    }
    private void RefreshPacking(PlantState plant)
    {
        var packageNumber = SelectedPackage?.Number;
        var basketNumber = SelectedEmptyBasket?.Number;
        var lotNumber = ReloadLot?.Number;
        var oven = ReloadOven;
        _refreshingPacking = true;
        Packages.Clear(); EmptyBaskets.Clear(); PackingLots.Clear(); ReloadOvens.Clear();
        foreach (var package in plant.Packages.OrderByDescending(p => p.CreatedAt).ThenByDescending(p => p.Number))
            Packages.Add(new(package.Number, package.SourceBasketNumber, package.ContentSequence, package.MaterialLotNumber,
                package.Material, package.Quantity, package.LocationId ?? $"→ {package.ReservedLocationId}",
                _localizer[$"Plant_PackageStatus_{package.Status}"], package.LabelText ?? _localizer["Plant_NoLabel"], package.Version, package.Status));
        foreach (var basket in plant.Baskets.Where(b => b.IsEmpty && !b.IsMoving
            && plant.Locations.Any(l => l.Id == b.LocationId && l.Kind == StationKind.Press)))
            EmptyBaskets.Add(new(basket.Number, basket.LocationId!, basket.ContentSequence, basket.Version));
        foreach (var lot in plant.MaterialLots.Where(l => plant.Recipes.Any(r => r.Supports(l))).OrderBy(l => l.Number)) PackingLots.Add(lot);
        foreach (var location in plant.Locations.Where(l => l.Kind == StationKind.AgingOven).OrderBy(l => l.Id)) ReloadOvens.Add(location.Id);
        SelectedPackage = Packages.FirstOrDefault(p => p.Number == packageNumber) ?? Packages.FirstOrDefault();
        SelectedEmptyBasket = EmptyBaskets.FirstOrDefault(b => b.Number == basketNumber) ?? EmptyBaskets.FirstOrDefault();
        if (SelectedPackage?.Number != packageNumber) _qualityDraftVersion = null;
        if (SelectedEmptyBasket?.Number != basketNumber) _basketDraftVersion = null;
        ReloadLot = PackingLots.FirstOrDefault(l => l.Number == lotNumber) ?? PackingLots.FirstOrDefault();
        ReloadOven = ReloadOvens.FirstOrDefault(o => o == oven) ?? ReloadOvens.FirstOrDefault();
        _refreshingPacking = false;
    }
}
