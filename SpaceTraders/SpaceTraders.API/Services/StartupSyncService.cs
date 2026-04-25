using Microsoft.EntityFrameworkCore;
using SpaceTraders.Infrastructure.Persistence;
using SpaceTraders.Infrastructure.Persistence.Entities;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Clients;

namespace SpaceTraders.API.Services;

public sealed class StartupSyncService(
    IServiceScopeFactory serviceScopeFactory,
    ILogger<StartupSyncService> logger) : IHostedService
{
    private readonly IServiceScopeFactory _serviceScopeFactory = serviceScopeFactory;
    private readonly ILogger<StartupSyncService> _logger = logger;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = _serviceScopeFactory.CreateAsyncScope();
        var apiClient = scope.ServiceProvider.GetRequiredService<ISpaceTradersApiClient>();
        var dbContext = scope.ServiceProvider.GetRequiredService<SpaceTradersDbContext>();
        var now = DateTimeOffset.UtcNow;

        var agent = await apiClient.GetMyAgentAsync(cancellationToken);
        dbContext.Agents.Update(new CachedAgent
        {
            Symbol = agent.Symbol,
            AccountId = agent.AccountId,
            HeadquartersSymbol = agent.Headquarters,
            StartingFaction = agent.StartingFaction,
            Credits = agent.Credits,
            ShipCount = agent.ShipCount,
            LastSyncedAt = now
        });

        var ships = await apiClient.GetMyShipsAsync(cancellationToken: cancellationToken);
        foreach (var ship in ships.Data)
        {
            dbContext.Ships.Update(new CachedShip
            {
                Symbol = ship.Symbol,
                SystemSymbol = ship.Nav?.SystemSymbol,
                WaypointSymbol = ship.Nav?.WaypointSymbol,
                Status = ship.Nav?.Status,
                FlightMode = ship.Nav?.FlightMode,
                FuelCurrent = ship.Fuel?.Current ?? 0,
                FuelCapacity = ship.Fuel?.Capacity ?? 0,
                LastSyncedAt = now
            });
        }

        var contracts = await apiClient.GetMyContractsAsync(cancellationToken: cancellationToken);
        foreach (var contract in contracts.Data)
        {
            dbContext.Contracts.Update(new CachedContract
            {
                Id = contract.Id,
                FactionSymbol = contract.FactionSymbol,
                Type = contract.Type,
                IsAccepted = contract.Accepted,
                IsFulfilled = contract.Fulfilled,
                Expiration = contract.Expiration,
                DeadlineToAccept = contract.DeadlineToAccept,
                LastSyncedAt = now
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Completed startup sync for agent, ships, and contracts.");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
