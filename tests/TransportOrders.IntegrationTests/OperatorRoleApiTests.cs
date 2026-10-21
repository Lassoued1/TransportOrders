using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using TransportOrders.Core;

namespace TransportOrders.IntegrationTests;

public sealed class RoleApiFactory : WebApplicationFactory<Program>
{
    private readonly TempDatabase _database = new();
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing"); builder.UseSetting("Database:Path", _database.Path);
        builder.UseSetting("Access:UseRoles", "true");
        foreach (var role in new[] { "Quality", "Logistics", "Maintenance", "Supervisor" })
            builder.UseSetting($"Access:Roles:{role}:Users:0", $"PLANT\\{role}");
        builder.UseSetting("Access:Roles:Supervisor:Groups:0", "PLANT\\Leads");
        // These must not let an unmapped account bypass role-based access.
        builder.UseSetting("OperatorCommands:AllowedOperators:0", "PLANT\\Unmapped");
        builder.UseSetting("EquipmentControl:AllowedOperators:0", "PLANT\\Unmapped");
        builder.UseTestWindowsAuthentication();
    }
    protected override void Dispose(bool disposing) { base.Dispose(disposing); _database.Dispose(); }
}

public class OperatorRoleApiTests(RoleApiFactory factory) : IClassFixture<RoleApiFactory>
{
    public static IEnumerable<object[]> Matrix()
    {
        var routes = new (string Route, OrderStatus? Target, string[] Roles)[]
        {
            ("/api/equipment/E/control", null, ["Maintenance"]),
            ("/api/packages/P/inspection", null, ["Quality"]),
            ("/api/baskets/B/reload", null, ["Logistics"]),
            ("/api/plc/simulation/settings", null, ["Maintenance"]),
            ("/api/plc/commands/11111111-1111-1111-1111-111111111111/retry", null, ["Maintenance"]),
            ("/api/plc/commands/11111111-1111-1111-1111-111111111111/manual-arrival", null, ["Maintenance"]),
            ("/api/transports/dispatch", null, ["Logistics"]),
            ("/api/transports/TR/manual-arrival", null, ["Logistics"]),
            ("/api/orders/TO/state", OrderStatus.OnHold, ["Quality"]),
            ("/api/orders/TO/state", OrderStatus.Planned, ["Quality"]),
            ("/api/orders/TO/state", OrderStatus.Cancelled, ["Logistics"]),
            ("/api/simulation/step", null, []),
            ("/api/runtime/control", null, [])
        };
        foreach (var route in routes)
            foreach (var role in new[] { "Quality", "Logistics", "Maintenance", "Supervisor" })
                yield return [route.Route, route.Target ?? OrderStatus.Planned, role, role == "Supervisor" || route.Roles.Contains(role)];
    }
    [Theory, MemberData(nameof(Matrix))]
    public async Task Each_route_enforces_the_role_matrix_before_applying_any_change(string route, OrderStatus? target, string role, bool allowed)
    {
        using var client = factory.CreateClient(); client.DefaultRequestHeaders.Add("Test-User", $"PLANT\\{role}");
        // Allowed requests reach validation (400); denied roles receive 403 first. No plant mutation is requested.
        var response = await client.PostAsJsonAsync(route, new { TargetStatus = target ?? OrderStatus.Planned });
        Assert.Equal(allowed ? HttpStatusCode.BadRequest : HttpStatusCode.Forbidden, response.StatusCode);
    }
    [Theory]
    [InlineData("Quality", 2)][InlineData("Logistics", 4)][InlineData("Maintenance", 2)][InlineData("Supervisor", 10)]
    public async Task Access_endpoint_reports_only_the_authenticated_accounts_effective_permissions(string role, int count)
    {
        using var client = factory.CreateClient(); client.DefaultRequestHeaders.Add("Test-User", $"PLANT\\{role}");
        var snapshot = await client.GetFromJsonAsync<OperatorAccessSnapshot>("/api/access");
        Assert.Equal($"PLANT\\{role}", snapshot!.User); Assert.Equal([role], snapshot.Roles); Assert.Equal(count, snapshot.Permissions.Count);
    }
    [Fact]
    public async Task Legacy_allowlists_cannot_bypass_explicit_roles_or_spoofed_request_roles()
    {
        using var client = factory.CreateClient(); client.DefaultRequestHeaders.Add("Test-User", "PLANT\\Unmapped");
        var snapshot = await client.GetFromJsonAsync<OperatorAccessSnapshot>("/api/access");
        Assert.Empty(snapshot!.Roles); Assert.Empty(snapshot.Permissions);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/simulation/step", new { Roles = new[] { "Supervisor" } })).StatusCode);
    }
    [Fact]
    public async Task Authenticated_group_membership_grants_configured_role_but_anonymous_access_is_denied()
    {
        using var client = factory.CreateClient(); client.DefaultRequestHeaders.Add("Test-User", "PLANT\\GroupUser");
        client.DefaultRequestHeaders.Add("Test-Groups", "PLANT\\Leads");
        var access = await client.GetFromJsonAsync<OperatorAccessSnapshot>("/api/access");
        Assert.Contains("Supervisor", access!.Roles); Assert.Equal(10, access.Permissions.Count);
        using var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/access")).StatusCode);
    }
}
