using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SpaceTraders.API.Endpoints;
using SpaceTraders.API.Middleware;
using SpaceTraders.API.Services;
using SpaceTraders.Application;
using SpaceTraders.Application.Automation;
using SpaceTraders.Infrastructure.Persistence;
using SpaceTraders.Infrastructure.Persistence.Seed;
using SpaceTraders.Infrastructure.SpaceTradersAPI;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Configuration;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddApplication()
    .AddPersistence(builder.Configuration)
    .AddSpaceTradersApi(options =>
    {
        builder.Configuration.GetSection("SpaceTradersApi").Bind(options);
        options.BaseUrl ??= SpaceTradersApiOptions.DefaultBaseUrl;
        options.AccountToken ??= builder.Configuration["SpaceTraders:AccountToken"];
        options.AgentToken ??= builder.Configuration["SpaceTraders:AgentToken"];
    });

builder.Services
    .AddHealthChecks()
    .AddDbContextCheck<SpaceTradersDbContext>("postgresql");

builder.Services.Configure<HostOptions>(opts =>
    opts.ShutdownTimeout = TimeSpan.FromSeconds(60));

// Order matters: bootstrap → sync → recovery → game loop → ship workers
builder.Services.AddHostedService<AgentBootstrapService>();
builder.Services.AddHostedService<StartupSyncService>();
builder.Services.AddHostedService<StartupRecoveryService>();
builder.Services.AddHostedService<GameLoopService>();
builder.Services.AddHostedService<ShipWorkerService>();
builder.Services.AddHostedService<ContractWatchService>();
builder.Services.AddHostedService<ScoutService>();

var app = builder.Build();

// Skip database initialization in Testing environment to avoid requiring a real DB connection.
if (!app.Environment.IsEnvironment("Testing"))
{
    await using (var scope = app.Services.CreateAsyncScope())
    {
        var dbContext = scope.ServiceProvider.GetRequiredService<SpaceTradersDbContext>();
        await dbContext.Database.EnsureCreatedAsync();
        await DefaultSettingsSeed.SeedAsync(dbContext);
    }
}

app.UseMiddleware<ApiKeyMiddleware>();

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready");
app.MapHealthChecks("/health/startup");

app.MapStatusEndpoints();
app.MapSettingsEndpoints();
app.MapControlEndpoints();

app.Run();

// Required for WebApplicationFactory in integration tests
public partial class Program { }
