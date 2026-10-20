using TransportOrders.Core;
using TransportOrders.Data;
using Microsoft.Extensions.Hosting.WindowsServices;

namespace TransportOrders.Api;

public sealed class PlantRuntimeStatus
{
    public bool Running { get; set; }
    public DateTime? LastTickUtc { get; set; }
    public string? LastAction { get; set; }
    public string? LastError { get; set; }
}

public sealed class PlantRuntimeWorker(PlantRuntime runtime, PlantDriverLease lease,
    PlantRuntimeStatus status, ILogger<PlantRuntimeWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Held for the entire lifetime: WPF and a second service cannot schedule concurrently.
        using var ownership = lease.TryEnter();
        if (ownership is null)
        {
            logger.LogCritical("Another process owns automatic plant scheduling");
            if (WindowsServiceHelpers.IsWindowsService()) Environment.Exit(1);
            throw new InvalidOperationException("Another process owns automatic plant scheduling.");
        }
        status.Running = true;
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var result = await runtime.TickAsync(stoppingToken);
                    status.LastTickUtc = DateTime.UtcNow;
                    status.LastAction = result.Action.ToString();
                    status.LastError = null;
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception ex)
                {
                    status.LastError = ex.GetBaseException().Message;
                    logger.LogError(ex, "Plant runtime tick failed; persisted work retained for the next poll");
                }
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
        }
        finally { status.Running = false; }
    }
}
