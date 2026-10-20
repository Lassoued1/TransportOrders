using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using TransportOrders.Api;
using TransportOrders.Core;

namespace TransportOrders.IntegrationTests;

public class AlarmApiTests
{
    [Fact]
    public async Task Only_maintenance_or_supervisor_can_acknowledge_and_actor_is_the_authenticated_account()
    {
        using var factory = new RoleApiFactory(); using var client = factory.CreateClient();
        var repo = factory.Services.GetRequiredService<IOrderRepository>();
        var alarms = factory.Services.GetRequiredService<IAlarmRepository>();
        var equipment = (await repo.GetPlantStateAsync()).Equipment.First();
        await repo.SetEquipmentControlAsync(equipment.Id, equipment.Version, EquipmentMode.Automatic, EquipmentCondition.Fault,
            "test", "Fault for alarm test", DateTime.UtcNow);
        await alarms.SynchronizeAsync(AlarmDetection.Detect(await repo.GetPlantStateAsync(), null, null), DateTime.UtcNow);
        var alarm = Assert.Single(await alarms.ReadAsync());
        var identity = factory.Services.GetRequiredService<PlantServiceIdentity>().Id;
        var command = new AlarmAcknowledgementRequest(alarm.Version, "Equipment inspected", identity);
        var route = $"/api/alarms/{alarm.Id}/acknowledge";
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync(route, command)).StatusCode);
        client.DefaultRequestHeaders.Add("Test-User", "PLANT\\Quality");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(route, command)).StatusCode);
        client.DefaultRequestHeaders.Remove("Test-User"); client.DefaultRequestHeaders.Add("Test-User", "PLANT\\Maintenance");
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(route, command with { ExpectedPlantId = "wrong-site" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(route, command with { Reason = "" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(route, command)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(route, command)).StatusCode);
        var after = Assert.Single(await alarms.ReadAsync());
        Assert.True(after.IsActive); Assert.Equal("PLANT\\Maintenance", after.AcknowledgedBy);
        Assert.Equal(EquipmentCondition.Fault, (await repo.GetPlantStateAsync()).Equipment.Single(e => e.Id == equipment.Id).Condition);
        var snapshot = await client.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/client-state");
        Assert.Equal(1, snapshot.GetProperty("alarms").GetArrayLength());
    }
}
