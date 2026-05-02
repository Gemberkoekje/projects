using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Services;
using SpaceTraders.Infrastructure.Persistence.Repositories;
using SpaceTraders.Infrastructure.Persistence.Scoping;

namespace SpaceTraders.Infrastructure.Persistence;

public static class DependencyInjection
{
    public static IServiceCollection AddPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");
        }

        var initialToken = configuration["SpaceTraders:AgentToken"]
            ?? configuration["SpaceTradersApi:AgentToken"]
            ?? configuration["SPACETRADERS_AGENT_TOKEN"]
            ?? string.Empty;

        services.AddSingleton<IAgentDataScope>(_ =>
        {
            var scope = new AgentDataScope();
            if (!string.IsNullOrWhiteSpace(initialToken))
            {
                scope.Set(initialToken);
            }
            return scope;
        });

        // Singleton: tracks current active run ID in memory so LedgerRepository can tag entries.
        services.AddSingleton<ActiveRunIdProvider>();
        services.AddSingleton<IActiveRunIdProvider>(sp => sp.GetRequiredService<ActiveRunIdProvider>());

        services.AddDbContext<SpaceTradersDbContext>((_, options) =>
        {
            options.UseNpgsql(connectionString, pgOptions =>
            {
                pgOptions.CommandTimeout(30);
                pgOptions.EnableRetryOnFailure(maxRetryCount: 3);
            });
        });

        services.AddScoped<IAgentRepository, AgentRepository>();
        services.AddScoped<IShipRepository, ShipRepository>();
        services.AddScoped<IContractRepository, ContractRepository>();
        services.AddScoped<IMarketRepository, MarketRepository>();
        services.AddScoped<IShipyardRepository, ShipyardRepository>();
        services.AddScoped<IShipAssignmentRepository, ShipAssignmentRepository>();
        services.AddScoped<IActivityLogRepository, ActivityLogRepository>();
        services.AddScoped<ISettingsRepository, SettingsRepository>();
        services.AddScoped<ITradeOpportunityRepository, TradeOpportunityRepository>();
        services.AddScoped<IWaypointRepository, WaypointRepository>();
        services.AddScoped<ISurveyRepository, SurveyRepository>();
        services.AddScoped<ISystemRepository, SystemRepository>();
        services.AddScoped<ILeaderLeaseRepository, LeaderLeaseRepository>();
        services.AddScoped<IApiEndpointUsageRecorder, ApiEndpointUsageRecorder>();
        services.AddScoped<IConstructionRepository, ConstructionRepository>();

        services.AddScoped<IAgentCreditsSampleRepository, AgentCreditsSampleRepository>();
        services.AddScoped<IMarketPriceSampleRepository, MarketPriceSampleRepository>();
        services.AddScoped<ILedgerRepository, LedgerRepository>();
        services.AddScoped<IShipTaskRecordRepository, ShipTaskRecordRepository>();
        services.AddScoped<IRunRepository, RunRepository>();
        services.AddScoped<IShipGoalRepository, ShipGoalRepository>();

        return services;
    }
}
