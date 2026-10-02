using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
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
using SpaceTraders.Application.Health;
using SpaceTraders.Application.Interfaces;
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

// Every warning and error also goes to the RepeatingError health rule.
builder.Services.AddSingleton<ErrorLog>();

// The host logs through its own logger, not the process-wide Log.Logger (preserveStaticLogger),
// which every host replaces when it starts and closes when it stops: tests run hosts side by side,
// and a host's lines reached another host's sinks, or none (B41). Nothing here uses the static Log.
builder.Host.UseSerilog(
    (ctx, services, cfg) =>
    {
        if (ctx.HostingEnvironment.IsProduction())
        {
            // JSON for Loki, with the rendered message, so a line reads without its template.
            cfg.WriteTo.Console(new RenderedCompactJsonFormatter());
        }
        else
        {
            cfg.WriteTo.Console();
        }

        cfg.WriteTo.Sink(new ErrorLogSink(services.GetRequiredService<ErrorLog>()));
        cfg.ReadFrom.Configuration(ctx.Configuration);
        cfg.Enrich.FromLogContext();
        cfg.Enrich.WithProperty("Application", "SpaceTraders.API");
    },
    preserveStaticLogger: true);

// Wolverine keeps messages in memory: nothing is stored in Postgres (B2). After a restart, startup
// sync and startup recovery pick the ships up again, and arrivals wait in scheduled_ship_events.
// EF Core registers the DbContext's options through a factory, so Wolverine 6's generated handler
// code can't construct the DbContext itself; resolving it from the scope is the intended way.
builder.Services
    .AddApplication(opts => opts.CodeGeneration.AlwaysUseServiceLocationFor<SpaceTradersDbContext>())
    .AddPersistence(builder.Configuration)
    .AddSpaceTradersApi(options =>
    {
        builder.Configuration.GetSection("SpaceTradersApi").Bind(options);
        options.BaseUrl ??= SpaceTradersApiOptions.DefaultBaseUrl;
        options.AccountToken ??= builder.Configuration["SpaceTraders:AccountToken"];
        options.AgentToken ??= builder.Configuration["SpaceTraders:AgentToken"];
    });

builder.Services.AddSingleton<SpaceTraders.Application.Interfaces.ICreditHistoryService, SpaceTraders.Application.Services.CreditHistoryService>();
builder.Services.AddSingleton(Metrics.DefaultRegistry);
builder.Services.AddSingleton<IAutomationMetrics, PrometheusAutomationMetrics>();

// /metrics has a port of its own, which the Service and the ingress don't route: Prometheus scrapes
// the pod there, without the API key that guards everything on the main port (B11). Unset or 0:
// no metrics server (the tests).
var metricsPort = builder.Configuration.GetValue("Metrics:Port", 0);
if (metricsPort > 0)
{
    builder.Services.AddMetricServer(options =>
    {
        options.Port = checked((ushort)metricsPort);
        options.Hostname = builder.Configuration["Metrics:Hostname"] is { Length: > 0 } hostname ? hostname : "+";
    });
}

builder.Services.AddSingleton<IServerResetMonitor, ServerResetMonitor>();

builder.Services.AddSingleton<SettingsSnapshotLogger>();
builder.Services.AddSingleton<AgentBootstrapService>();
builder.Services.AddSingleton<StartupSyncService>();
builder.Services.AddSingleton<StartupSnapshotService>();
builder.Services.AddSingleton<StartupRecoveryService>();
builder.Services.AddSingleton<SettingsStartupLoggingService>();
builder.Services.AddSingleton<GameLoopService>();
builder.Services.AddSingleton<DataRetentionService>();
builder.Services.AddSingleton<DatabaseSizeGuardService>();
builder.Services.AddSingleton<ShipStateJournal>();
builder.Services.AddSingleton<PrometheusMetricsService>();
builder.Services.AddSingleton<PrometheusMarketMetricsService>();

// The health rules (phase 3): the monitor evaluates them every minute, each in the scope of one
// evaluation. The API client's handler records the 401s and 429s they read.
builder.Services.AddSingleton<HealthMonitorService>();
builder.Services.AddSingleton<ApiResponseLog>();
builder.Services.AddScoped<HealthFleet>();
builder.Services.AddScoped<IHealthRule, ContractStalledRule>();
builder.Services.AddScoped<IHealthRule, ContractLeftOpenRule>();
builder.Services.AddScoped<IHealthRule, ContractDeadlineAtRiskRule>();
builder.Services.AddScoped<IHealthRule, ShipStuckRule>();
builder.Services.AddScoped<IHealthRule, ShipLeftIdleRule>();
builder.Services.AddScoped<IHealthRule, CircuitBreakerTrippedRule>();
builder.Services.AddScoped<IHealthRule, RepeatingErrorRule>();
builder.Services.AddScoped<IHealthRule, CreditsUnchangedRule>();
builder.Services.AddScoped<IHealthRule, ApiUnauthorizedRule>();
builder.Services.AddScoped<IHealthRule, ApiThrottledRule>();

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

// Defines every spacetraders_* metric, so the first scrape lists them all.
app.Services.GetRequiredService<IAutomationMetrics>();

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

    // Kubernetes' startup probe reads this (B23). "Still running" is Degraded, which answers 200 by
    // default: the probe would pass before the startup chain has completed (B39).
    ResultStatusCodes = { [HealthStatus.Degraded] = StatusCodes.Status503ServiceUnavailable },
});

app.MapStatusEndpoints();
app.MapSettingsEndpoints();
app.MapControlEndpoints();
app.MapHealthExtendedEndpoints();
app.MapFleetStatusEndpoints();
app.MapMarketsEndpoints();
app.MapShipyardsEndpoints();
app.MapHub<DashboardHub>("/hubs/dashboard");

var startupState = app.Services.GetRequiredService<StartupInitializationState>();

await app.RunAsync();

// A failed startup chain stops the host (DeferredStartupHostedService). Exit non-zero, so the
// restart shows up as a failure.
if (startupState.HasFailed)
{
    Environment.ExitCode = 1;
}

/// <summary>Entry point marker for the SpaceTraders API; used by WebApplicationFactory in integration tests.</summary>
public sealed partial class Program
{
}
