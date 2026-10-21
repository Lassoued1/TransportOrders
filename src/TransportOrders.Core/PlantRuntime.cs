namespace TransportOrders.Core;

/// <summary>Polls every outstanding command, even when the scheduler cannot move equipment.</summary>
public sealed class PlantRuntime(IOrderRepository repository, TimeProvider clock, PlantOperationGate? operations = null,
    IRuntimeControlRepository? controls = null)
{
    private readonly PlantOperationGate _operations = operations ?? new();
    public Task<SimulationResult> TickAsync(CancellationToken cancellationToken = default) =>
        _operations.RunAsync(async () =>
        {
            // Check under the same gate as pause/resume, before PLC polling or scheduling.
            if (controls is not null && (await controls.ReadAsync(cancellationToken)).Paused)
                return new SimulationResult(SimulationAction.CentralService);
            var plc = repository as IPlcRepository ?? throw new InvalidOperationException("PLC persistence is required.");
            var state = await repository.GetPlantStateAsync(cancellationToken);
            var exchange = new PlcExchangeService(repository, plc.PlcChannel, clock);
            foreach (var command in state.PlcCommands.Where(c => c.Status is not (PlcCommandStatus.Applied or PlcCommandStatus.Rejected))
                         .OrderBy(c => c.CreatedAt).ThenBy(c => c.Id))
                await exchange.ProcessAsync(command.Id, cancellationToken);
            return await new ExtrusionSimulator(repository, clock).StepAsync(cancellationToken);
        }, cancellationToken);
}
