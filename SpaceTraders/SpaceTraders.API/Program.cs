using ImTools;
using JasperFx.MultiTenancy;
using JasperFx.Resources;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Prometheus;
using Serilog;
using Serilog.Formatting.Compact;
using SpaceTraders.API.Configuration;
using SpaceTraders.API.Endpoints;
using SpaceTraders.API.Hubs;
using SpaceTraders.API.Middleware;
using SpaceTraders.API.Services;
using SpaceTraders.Application;
using SpaceTraders.Application.Automation;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Infrastructure.Persistence;
using SpaceTraders.Infrastructure.Persistence.Seed;
using SpaceTraders.Infrastructure.SpaceTradersAPI;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Configuration;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.Persistence;
using Wolverine.Postgresql;

const string PathBase = "/spacetraders/api";

var builder = WebApplication.CreateBuilder(args);

if (builder.Environment.IsDevelopment())
{
    builder.Configuration.AddUserSecrets<Program>(optional: true, reloadOnChange: true);
}

builder.Services.Configure<SpaceTradersBootstrapOptions>(builder.Configuration.GetSection("SpaceTraders"));
builder.Services.AddSingleton<StartupInitializationState>();

// CORS — allow the configured WebUI origin (or any origin if not configured, for development convenience).
var webUiOrigin = builder.Configuration["WebUI:Origin"];
builder.Services.AddCors(options =>
{
    options.AddPolicy("Dashboard", policy =>
    {
        if (!string.IsNullOrWhiteSpace(webUiOrigin))
        {
            policy.WithOrigins(webUiOrigin)
                  .AllowAnyHeader()
                  .WithMethods("GET")
                  .AllowCredentials(); // required for SignalR WebSocket upgrade
        }
        else
        {
            policy.AllowAnyOrigin().AllowAnyHeader().WithMethods("GET");
        }
    });
});

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
if (!builder.Environment.IsEnvironment("Testing"))
{
    builder.Host.UseResourceSetupOnStartup();
}

builder.Services
    .AddApplication(opts => ConfigureWolverine(opts, builder.Configuration, builder.Environment))
    .AddPersistence(builder.Configuration)
    .AddSpaceTradersApi(options =>
    {
        builder.Configuration.GetSection("SpaceTradersApi").Bind(options);
        options.BaseUrl ??= SpaceTradersApiOptions.DefaultBaseUrl;
        options.AccountToken ??= builder.Configuration["SpaceTraders:AccountToken"];
        options.AgentToken ??= builder.Configuration["SpaceTraders:AgentToken"];
    });

builder.Services.AddSingleton<SpaceTraders.Application.Interfaces.ICreditHistoryService, SpaceTraders.Application.Services.CreditHistoryService>();

builder.Services.AddSingleton<SettingsSnapshotLogger>();
builder.Services.AddSingleton<AgentBootstrapService>();
builder.Services.AddSingleton<StartupSyncService>();
builder.Services.AddSingleton<StartupSnapshotService>();
builder.Services.AddSingleton<StartupRecoveryService>();
builder.Services.AddSingleton<SettingsStartupLoggingService>();
builder.Services.AddSingleton<GameLoopService>();
builder.Services.AddSingleton<ActivityLogPruningService>();
builder.Services.AddSingleton<DataRetentionService>();
builder.Services.AddSingleton<PrometheusMetricsService>();

// RunLifecycleService is both a singleton startup-managed service and the IRunLifecycleManager implementation.
builder.Services.AddSingleton<RunLifecycleService>();
builder.Services.AddSingleton<SpaceTraders.Application.Interfaces.IRunLifecycleManager>(
    sp => sp.GetRequiredService<RunLifecycleService>());

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// SignalR hub for real-time dashboard invalidation events.
builder.Services.AddSignalR();
builder.Services.AddSingleton<IDashboardNotifier, DashboardNotifier>();

builder.Services
    .AddHealthChecks()
    .AddDbContextCheck<SpaceTradersDbContext>("postgresql", tags: ["ready"])
    .AddCheck<StartupInitializationHealthCheck>("startup", tags: ["startup"]);

builder.Services.Configure<HostOptions>(opts =>
    opts.ShutdownTimeout = TimeSpan.FromSeconds(60));

// LeaderElectionService doubles as ILeaderElection (singleton) and is started after deferred bootstrap completes.
builder.Services.AddSingleton<LeaderElectionService>();
builder.Services.AddSingleton<SpaceTraders.Application.Interfaces.ILeaderElection>(
    sp => sp.GetRequiredService<LeaderElectionService>());

builder.Services.AddHostedService<DeferredStartupHostedService>();

var app = builder.Build();

app.UsePathBase(PathBase);
app.UseCors("Dashboard");

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseMiddleware<ApiKeyMiddleware>();

app.UseHttpMetrics();

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready"),
});
app.MapHealthChecks("/health/startup", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("startup"),
});

app.MapMetrics();
app.MapStatusEndpoints();
app.MapSettingsEndpoints();
app.MapControlEndpoints();
app.MapHealthExtendedEndpoints();
app.MapFleetStatusEndpoints();
app.MapMarketsEndpoints();
app.MapShipyardsEndpoints();
app.MapHub<DashboardHub>("/hubs/dashboard");

await app.RunAsync();

static void ConfigureWolverine(
    WolverineOptions options,
    IConfiguration configuration,
    IHostEnvironment environment)
{
    var connectionString = configuration.GetConnectionString("DefaultConnection");

    if (string.IsNullOrWhiteSpace(connectionString))
    {
        throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");
    }

    options.Durability.DurabilityAgentEnabled = false;

    if (environment.IsEnvironment("Testing"))
    {
        // Do not connect to Postgres in test/DI-validation hosts; keep in-memory message persistence.
        return;
    }

    options.UseEntityFrameworkCoreTransactions(TransactionMiddlewareMode.Eager);
    options.PersistMessagesWithPostgresql(connectionString, "wolverine")
        .Enroll<SpaceTradersDbContext>();
    options.Policies.UseDurableLocalQueues();
}

/// <summary>Entry point marker for the SpaceTraders API; used by WebApplicationFactory in integration tests.</summary>
public sealed partial class Program
{
}
