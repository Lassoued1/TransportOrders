using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TransportOrders.Core;

namespace TransportOrders.App.ViewModels;

public sealed partial class ExtrusionViewModel
{
    private ClientRuntimeState? _runtime;
    private Guid? _runtimeDraftVersion;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ToggleCentralRuntimeCommand))]
    private string _runtimeReason = "";

    public string CentralRuntimeLabel => _localizer[_runtime?.Paused == true ? "Runtime_Resume" : "Runtime_Pause"];

    public void SetRuntimeState(ClientRuntimeState? runtime)
    {
        _runtime = runtime;
        if (runtime is { Enabled: true, Running: true } || runtime?.Paused == true) IsRunning = false;
        OnPropertyChanged(nameof(CentralRuntimeLabel));
        ToggleCentralRuntimeCommand.NotifyCanExecuteChanged();
        StepCommand.NotifyCanExecuteChanged();
        ToggleRunCommand.NotifyCanExecuteChanged();
    }

    partial void OnRuntimeReasonChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) _runtimeDraftVersion = null;
        else _runtimeDraftVersion ??= _runtime?.Version;
    }

    private bool CanToggleCentralRuntime() => !IsBusy && _operatorCommands is not null
        && Allows(PlantPermissions.Runtime) && _runtime is { Enabled: true, Running: true }
        && _runtime.Version != Guid.Empty && !string.IsNullOrWhiteSpace(RuntimeReason);

    [RelayCommand(CanExecute = nameof(CanToggleCentralRuntime))]
    private async Task ToggleCentralRuntimeAsync()
    {
        if (_runtime is null) return;
        IsBusy = true;
        IsRunning = false;
        try
        {
            await Commands.SetRuntimeAsync(_runtimeDraftVersion ?? _runtime.Version, !_runtime.Paused, RuntimeReason);
            RuntimeReason = "";
            if (_refreshOrders is not null) await _refreshOrders();
        }
        catch (Exception ex) { _messages.ShowError(ex.Message); }
        finally { IsBusy = false; }
    }
}
