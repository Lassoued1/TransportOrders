using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TransportOrders.Core;

namespace TransportOrders.App.ViewModels;

public sealed record PlcRow(Guid Id, string Operation, string Kind, string Status, string Route, string Attempts,
    string Deadline, string Detail, Guid Version, PlcCommandStatus StatusValue, int AttemptCount);

public sealed partial class ExtrusionViewModel
{
    private bool _refreshingPlc;
    private Guid? _plcDraftCommandVersion;
    private Guid? _plcDraftSettingsVersion;
    partial void OnSelectedPlcCommandChanged(PlcRow? value)
    { if (!_refreshingPlc) _plcDraftCommandVersion = null; }
    partial void OnPlcConnectedChanged(bool value) => CapturePlcDraft();
    partial void OnPlcSuppressRepliesChanged(bool value) => CapturePlcDraft();
    partial void OnPlcReasonChanged(string value)
    { if (!string.IsNullOrWhiteSpace(value)) CapturePlcDraft(); }
    private void CapturePlcDraft()
    {
        if (_refreshingPlc) return;
        if (SelectedPlcCommand is { } command) _plcDraftCommandVersion ??= command.Version;
        if (_state.PlcSimulation is { } settings) _plcDraftSettingsVersion ??= settings.Version;
    }
    private void ClearPlcDraft()
    { _plcDraftCommandVersion = null; _plcDraftSettingsVersion = null; PlcReason = string.Empty; }
    public ObservableCollection<PlcRow> PlcCommands { get; } = [];
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RetryPlcCommand))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmManualPlcCommand))]
    private PlcRow? _selectedPlcCommand;
    [ObservableProperty] private bool _plcConnected = true;
    [ObservableProperty] private bool _plcSuppressReplies;
    [ObservableProperty] private string _plcReason = string.Empty;
    [ObservableProperty] private string _plcChannelState = string.Empty;
    private bool CanSetPlcSettings() => !IsBusy && !IsRunning && Allows(PlantPermissions.Plc) && _state.PlcSimulation is not null;
    private bool CanRetryPlc() => CanSetPlcSettings() && SelectedPlcCommand is { StatusValue: PlcCommandStatus.TimedOut, AttemptCount: < PlcCommand.MaxAttempts };
    private bool CanConfirmManualPlc() => CanSetPlcSettings() && SelectedPlcCommand is { StatusValue: not PlcCommandStatus.Applied };
    [RelayCommand(CanExecute = nameof(CanConfirmManualPlc))]
    private async Task ConfirmManualPlcAsync()
    {
        if (SelectedPlcCommand is not { } command) return;
        if (string.IsNullOrWhiteSpace(PlcReason)) { _messages.ShowError(_localizer["Plant_ReasonRequired"]); return; }
        if (!_messages.Confirm(_localizer.Format("Plc_ManualPrompt", command.Operation, command.Route))) return;
        await SavePackingActionAsync(() => Commands.ConfirmPlcAsync(command.Id, _plcDraftCommandVersion ?? command.Version, PlcReason), ClearPlcDraft);
    }
    [RelayCommand(CanExecute = nameof(CanSetPlcSettings))]
    private async Task SetPlcSettingsAsync()
    {
        if (_state.PlcSimulation is not { } settings) return;
        if (string.IsNullOrWhiteSpace(PlcReason)) { _messages.ShowError(_localizer["Plant_ReasonRequired"]); return; }
        await SavePackingActionAsync(() => Commands.SetPlcSettingsAsync(_plcDraftSettingsVersion ?? settings.Version, PlcConnected, PlcSuppressReplies, PlcReason), ClearPlcDraft);
    }
    [RelayCommand(CanExecute = nameof(CanRetryPlc))]
    private async Task RetryPlcAsync()
    {
        if (SelectedPlcCommand is not { } command) return;
        if (string.IsNullOrWhiteSpace(PlcReason)) { _messages.ShowError(_localizer["Plant_ReasonRequired"]); return; }
        await SavePackingActionAsync(() => Commands.RetryPlcAsync(command.Id, _plcDraftCommandVersion ?? command.Version, PlcReason), ClearPlcDraft);
    }
    private void RefreshPlc(PlantState plant)
    {
        var id = SelectedPlcCommand?.Id;
        _refreshingPlc = true;
        PlcCommands.Clear();
        foreach (var command in plant.PlcCommands.OrderByDescending(c => c.CreatedAt).ThenBy(c => c.Id))
            PlcCommands.Add(new(command.Id, command.Operation, _localizer[$"Plc_Kind_{command.Kind}"], _localizer[$"Plc_Status_{command.Status}"],
                $"{command.Source} → {command.Target}", $"{command.Attempts}/{PlcCommand.MaxAttempts}",
                command.DeadlineAt?.ToLocalTime().ToString("HH:mm:ss") ?? "—",
                command.LastError ?? command.History.OrderByDescending(h => h.Id).FirstOrDefault()?.Detail ?? "—",
                command.Version, command.Status, command.Attempts));
        SelectedPlcCommand = PlcCommands.FirstOrDefault(c => c.Id == id) ?? PlcCommands.FirstOrDefault();
        if (SelectedPlcCommand?.Id != id) _plcDraftCommandVersion = null;
        if (_plcDraftSettingsVersion is null)
        {
            PlcConnected = plant.PlcSimulation?.Connected ?? false;
            PlcSuppressReplies = plant.PlcSimulation?.SuppressAcknowledgements ?? false;
        }
        PlcChannelState = _localizer[plant.PlcSimulation is null ? "Plc_NotConfigured" : plant.PlcSimulation.Connected
            ? plant.PlcSimulation.SuppressAcknowledgements ? "Plc_RepliesSuppressed" : "Plc_Connected" : "Plc_Offline"];
        _refreshingPlc = false;
        SetPlcSettingsCommand.NotifyCanExecuteChanged(); RetryPlcCommand.NotifyCanExecuteChanged();
        ConfirmManualPlcCommand.NotifyCanExecuteChanged();
    }
}
