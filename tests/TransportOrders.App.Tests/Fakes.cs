using TransportOrders.App.Services;
using TransportOrders.Core;

namespace TransportOrders.App.Tests;

/// <summary>
/// In-memory stand-in for the database: every load builds new order objects from the "stored" state,
/// so a change that was not saved really disappears on reload, as with SQLite.
/// </summary>
internal sealed class FakeOrderRepository : IOrderRepository
{
    private readonly List<(string Number, string Heat, OrderPriority Priority, DateTime Due)> _rows = [];
    private readonly Dictionary<string, List<StatusChange>> _stored = [];
    private readonly DateTime _createdAt;

    public FakeOrderRepository(DateTime createdAt) => _createdAt = createdAt;

    public int SaveCount { get; private set; }

    public Exception? FailNextSave { get; set; }

    public FakeOrderRepository Add(string number, string heat, OrderStatus status = OrderStatus.Planned,
        OrderPriority priority = OrderPriority.Normal, TimeSpan? dueIn = null)
    {
        _rows.Add((number, heat, priority, _createdAt + (dueIn ?? TimeSpan.FromHours(4))));
        var order = Build(_rows[^1]);
        switch (status)
        {
            case OrderStatus.InTransit:
                order.TransitionTo(OrderStatus.InTransit, "seed", _createdAt.AddMinutes(5));
                break;
            case OrderStatus.Done:
                order.TransitionTo(OrderStatus.InTransit, "seed", _createdAt.AddMinutes(5));
                order.TransitionTo(OrderStatus.Done, "seed", _createdAt.AddMinutes(15));
                break;
            case OrderStatus.Cancelled:
                order.TransitionTo(OrderStatus.Cancelled, "seed", _createdAt.AddMinutes(5));
                break;
        }

        _stored[number] = [.. order.History];
        return this;
    }

    public StatusChange LastStoredChange(string number) => _stored[number][^1];

    public Task<IReadOnlyList<TransportOrder>> GetAllAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<TransportOrder>>(_rows.Select(Load).ToList());

    public Task<TransportOrder?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<TransportOrder?> GetByOrderNumberAsync(string orderNumber, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task SaveAsync(TransportOrder order, CancellationToken cancellationToken = default)
    {
        if (FailNextSave is { } failure)
        {
            FailNextSave = null;
            throw failure;
        }

        SaveCount++;
        _stored[order.OrderNumber] = [.. order.History];
        return Task.CompletedTask;
    }

    private TransportOrder Build((string Number, string Heat, OrderPriority Priority, DateTime Due) row) =>
        new(row.Number, row.Heat, "Billet 6060", 10, "Warehouse A", "Press 1", row.Priority, row.Due, _createdAt, "seed");

    /// <summary>Replays the stored history through the domain rules, like materializing a saved row.</summary>
    private TransportOrder Load((string Number, string Heat, OrderPriority Priority, DateTime Due) row)
    {
        var order = Build(row);
        foreach (var change in _stored[row.Number].Skip(1))
        {
            order.TransitionTo(change.ToStatus, change.ChangedBy, change.ChangedAt);
        }

        return order;
    }
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

internal sealed class FakeUser(string name) : ICurrentUser
{
    public string Name { get; } = name;
}
