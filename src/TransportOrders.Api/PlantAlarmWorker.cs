using TransportOrders.Core;

namespace TransportOrders.Api;

public sealed class PlantAlarmWorker(IOrderRepository orders, IAlarmRepository alarms, PlantOperationGate gate,
    PlantRuntimeStatus runtime, IServiceProvider services, TimeProvider clock, ILogger<PlantAlarmWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await gate.RunAsync(async () =>
                {
                    var state = await orders.GetPlantStateAsync(stoppingToken);
                    await alarms.SynchronizeAsync(AlarmDetection.Detect(state, runtime.LastError,
                        services.GetService<PlantBackupWorker>()?.Status.LastError), clock.GetUtcNow().UtcDateTime, stoppingToken);
                    return true;
                }, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "Alarm synchronization failed; existing alarm history retained"); }
            try { await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
public sealed record AlarmAcknowledgementRequest(Guid ExpectedVersion, string Reason, string ExpectedPlantId);
