using System.Text.Json.Serialization;
using TransportOrders.Api;
using TransportOrders.Data;
using TransportOrders.Core;
using Microsoft.Extensions.Hosting.WindowsServices;
using Serilog;
using Microsoft.AspNetCore.Authentication.Negotiate;

if (MaintenanceCommand.TryRun(args)) return;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = WindowsServiceHelpers.IsWindowsService() ? AppContext.BaseDirectory : null
});
builder.Services.AddWindowsService(options => options.ServiceName = "TransportOrders.Runtime");
var operatingMode = builder.Configuration["Plant:Mode"] ?? "Demo";
if (operatingMode is not ("Demo" or "Commissioning"))
    throw new InvalidOperationException("Supported Plant:Mode values: Demo, Commissioning. A live PLC driver must be commissioned before enabling physical operation.");
var commissioning = operatingMode == "Commissioning";
if (commissioning && (string.IsNullOrWhiteSpace(builder.Configuration["Database:Path"])
    || string.IsNullOrWhiteSpace(builder.Configuration["Plant:Id"])))
    throw new InvalidOperationException("Commissioning requires explicit Database:Path and Plant:Id.");

// Same SQLite file as the WPF app by default; override with Database:Path (appsettings, env var or command line).
var databasePath = builder.Configuration["Database:Path"] ?? DatabasePaths.GetDefaultDatabasePath();
if (WindowsServiceHelpers.IsWindowsService() && (!Path.IsPathFullyQualified(databasePath)
    || string.IsNullOrWhiteSpace(builder.Configuration["Database:Path"])))
    throw new InvalidOperationException("A Windows service requires an explicit absolute Database:Path.");
databasePath = Path.GetFullPath(databasePath);
var logDirectory = builder.Configuration["Runtime:LogDirectory"] ?? Path.Combine(Path.GetDirectoryName(databasePath)!, "logs");
builder.Services.AddSerilog((services, logging) => logging.MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
    .WriteTo.Console().WriteTo.File(Path.Combine(logDirectory, "service-.log"),
        rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14));
builder.Services.AddTransportOrdersData(databasePath);
builder.Services.AddAuthentication(NegotiateDefaults.AuthenticationScheme).AddNegotiate();
var plantIdentity = new PlantServiceIdentity(builder.Configuration, databasePath);
builder.Services.AddSingleton(plantIdentity);
var backupEnabled = builder.Configuration.GetValue<bool>("Backup:Enabled");
if (backupEnabled)
{
    builder.Services.AddSingleton<PlantBackupWorker>();
    builder.Services.AddHostedService(services => services.GetRequiredService<PlantBackupWorker>());
}
var access = new OperatorAccessPolicy(builder.Configuration);
builder.Services.AddSingleton(access);
builder.Services.AddSingleton<Microsoft.AspNetCore.Authorization.IAuthorizationMiddlewareResultHandler, AccessDecisionLogger>();
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
    foreach (var permission in PlantPermissions.All)
        options.AddPolicy(permission, policy => policy.RequireAuthenticatedUser().RequireAssertion(context => access.Allows(context.User, permission)));
});
builder.Services.AddSingleton<PlantOperationGate>();
builder.Services.AddHostedService<PlantAlarmWorker>();
builder.Services.AddSingleton<PlantCommandService>();
var runtimeEnabled = builder.Configuration.GetValue<bool>("Runtime:Enabled");
if (commissioning && runtimeEnabled) throw new InvalidOperationException("Commissioning must keep Runtime:Enabled false.");
builder.Services.AddSingleton<PlantRuntimeStatus>();
if (runtimeEnabled)
{
    builder.Services.AddSingleton(new PlantDriverLease(databasePath));
    builder.Services.AddSingleton<PlantRuntime>();
    builder.Services.AddHostedService<PlantRuntimeWorker>();
}
builder.Services.AddSingleton(PlantTimeZone.FromConfiguration(builder.Configuration));

builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options => options.SwaggerDoc("v1", new()
{
    Title = "TransportOrders API",
    Version = "v1",
    Description = "Plant consultation and authenticated equipment controls for the central runtime.",
}));

var app = builder.Build();

DatabaseInitializationResult initialization;
try { initialization = commissioning
    ? await app.Services.GetRequiredService<DatabaseInitializer>().InitializeSchemaOnlyAsync()
    : await app.Services.GetRequiredService<DatabaseInitializer>().InitializeAsync(); }
