using System.Security.Claims;
using TransportOrders.Core;

namespace TransportOrders.Api;

public sealed class OperatorRoleAssignment
{
    public string[] Users { get; set; } = [];
    public string[] Groups { get; set; } = [];
}

public sealed class OperatorAccessPolicy
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> RolePermissions =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Quality"] = [PlantPermissions.Quality, PlantPermissions.OrderQuality],
            ["Logistics"] = [PlantPermissions.Basket, PlantPermissions.OrderCancel, PlantPermissions.Dispatch, PlantPermissions.Arrival],
            ["Maintenance"] = [PlantPermissions.Equipment, PlantPermissions.Plc],
            ["Supervisor"] = PlantPermissions.All
        };
    private readonly Dictionary<string, OperatorRoleAssignment> _roles;
    private readonly string[] _equipment;
    private readonly string[] _operators;
    private readonly bool _useRoles;
    public OperatorAccessPolicy(IConfiguration configuration)
    {
        var configured = configuration.GetSection("Access:Roles").Get<Dictionary<string, OperatorRoleAssignment>>() ?? [];
        if (configured.Keys.Any(role => !RolePermissions.ContainsKey(role)))
            throw new InvalidOperationException("Unknown operator role. Use Quality, Logistics, Maintenance or Supervisor.");
        _roles = configured.ToDictionary(e => RolePermissions.Keys.Single(role => role.Equals(e.Key, StringComparison.OrdinalIgnoreCase)),
            e => e.Value ?? new OperatorRoleAssignment(), StringComparer.OrdinalIgnoreCase);
        _useRoles = configuration.GetValue<bool>("Access:UseRoles") || _roles.Count > 0;
        _equipment = configuration.GetSection("EquipmentControl:AllowedOperators").Get<string[]>() ?? [];
        _operators = configuration.GetSection("OperatorCommands:AllowedOperators").Get<string[]>() ?? [];
    }
    public OperatorAccessSnapshot Describe(ClaimsPrincipal user, string databasePath)
    {
        var roles = new List<string>(); var permissions = new HashSet<string>(StringComparer.Ordinal);
        if (user.Identity?.IsAuthenticated == true)
        {
            if (_useRoles)
            {
                foreach (var entry in _roles)
                    if (entry.Value.Users.Contains(user.Identity.Name ?? "", StringComparer.OrdinalIgnoreCase)
                        || entry.Value.Groups.Any(group => !string.IsNullOrWhiteSpace(group) && user.IsInRole(group)))
                    { roles.Add(entry.Key); permissions.UnionWith(RolePermissions[entry.Key]); }
            }
            else
            {
                // Existing installations keep their explicit allowlists until a role mapping is installed.
                if (_equipment.Contains(user.Identity.Name ?? "", StringComparer.OrdinalIgnoreCase))
                { roles.Add("LegacyEquipment"); permissions.Add(PlantPermissions.Equipment); }
                if (_operators.Contains(user.Identity.Name ?? "", StringComparer.OrdinalIgnoreCase))
                { roles.Add("LegacyOperator"); permissions.UnionWith(PlantPermissions.All.Where(p => p != PlantPermissions.Equipment && p != PlantPermissions.Runtime)); }
            }
        }
        return new(user.Identity?.Name ?? "", roles.Order().ToArray(), permissions.Order().ToArray(), databasePath);
    }
    public bool Allows(ClaimsPrincipal user, string permission) => Describe(user, "").Permissions.Contains(permission);
}
