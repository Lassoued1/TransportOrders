using System.Collections.ObjectModel;
using System.Net;
using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TransportOrders.App.Services;
using TransportOrders.Core;

namespace TransportOrders.App.ViewModels;

public sealed record AlarmRow(PlantAlarmSnapshot Alarm, string Message, string State, string Raised, string Acknowledgement);
public sealed record AlarmHistoryRow(string Time, string Actor, string Action, string Note);
public sealed partial class AlarmViewModel : ObservableObject
{
    private readonly PlantConnection _connection;
    private readonly ILocalizer _localizer;
    private readonly IUserMessageService _messages;
    private readonly Func<Task> _refresh;
    private PlantClientSnapshot? _snapshot;
    private Guid? _draftId;
    private Guid? _draftVersion;
    private bool _refreshing;
    public AlarmViewModel(PlantConnection connection, ILocalizer localizer, IUserMessageService messages, Func<Task> refresh)
    {
        _connection = connection; _localizer = localizer; _messages = messages; _refresh = refresh;
        localizer.LanguageChanged += (_, _) => Refresh(_snapshot);
    }
    public ObservableCollection<AlarmRow> Alarms { get; } = [];
    public ObservableCollection<AlarmHistoryRow> History { get; } = [];
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(AcknowledgeCommand))] private AlarmRow? _selectedAlarm;
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(AcknowledgeCommand))] private string _reason = "";
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(AcknowledgeCommand))] private bool _isBusy;
    [ObservableProperty] private string _summary = "";
    public void Refresh(PlantClientSnapshot? snapshot)
    {
        _snapshot = snapshot;
        var items = snapshot?.Alarms ?? Alarms.Select(a => a.Alarm).ToArray();
        {
            var selected = SelectedAlarm?.Alarm.Id;
            _refreshing = true;
            Alarms.Clear();
            foreach (var alarm in items)
                Alarms.Add(new(alarm, _localizer[$"Alarm_{alarm.Code}"], _localizer[alarm.IsActive ? "Alarm_Active" : "Alarm_Cleared"],
                    alarm.RaisedUtc.ToLocalTime().ToString("g"), alarm.AcknowledgedUtc is null ? _localizer["Alarm_Unacknowledged"]
                        : $"{alarm.AcknowledgedBy} · {alarm.AcknowledgedUtc.Value.ToLocalTime():g}"));
            SelectedAlarm = Alarms.FirstOrDefault(a => a.Alarm.Id == selected) ?? Alarms.FirstOrDefault();
            _refreshing = false;
            if (SelectedAlarm?.Alarm.Id != _draftId)
            { _draftId = SelectedAlarm?.Alarm.Id; _draftVersion = SelectedAlarm?.Alarm.Version; Reason = ""; }
        }
        Summary = snapshot is null ? _localizer["Alarm_Offline"] : snapshot.Alarms is null ? _localizer["Alarm_Unavailable"] : _localizer.Format("Alarm_Summary",
            Alarms.Count(a => a.Alarm.IsActive), Alarms.Count(a => a.Alarm.AcknowledgedUtc is null));
        AcknowledgeCommand.NotifyCanExecuteChanged();
    }
    partial void OnSelectedAlarmChanged(AlarmRow? value)
    {
        if (!_refreshing && value?.Alarm.Id != _draftId) { _draftId = value?.Alarm.Id; _draftVersion = value?.Alarm.Version; Reason = ""; }
        History.Clear();
        foreach (var e in value?.Alarm.History ?? []) History.Add(new(e.AtUtc.ToLocalTime().ToString("g"), e.Actor,
            _localizer[$"Alarm_Event_{e.Action}"], e.Note));
    }
    [RelayCommand]
    private void RefreshSelection() { _draftVersion = SelectedAlarm?.Alarm.Version; AcknowledgeCommand.NotifyCanExecuteChanged(); }
    private bool CanAcknowledge() => !IsBusy && _snapshot?.Alarms is not null && _snapshot.Access.Permissions.Contains(PlantPermissions.Equipment)
        && SelectedAlarm?.Alarm.AcknowledgedUtc is null && SelectedAlarm is not null && _draftVersion.HasValue
        && !string.IsNullOrWhiteSpace(Reason) && Reason.Length <= 1000;
    [RelayCommand(CanExecute = nameof(CanAcknowledge))]
    private async Task AcknowledgeAsync()
    {
        if (!CanAcknowledge()) return;
        IsBusy = true;
        try { await _connection.AcknowledgeAlarmAsync(SelectedAlarm!.Alarm.Id, _draftVersion!.Value, Reason); }
        catch (HttpRequestException ex) { _messages.ShowError(_localizer[ex.StatusCode == HttpStatusCode.Conflict
            ? "OperatorApi_Conflict" : ex.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized
                ? "OperatorApi_Unauthorized" : "OperatorApi_Unavailable"]); }
        catch (Exception ex) when (ex is TaskCanceledException or InvalidOperationException)
        { _messages.ShowError(_localizer["OperatorApi_Unavailable"]); }
        finally { await _refresh(); IsBusy = false; }
    }
}
