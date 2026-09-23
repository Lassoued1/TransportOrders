using System.Text.Json.Serialization;
using TransportOrders.Api;
using TransportOrders.Data;

var builder = WebApplication.CreateBuilder(args);

// Same SQLite file as the WPF app by default; override with Database:Path (appsettings, env var or command line).
var databasePath = builder.Configuration["Database:Path"] ?? DatabasePaths.GetDefaultDatabasePath();
builder.Services.AddTransportOrdersData(databasePath);
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
    Description = "Read-only access to the transport orders managed by the TransportOrders desktop app.",
}));

var app = builder.Build();

var initialization = await app.Services.GetRequiredService<DatabaseInitializer>().InitializeAsync();
app.Logger.LogInformation("Database {Path} ready (demo data seeded: {Seeded})", databasePath, initialization.Seeded);

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapHealthChecks("/health");
app.MapOrderEndpoints();

app.Run();

// Exposes the entry point to WebApplicationFactory in the integration tests.
public partial class Program;
