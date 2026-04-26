using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Prometheus;
using Serilog;
using Serilog.Formatting.Compact;
using SpaceTraders.API.Configuration;
using SpaceTraders.API.Endpoints;
using SpaceTraders.API.Middleware;
using SpaceTraders.API.Services;
using SpaceTraders.Application;
using SpaceTraders.Application.Automation;
using SpaceTraders.Infrastructure.Persistence;
using SpaceTraders.Infrastructure.Persistence.Seed;
using SpaceTraders.Infrastructure.SpaceTradersAPI;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Configuration;

const string PathBase = "/spacetraders/api";

var builder = WebApplication.CreateBuilder(args);

if (builder.Environment.IsDevelopment())
{
    builder.Configuration.AddUserSecrets<Program>(optional: true, reloadOnChange: true);
}

builder.Services.Configure<SpaceTradersBootstrapOptions>(builder.Configuration.GetSection("SpaceTraders"));

builder.Host.UseSerilog((ctx, cfg) =>
{
    if (ctx.HostingEnvironment.IsProduction())
    {
        cfg.WriteTo.Console(new CompactJsonFormatter());
    }
    else
    {
        cfg.WriteTo.Console();
    }

    cfg.ReadFrom.Configuration(ctx.Configuration);
    cfg.Enrich.FromLogContext();
    cfg.Enrich.WithProperty("Application", "SpaceTraders.API");
});

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

builder.Services.AddSingleton<SettingsSnapshotLogger>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services
    .AddHealthChecks()
    .AddDbContextCheck<SpaceTradersDbContext>("postgresql");

builder.Services.Configure<HostOptions>(opts =>
    opts.ShutdownTimeout = TimeSpan.FromSeconds(60));

// LeaderElectionService doubles as ILeaderElection (singleton) and IHostedService.
builder.Services.AddSingleton<LeaderElectionService>();
builder.Services.AddSingleton<SpaceTraders.Application.Interfaces.ILeaderElection>(
    sp => sp.GetRequiredService<LeaderElectionService>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<LeaderElectionService>());

builder.Services.AddHostedService<AgentBootstrapService>();
builder.Services.AddHostedService<StartupSyncService>();
builder.Services.AddHostedService<StartupRecoveryService>();
builder.Services.AddHostedService<SettingsStartupLoggingService>();
builder.Services.AddHostedService<GameLoopService>();
builder.Services.AddHostedService<ShipWorkerService>();
builder.Services.AddHostedService<ContractWatchService>();
builder.Services.AddHostedService<ScoutService>();
builder.Services.AddHostedService<ActivityLogPruningService>();
builder.Services.AddHostedService<PrometheusMetricsService>();

var app = builder.Build();

app.UsePathBase(PathBase);

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Skip database initialization in Testing environment to avoid requiring a real DB connection.
if (!app.Environment.IsEnvironment("Testing"))
{
    await using var scope = app.Services.CreateAsyncScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<SpaceTradersDbContext>();
    await SpaceTradersDatabaseInitializer.InitializeAsync(dbContext);
}

app.UseMiddleware<ApiKeyMiddleware>();

app.UseHttpMetrics();

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready");
app.MapHealthChecks("/health/startup");

app.MapMetrics();
app.MapStatusEndpoints();
app.MapSettingsEndpoints();
app.MapControlEndpoints();

await app.RunAsync();

/// <summary>Entry point marker for the SpaceTraders API; used by WebApplicationFactory in integration tests.</summary>
public sealed partial class Program
{
}
