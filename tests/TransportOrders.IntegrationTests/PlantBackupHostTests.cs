using System.Net;
using System.Net.Http.Json;
using TransportOrders.Api;
using TransportOrders.Data;

namespace TransportOrders.IntegrationTests;

public class PlantBackupHostTests
{
    [Fact]
    public async Task Enabled_worker_creates_verified_backup_and_reports_status_only_to_authenticated_readers()
    {
        using var database = new TempDatabase();
        var directory = Path.GetDirectoryName(database.Path)!;
        using var baseFactory = new ApiFactory();
        using var factory = baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Backup:Enabled", "true");
            builder.UseSetting("Backup:Directory", directory);
            builder.UseSetting("Plant:Id", "backup-test-plant");
        });
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Remove("Test-User");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/backups/status")).StatusCode);
        client.DefaultRequestHeaders.Add("Test-User", "PLANT\\Reader");
        PlantBackupStatus? status = null;
        for (var poll = 0; poll < 100; poll++)
        {
            status = await client.GetFromJsonAsync<PlantBackupStatus>("/api/backups/status");
            if (status?.LastSuccessUtc is not null || status?.LastError is not null) break;
            await Task.Delay(20);
        }
        Assert.True(status!.Enabled); Assert.Null(status.LastError); Assert.NotNull(status.LastSuccessUtc);
        Assert.Equal("backup-test-plant", DatabaseBackup.Verify(status.LastPath!, "backup-test-plant").PlantId);
    }
    [Fact]
    public async Task Backup_failure_is_visible_without_stopping_plant_consultation()
    {
        using var database = new TempDatabase();
        File.WriteAllText(database.Path, "Not a directory");
        using var baseFactory = new ApiFactory();
        using var factory = baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Backup:Enabled", "true");
            builder.UseSetting("Backup:Directory", database.Path);
        });
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Remove("Test-User"); client.DefaultRequestHeaders.Add("Test-User", "PLANT\\Reader");
        PlantBackupStatus? status = null;
        for (var poll = 0; poll < 100; poll++)
        {
            status = await client.GetFromJsonAsync<PlantBackupStatus>("/api/backups/status");
            if (status?.LastError is not null) break;
            await Task.Delay(20);
        }
        Assert.NotNull(status!.LastError); Assert.Null(status.LastSuccessUtc);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/client-state")).StatusCode);
    }
}
