using TransportOrders.App.Services;
using TransportOrders.App.ViewModels;
using TransportOrders.Core;

namespace TransportOrders.App.Tests;

/// <summary>
/// In-memory stand-in for the database: every load builds new order objects from the "stored" history,
/// so a change that was not saved really disappears on reload, as with SQLite.
/// </summary>
internal sealed class FakeOrderRepository(DateTime createdAt) : IOrderRepository
{
    private readonly List<Spec> _specs = [];
    private readonly Dictionary<string, List<StatusChange>> _stored = [];
    private readonly Dictionary<string, Transport> _transports = [];
    private readonly PlantEquipment _shuttle = new(PlantEquipment.SharedShuttle, EquipmentKind.Shuttle, null, createdAt);

    public Task StartPackingAsync(string basketNumber, Guid basketVersion, string user, DateTime now, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task CompletePackingAsync(string packageNumber, Guid version, string user, DateTime now, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task InspectPackageAsync(string packageNumber, Guid version, bool visualPassed, bool quantityVerified, string user, string reason, DateTime now, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task StartPackageShippingAsync(string packageNumber, Guid version, string user, DateTime now, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task CompletePackageShippingAsync(string packageNumber, Guid version, string user, DateTime now, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task StartEmptyReturnAsync(string basketNumber, Guid version, string target, string user, DateTime now, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task CompleteEmptyReturnAsync(Guid returnId, Guid version, string user, DateTime now, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task ReloadBasketAsync(string basketNumber, Guid version, string lotNumber, int quantity, decimal weight, decimal loadingLength, string targetOven, string user, DateTime now, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task SetEquipmentControlAsync(string equipmentId, Guid expectedVersion, EquipmentMode mode,
        EquipmentCondition condition, string user, string reason, DateTime now, CancellationToken cancellationToken = default)
    {
        ThrowIfFailing();
        _shuttle.SetControl(mode, condition, user, reason, now);
        return Task.CompletedTask;
    }
    public Task StartOvenCycleAsync(string equipmentId, string recipeId, IReadOnlyCollection<string> basketNumbers,
        string user, DateTime now, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task AdvanceOvenCycleAsync(Guid cycleId, Guid expectedVersion, int minutes, string user, DateTime now,
        CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public int SaveCount { get; private set; }

    public Exception? FailNextSave { get; set; }

    public IReadOnlyCollection<Transport> Transports => _transports.Values;

    public async Task<PlantState> GetPlantStateAsync(CancellationToken cancellationToken = default)
    {
        var orders = await GetAllAsync(cancellationToken);
        var latest = orders.GroupBy(o => o.PieceNumber).Select(g => g.Last()).ToList();
        var lots = latest.GroupBy(o => o.ParentPieceNumber ?? o.HeatNumber)
            .Select(g => new MaterialLot(g.Key, g.First().HeatNumber, g.First().Material)).ToList();
        var locations = ExtrusionPlant.Stations.Select(s => new PlantLocation(s.Id, s.Kind, s.Capacity)).ToList();
        foreach (var id in orders.SelectMany(o => new[] { o.SourceLocation, o.TargetLocation }).Distinct())
            if (locations.All(l => l.Id != id)) locations.Add(new(id, StationKind.Buffer, 100));
        var baskets = latest.Select(o =>
        {
            var basket = new Basket(o.PieceNumber, o.ParentPieceNumber ?? o.HeatNumber, o.Quantity, o.Weight,
                o.LoadingLength, o.Status == OrderStatus.Done ? o.TargetLocation : o.SourceLocation);
            if (o.Status == OrderStatus.InTransit) basket.Depart(o.SourceLocation, o.TargetLocation, o.TransportNumber!);
            return basket;
        }).ToList();
        return new(baskets, lots, locations, orders, _transports.Values.ToList()) { Equipment = [_shuttle] };
    }

    public FakeOrderRepository Add(string number, string heat, string piece, string? parent, decimal metres, decimal tonnes,
        string from = "Forge", string to = "Heat treatment", OrderPriority priority = OrderPriority.Normal, TimeSpan? dueIn = null)
    {
        var spec = new Spec(number, heat, piece, parent, metres, tonnes, from, to, priority, createdAt + (dueIn ?? TimeSpan.FromHours(4)));
        _specs.Add(spec);
        _stored[number] = [.. Build(spec).History];
        return this;
    }

    public FakeOrderRepository Change(string number, OrderStatus status, string? note = null)
    {
        var order = Load(_specs.Single(s => s.Number == number));
        order.TransitionTo(status, "seed", createdAt.AddMinutes(3), note);
        _stored[number] = [.. order.History];
        return this;
    }

    public FakeOrderRepository Ship(string transportNumber, Vehicle vehicle, PartLoadReason? reason, params string[] numbers)
    {
        var orders = numbers.Select(n => Load(_specs.Single(s => s.Number == n))).ToList();
        _transports[transportNumber] = Transport.Dispatch(transportNumber, vehicle, orders, "seed", createdAt.AddMinutes(5), reason);
        foreach (var order in orders)
        {
            _stored[order.OrderNumber] = [.. order.History];
        }

        return this;
    }

    public StatusChange LastStoredChange(string number) => _stored[number][^1];

    public Task<IReadOnlyList<TransportOrder>> GetAllAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<TransportOrder>>(_specs.Select(Load).ToList());

    public Task<TransportOrder?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<TransportOrder?> GetByOrderNumberAsync(string orderNumber, CancellationToken cancellationToken = default) =>
        Task.FromResult(_specs.FirstOrDefault(s => s.Number == orderNumber) is { } spec ? Load(spec) : null);

    public Task SaveAsync(TransportOrder order, CancellationToken cancellationToken = default)
    {
        ThrowIfFailing();
        SaveCount++;
        _stored[order.OrderNumber] = [.. order.History];
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Transport>> GetTransportsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Transport>>(_transports.Values.ToList());

    public Task<string> NextTransportNumberAsync(DateTime utcNow, CancellationToken cancellationToken = default) =>
        Task.FromResult($"TR-{utcNow.Year}-{_transports.Count + 1:D4}");

    public Task SaveTransportAsync(Transport transport, IReadOnlyCollection<TransportOrder> orders, CancellationToken cancellationToken = default)
    {
        ThrowIfFailing();
        SaveCount++;
        _transports[transport.TransportNumber] = transport;
        foreach (var order in orders)
        {
            _stored[order.OrderNumber] = [.. order.History];
        }

        return Task.CompletedTask;
    }

    private void ThrowIfFailing()
    {
        if (FailNextSave is { } failure)
        {
            FailNextSave = null;
            throw failure;
        }
    }

    private TransportOrder Build(Spec s) =>
        new(s.Number, s.Heat, s.Piece, s.Parent, "1.2379 forged bar", 2, s.Tonnes, s.Metres, s.From, s.To,
            s.Priority, s.Due, createdAt, "seed");

    /// <summary>Replays the stored history through the domain rules, like materializing a saved row.</summary>
    private TransportOrder Load(Spec spec)
    {
        var order = Build(spec);
        foreach (var change in _stored[spec.Number].Skip(1))
        {
            if (change.ToStatus == OrderStatus.InTransit)
            {
                order.Load(change.Note!, change.ChangedBy, change.ChangedAt);
            }
            else
            {
                order.TransitionTo(change.ToStatus, change.ChangedBy, change.ChangedAt, change.Note);
            }
        }

        return order;
    }

    private sealed record Spec(string Number, string Heat, string Piece, string? Parent, decimal Metres, decimal Tonnes,
        string From, string To, OrderPriority Priority, DateTime Due);
}

internal sealed class FakeMessages : IUserMessageService
{
    public List<string> Errors { get; } = [];

    public List<string> Infos { get; } = [];

    public bool ConfirmAnswer { get; set; } = true;

    public int ConfirmCount { get; private set; }

    /// <summary>Runs while an error is "on screen", to inspect the state the user sees at that moment.</summary>
    public Action? OnError { get; set; }

    public void ShowError(string message)
    {
        Errors.Add(message);
        OnError?.Invoke();
    }

    public void ShowInfo(string message) => Infos.Add(message);

    public bool Confirm(string message)
    {
        ConfirmCount++;
        return ConfirmAnswer;
    }
}

internal sealed class FakeDialogs : IDialogService
{
    public string? ReasonAnswer { get; set; }

    public int ReasonRequests { get; private set; }

    public LoadPlanViewModel? LoadPlan { get; private set; }

    public GenealogyViewModel? Genealogy { get; private set; }

    public string? AskForReason(string title, string prompt)
    {
        ReasonRequests++;
        return ReasonAnswer;
    }

    // The real dialogs are modal; the tests act on the captured view models afterwards, which is equivalent.
    public void ShowLoadPlan(LoadPlanViewModel viewModel) => LoadPlan = viewModel;

    public void ShowGenealogy(GenealogyViewModel viewModel) => Genealogy = viewModel;
}

internal sealed class FakeUser(string name) : ICurrentUser
{
    public string Name { get; } = name;
}