catch (Exception ex)
{
    string fileAccess;
    try
    {
        using var probe = File.Open(databasePath, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
        fileAccess = "Database file can be opened for read/write";
    }
    catch (Exception accessError) { fileAccess = accessError.Message; }
    var ancestors = new List<string>();
    for (var folder = Path.GetDirectoryName(databasePath); folder is not null; folder = Path.GetDirectoryName(folder))
    {
        try { ancestors.Add($"{folder}: {File.GetAttributes(folder)}"); }
        catch (Exception folderError) { ancestors.Add($"{folder}: {folderError.Message}"); }
    }
    throw new InvalidOperationException($"Database initialization failed: '{databasePath}'; environment '{app.Environment.EnvironmentName}'; content root '{app.Environment.ContentRootPath}'; file exists {File.Exists(databasePath)}; access: {fileAccess}; ancestors: {string.Join(" | ", ancestors)}.", ex);
}
app.Logger.LogInformation("Database {Path} ready (demo data seeded: {Seeded})", databasePath, initialization.Seeded);

app.UseExceptionHandler();
app.UseStatusCodePages();
// Refuse network plaintext before issuing a Windows authentication challenge.
app.Use(async (context, next) =>
{
    var remote = context.Connection.RemoteIpAddress;
    if (remote?.IsIPv4MappedToIPv6 == true) remote = remote.MapToIPv4();
    if (!context.Request.IsHttps && remote is not null && !System.Net.IPAddress.IsLoopback(remote))
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsync("HTTPS is required for remote plant access.");
        return;
    }
    await next(context);
});
app.UseAuthentication();
app.UseAuthorization();
app.Use(async (context, next) =>
{
    if (commissioning && context.Request.Method is not ("GET" or "HEAD" or "OPTIONS"))
    { context.Response.StatusCode = StatusCodes.Status403Forbidden; return; }
    await next(context);
});
OperatorAccessSnapshot DescribeAccess(HttpContext http, string path)
{
    var description = access.Describe(http.User, path);
    return commissioning ? description with { Permissions = Array.Empty<string>() } : description;
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapHealthChecks("/health").AllowAnonymous();
app.MapGet("/api/backups/status", (IServiceProvider services) => backupEnabled
    ? services.GetRequiredService<PlantBackupWorker>().Status : new PlantBackupStatus(false, null, null, null));
app.MapGet("/api/client-state", async (HttpContext http, IOrderRepository repository, TimeProvider clock,
    PlantRuntimeStatus runtime, PlantTimeZone timeZone, IAlarmRepository alarms, IRuntimeControlRepository controls, CancellationToken ct) =>
{
    var control = await controls.ReadAsync(ct);
    return new PlantClientSnapshot(1, plantIdentity.Id, plantIdentity.Name, timeZone.Zone.Id, clock.GetUtcNow().UtcDateTime,
        PlantStateSnapshot.From(await repository.GetPlantStateAsync(ct)), DescribeAccess(http, ""),
        new(runtimeEnabled, runtime.Running, runtime.LastTickUtc, runtime.LastError, control.Paused, control.Version), await alarms.ReadAsync(ct), operatingMode);
}).RequireAuthorization();
app.MapPost("/api/alarms/{id:guid}/acknowledge", async (Guid id, AlarmAcknowledgementRequest command, HttpContext http,
    IAlarmRepository alarms, PlantOperationGate gate, TimeProvider clock, CancellationToken ct) =>
{
    if (command.ExpectedPlantId != plantIdentity.Id) return Results.Conflict();
    try
    {
        await gate.RunAsync(async () => { await alarms.AcknowledgeAsync(id, command.ExpectedVersion,
            http.User.Identity!.Name!, command.Reason, clock.GetUtcNow().UtcDateTime, ct); return true; }, ct);
        return Results.Ok();
    }
    catch (KeyNotFoundException) { return Results.NotFound(); }
    catch (ArgumentException) { return Results.BadRequest(); }
    catch (Exception ex) when (ex is TransportRuleException or Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException)
    { return Results.Conflict(); }
}).RequireAuthorization(PlantPermissions.Equipment);
app.MapGet("/api/runtime", async (PlantRuntimeStatus status, IRuntimeControlRepository controls, CancellationToken ct) =>
{
    var control = await controls.ReadAsync(ct);
    return new {
    Enabled = runtimeEnabled, status.Running, status.LastTickUtc, status.LastAction, status.LastError,
    control.Paused, control.Version,
    DatabasePath = databasePath,
    PlantId = plantIdentity.Id,
    Mode = "Persistent demo PLC; simulated oven and quality operations" };
});
app.MapRuntimeControls(runtimeEnabled);
app.MapOrderEndpoints();
app.MapRecipeEndpoints(access);
app.MapGet("/api/access", (HttpContext http) => DescribeAccess(http, databasePath)).RequireAuthorization();
app.MapEquipmentControlEndpoints(databasePath);
app.MapOperatorCommands(databasePath);

app.Run();

// Exposes the entry point to WebApplicationFactory in the integration tests.
public partial class Program;
