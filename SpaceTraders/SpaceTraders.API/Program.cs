using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SpaceTraders.Application;
using SpaceTraders.Application.Automation;
using SpaceTraders.Infrastructure.Persistence;
using SpaceTraders.Infrastructure.Persistence.Seed;
using SpaceTraders.Infrastructure.SpaceTradersAPI;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Configuration;
using SpaceTraders.API.Services;

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

// Order matters: AgentBootstrapService → StartupSyncService → GameLoopService → ShipWorkerService → ContractWatchService
builder.Services.AddHostedService<AgentBootstrapService>();
builder.Services.AddHostedService<StartupSyncService>();
builder.Services.AddHostedService<GameLoopService>();
builder.Services.AddHostedService<ShipWorkerService>();
builder.Services.AddHostedService<ContractWatchService>();

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<SpaceTradersDbContext>();
    await dbContext.Database.EnsureCreatedAsync();
    await DefaultSettingsSeed.SeedAsync(dbContext);
}

app.Run();
