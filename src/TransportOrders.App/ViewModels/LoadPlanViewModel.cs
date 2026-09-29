using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TransportOrders.App.Services;
using TransportOrders.Core;

namespace TransportOrders.App.ViewModels;

/// <summary>
/// The "Plan loads" dialog: the planner's proposals, each dispatchable on its own. A part load can only be
/// dispatched once a reason is chosen; after each dispatch the plan is recalculated from the saved state.
/// </summary>
public sealed partial class LoadPlanViewModel : ObservableObject
{
    private readonly Func<LoadPlan> _plan;
    private readonly Func<LoadProposal, PartLoadReason?, Task<string?>> _dispatch;
    private readonly ILocalizer _localizer;

    /// <param name="plan">Calculates the current plan from the orders as they are now.</param>
    /// <param name="dispatch">Dispatches a proposal and returns the transport number, or null if it failed.</param>
    public LoadPlanViewModel(Func<LoadPlan> plan, Func<LoadProposal, PartLoadReason?, Task<string?>> dispatch, ILocalizer localizer)
    {
        _plan = plan;
        _dispatch = dispatch;
        _localizer = localizer;
        Reasons = Enum.GetNames<PartLoadReason>().Select(r => new FilterOption(r, localizer[$"Reason_{r}"])).ToList();
        ThresholdText = localizer.Format("Plan_Threshold", Transport.FullLoadThreshold);
        Refresh();
    }

    public ObservableCollection<LoadProposalViewModel> Proposals { get; } = [];

    public IReadOnlyList<FilterOption> Reasons { get; }

    public string ThresholdText { get; }

    public bool IsEmpty => Proposals.Count == 0;

    [ObservableProperty]
    private string _message = string.Empty;

    [ObservableProperty]
    private string _unplannableText = string.Empty;

    internal async Task DispatchAsync(LoadProposalViewModel proposal)
    {
        var number = await _dispatch(proposal.Proposal, proposal.Reason);
        if (number is null)
        {
            return;
        }

        Message = _localizer.Format("Plan_Dispatched", number, proposal.Proposal.Orders.Count, proposal.Proposal.FillRate);
        Refresh();
    }

    private void Refresh()
    {
        var plan = _plan();
        Proposals.Clear();
        foreach (var proposal in plan.Proposals)
        {
            Proposals.Add(new LoadProposalViewModel(proposal, this, _localizer));
        }

        UnplannableText = plan.Unplannable.Count == 0
            ? string.Empty
            : _localizer.Format("Plan_Unplannable", string.Join(", ", plan.Unplannable.Select(o => o.OrderNumber)));
        OnPropertyChanged(nameof(IsEmpty));
    }
}

public sealed partial class LoadProposalViewModel : ObservableObject
{
    private readonly LoadPlanViewModel _owner;

    public LoadProposalViewModel(LoadProposal proposal, LoadPlanViewModel owner, ILocalizer localizer)
    {
        Proposal = proposal;
        _owner = owner;
        VehicleText = localizer[$"Vehicle_{proposal.Vehicle.Type}"];
        RouteText = $"{proposal.SourceLocation} → {proposal.TargetLocation}";
        LengthText = localizer.Format("Plan_Length", proposal.LoadedLength, proposal.Vehicle.LoadingLength);
        WeightText = localizer.Format("Plan_Weight", proposal.LoadedWeight, proposal.Vehicle.Payload);
        FillText = localizer.Format("Kpi_Percent", proposal.FillRate);
        StateText = localizer[proposal.IsFull ? "Plan_Full" : "Plan_PartLoad"];
        OrderLines = proposal.Orders
            .OrderBy(o => o.OrderNumber)
            .Select(o => localizer.Format("Plan_OrderLine", o.OrderNumber, o.PieceNumber, o.LoadingLength, o.Weight))
            .ToList();
        _selectedReason = proposal.SuggestedReason?.ToString();
    }

    public LoadProposal Proposal { get; }

    public string VehicleText { get; }

    public string RouteText { get; }

    public string LengthText { get; }

    public string WeightText { get; }

    public string FillText { get; }

    public string StateText { get; }

    public double FillPercent => (double)(Proposal.FillRate * 100);

    public bool IsFull => Proposal.IsFull;

    public bool IsPartLoad => !Proposal.IsFull;

    public IReadOnlyList<string> OrderLines { get; }

    public IReadOnlyList<FilterOption> Reasons => _owner.Reasons;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DispatchCommand))]
    private string? _selectedReason;

    public PartLoadReason? Reason => IsFull || SelectedReason is null ? null : Enum.Parse<PartLoadReason>(SelectedReason);

    [RelayCommand(CanExecute = nameof(CanDispatch))]
    private Task DispatchAsync() => _owner.DispatchAsync(this);

    private bool CanDispatch() => IsFull || SelectedReason is not null;
}
