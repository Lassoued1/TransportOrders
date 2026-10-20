using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using TransportOrders.Core;

namespace TransportOrders.App.ViewModels;

public sealed record ScheduleRow(int Position, string Action, string Reference, string Equipment, string Route,
    string Recipe, string Baskets, string Priority, string Due, string Reason);
public sealed record ScheduleBlockRow(string Reference, string Equipment, string Reason);

public sealed partial class ExtrusionViewModel
{
    public ObservableCollection<ScheduleRow> ScheduledActions { get; } = [];
    public ObservableCollection<ScheduleBlockRow> ScheduleBlocks { get; } = [];
    [ObservableProperty] private string _scheduleNext = string.Empty;
    private void RefreshSchedule(PlantState plant)
    {
        var schedule = _simulator.GetSchedule(plant);
        ScheduledActions.Clear(); ScheduleBlocks.Clear();
        foreach (var action in schedule.Actions)
            ScheduledActions.Add(new(ScheduledActions.Count + 1, _localizer[$"Schedule_Action_{action.Kind}"],
                action.Kind == ScheduledActionKind.AdvanceOvenCycle ? action.Resource : action.Reference,
                action.Resource, action.Source is null && action.Target is null ? "—" : $"{action.Source ?? "—"} → {action.Target ?? "—"}",
                action.RecipeId ?? "—", string.Join(", ", action.BasketNumbers), _localizer[action.Urgent ? "Priority_Urgent" : "Priority_Normal"],
                action.DueAt?.ToLocalTime().ToString("dd.MM HH:mm") ?? "—", _localizer[$"Schedule_Reason_{action.Reason}"]));
        foreach (var block in schedule.Blocked)
            ScheduleBlocks.Add(new(block.Reference, block.Resource, _localizer[$"Schedule_Reason_{block.Reason}"]));
        ScheduleNext = ScheduledActions.FirstOrDefault() is { } next
            ? $"{next.Action} · {next.Reference} · {next.Reason}" : _localizer["Schedule_NoAction"];
    }
}
