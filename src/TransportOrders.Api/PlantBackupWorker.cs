using TransportOrders.Data;

namespace TransportOrders.Api;

public sealed record PlantBackupStatus(bool Enabled, DateTime? LastSuccessUtc, string? LastPath, string? LastError);
public sealed class PlantBackupWorker(IConfiguration configuration, PlantServiceIdentity plant,
    ILogger<PlantBackupWorker> logger) : BackgroundService
{
    private PlantBackupStatus _status = new(true, null, null, null);
    public PlantBackupStatus Status => Volatile.Read(ref _status);
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var database = configuration["Database:Path"] ?? throw new InvalidOperationException("Backup requires Database:Path.");
        var directory = configuration["Backup:Directory"] ?? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(database))!, "backups");
        var hours = configuration.GetValue("Backup:IntervalHours", 24);
        if (hours is < 1 or > 168) throw new InvalidOperationException("Backup:IntervalHours must be between 1 and 168.");
        await Task.Yield();
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = await Task.Run(() => DatabaseBackup.Create(database, directory, plant.Id), stoppingToken);
                Volatile.Write(ref _status, new(true, DateTime.UtcNow, result.DatabasePath, null));
                logger.LogInformation("Verified plant backup created: {Path}, SHA256 {Hash}", result.DatabasePath, result.Manifest.Sha256);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                Volatile.Write(ref _status, Status with { LastError = ex.GetBaseException().Message });
                logger.LogError(ex, "Plant backup failed; previous backups retained");
            }
            try { await Task.Delay(TimeSpan.FromHours(hours), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
