using System.Text.RegularExpressions;

namespace TransportOrders.Core;

public enum RecipeStatus { Draft, Validated, Archived }
public sealed record RecipeParameters(string Alloy, decimal TargetTemperature, int HeatingMinutes, int HoldingMinutes, int CoolingMinutes);
public sealed record RecipeEventSnapshot(DateTime AtUtc, string Actor, string Action, string Reason, int Revision,
    RecipeStatus Status, RecipeParameters Parameters);
public sealed class RecipeEvent
{
    private RecipeEvent() { }
    public RecipeEvent(DateTime now, string actor, string action, string reason, OvenRecipe recipe)
    { AtUtc = now; Actor = actor; Action = action; Reason = reason; Revision = recipe.Revision; Status = recipe.Status;
      Alloy = recipe.Alloy; TargetTemperature = recipe.TargetTemperature; HeatingMinutes = recipe.HeatingMinutes;
      HoldingMinutes = recipe.HoldingMinutes; CoolingMinutes = recipe.CoolingMinutes; }
    public int Id { get; private set; }
    public DateTime AtUtc { get; private set; }
    public string Actor { get; private set; } = "";
    public string Action { get; private set; } = "";
    public string Reason { get; private set; } = "";
    public int Revision { get; private set; }
    public RecipeStatus Status { get; private set; }
    public string Alloy { get; private set; } = "";
    public decimal TargetTemperature { get; private set; }
    public int HeatingMinutes { get; private set; }
    public int HoldingMinutes { get; private set; }
    public int CoolingMinutes { get; private set; }
    public RecipeEventSnapshot Snapshot() => new(AtUtc, Actor, Action, Reason, Revision, Status,
        new(Alloy, TargetTemperature, HeatingMinutes, HoldingMinutes, CoolingMinutes));
    internal static RecipeEvent Restore(RecipeEventSnapshot s) => new() { AtUtc = s.AtUtc, Actor = s.Actor, Action = s.Action,
        Reason = s.Reason, Revision = s.Revision, Status = s.Status, Alloy = s.Parameters.Alloy,
        TargetTemperature = s.Parameters.TargetTemperature, HeatingMinutes = s.Parameters.HeatingMinutes,
        HoldingMinutes = s.Parameters.HoldingMinutes, CoolingMinutes = s.Parameters.CoolingMinutes };
}

/// <summary>Illustrative demo settings, not approved production heat-treatment parameters.</summary>
public sealed partial class OvenRecipe
{
    private readonly List<RecipeEvent> _history = [];
    private OvenRecipe() { }
    public OvenRecipe(string id, string alloy, decimal targetTemperature, int heatingMinutes, int holdingMinutes, int coolingMinutes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (id.Length > 100 || !Regex.IsMatch(id, "^[A-Za-z0-9_.-]+$")) throw new ArgumentException("Use a recipe identifier of 1-100 letters, digits, dots, underscores or hyphens.");
        if (!Regex.IsMatch(alloy, "^[0-9]{4}$")) throw new ArgumentException("Use a four-digit aluminium alloy.", nameof(alloy));
        if (targetTemperature <= 20 || targetTemperature > 500) throw new ArgumentOutOfRangeException(nameof(targetTemperature));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(heatingMinutes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(holdingMinutes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(coolingMinutes);
        if ((long)heatingMinutes + holdingMinutes + coolingMinutes > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(coolingMinutes), "Total duration exceeds the simulation timer range.");
        Id = id; Alloy = alloy; TargetTemperature = targetTemperature;
        HeatingMinutes = heatingMinutes; HoldingMinutes = holdingMinutes; CoolingMinutes = coolingMinutes;
        Record(DateTime.UtcNow, "INITIALIZER", "Imported", "Illustrative simulation recipe");
    }
    public string Id { get; private set; } = string.Empty;
    public string Alloy { get; private set; } = string.Empty;
    public decimal TargetTemperature { get; private set; }
    public int HeatingMinutes { get; private set; }
    public int HoldingMinutes { get; private set; }
    public int CoolingMinutes { get; private set; }
    public int Revision { get; private set; } = 1;
    public Guid Version { get; private set; }
    public RecipeStatus Status { get; private set; } = RecipeStatus.Validated;
    public IReadOnlyList<RecipeEvent> History => _history.AsReadOnly();
    public bool Supports(MaterialLot lot) => Status == RecipeStatus.Validated && Regex.IsMatch(lot.Material, $"^EN AW-{Alloy}(?:\\s|$)", RegexOptions.CultureInvariant);
    public static OvenRecipe CreateDraft(string id, RecipeParameters p, string actor, string reason, DateTime now)
    {
        Review(actor, reason); var recipe = new OvenRecipe(id, p.Alloy, p.TargetTemperature, p.HeatingMinutes, p.HoldingMinutes, p.CoolingMinutes);
        recipe._history.Clear(); recipe.Status = RecipeStatus.Draft; recipe.Record(now, actor, "Created", reason); return recipe;
    }
    public void Revise(Guid version, RecipeParameters p, string actor, string reason, DateTime now)
    {
        Check(version); Review(actor, reason);
        if (Status == RecipeStatus.Archived) throw new TransportRuleException("Archived recipes cannot be edited.");
        _ = new OvenRecipe(Id, p.Alloy, p.TargetTemperature, p.HeatingMinutes, p.HoldingMinutes, p.CoolingMinutes);
        Alloy = p.Alloy; TargetTemperature = p.TargetTemperature; HeatingMinutes = p.HeatingMinutes;
        HoldingMinutes = p.HoldingMinutes; CoolingMinutes = p.CoolingMinutes;
        Revision = checked(Revision + 1); Status = RecipeStatus.Draft; Record(now, actor, "Revised", reason);
    }
    public void Validate(Guid version, string actor, string reason, DateTime now)
    {
        Check(version); Review(actor, reason);
        if (Status != RecipeStatus.Draft) throw new TransportRuleException("Only a draft can be validated for simulation.");
        Status = RecipeStatus.Validated; Record(now, actor, "Validated", reason);
    }
    public void Archive(Guid version, string actor, string reason, DateTime now)
    {
        Check(version); Review(actor, reason);
        if (Status == RecipeStatus.Archived) throw new TransportRuleException("Recipe already archived.");
        Status = RecipeStatus.Archived; Record(now, actor, "Archived", reason);
    }
    public void ImportLegacy(DateTime now)
    { if (Version == Guid.Empty) Record(now, "MIGRATION", "Imported", "Existing usable simulation recipe retained"); }
    private void Check(Guid version) { if (version != Version || version == Guid.Empty) throw new TransportRuleException("Recipe changed; reload before editing."); }
    private static void Review(string actor, string reason)
    { ArgumentException.ThrowIfNullOrWhiteSpace(actor); ArgumentException.ThrowIfNullOrWhiteSpace(reason);
      if (reason.Length > 1000) throw new ArgumentException("Reason must not exceed 1000 characters."); }
    private void Record(DateTime now, string actor, string action, string reason)
    { Version = Guid.NewGuid(); _history.Add(new(now, actor, action, reason, this)); }
}

public interface IRecipeRepository
{
    Task CreateAsync(string id, RecipeParameters p, string actor, string reason, DateTime now, CancellationToken ct = default);
    Task ChangeAsync(string id, Guid version, string action, RecipeParameters? p, string actor, string reason, DateTime now, CancellationToken ct = default);
}
