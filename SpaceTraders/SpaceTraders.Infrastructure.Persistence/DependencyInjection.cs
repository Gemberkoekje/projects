using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Infrastructure.Persistence.Repositories;

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

        services.AddDbContext<SpaceTradersDbContext>(options => options.UseNpgsql(connectionString));

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

        return services;
    }
}
