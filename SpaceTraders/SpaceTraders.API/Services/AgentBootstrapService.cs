using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SpaceTraders.API.Configuration;
using SpaceTraders.Infrastructure.Persistence;
using SpaceTraders.Infrastructure.Persistence.Entities;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Clients;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Accounts;

namespace SpaceTraders.API.Services;

public sealed class AgentBootstrapService(
    IServiceScopeFactory serviceScopeFactory,
    IAgentTokenProvider agentTokenProvider,
    IOptions<SpaceTradersBootstrapOptions> options,
    ILogger<AgentBootstrapService> logger) : IHostedService
{
    private const string AgentTokenKey = "AgentToken";

    private readonly IServiceScopeFactory _serviceScopeFactory = serviceScopeFactory;
    private readonly IAgentTokenProvider _agentTokenProvider = agentTokenProvider;
    private readonly SpaceTradersBootstrapOptions _options = options.Value;
    private readonly ILogger<AgentBootstrapService> _logger = logger;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = _serviceScopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<SpaceTradersDbContext>();
        var apiClient = scope.ServiceProvider.GetRequiredService<ISpaceTradersApiClient>();

        var storedToken = await dbContext.Credentials
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Key == AgentTokenKey, cancellationToken);

        if (storedToken is not null)
        {
            _agentTokenProvider.Set(storedToken.Value);
            _logger.LogInformation("Loaded agent token from database.");
            return;
        }

        if (string.IsNullOrWhiteSpace(_options.AccountToken))
        {
            throw new InvalidOperationException("SpaceTraders:AccountToken must be configured when no stored agent token exists.");
        }

        if (string.IsNullOrWhiteSpace(_options.AgentName))
        {
            throw new InvalidOperationException("SpaceTraders:AgentName must be configured when no stored agent token exists.");
        }

        var registration = await apiClient.RegisterAsync(new RegisterRequest
        {
            Symbol = _options.AgentName,
            Faction = _options.AgentFaction
        }, cancellationToken);

        dbContext.Credentials.Add(new StoredCredential
        {
            Key = AgentTokenKey,
            Value = registration.Token,
            StoredAt = DateTimeOffset.UtcNow
        });

        dbContext.Agents.Update(new CachedAgent
        {
            Symbol = registration.Agent.Symbol,
            AccountId = registration.Agent.AccountId,
            HeadquartersSymbol = registration.Agent.Headquarters,
            StartingFaction = registration.Agent.StartingFaction,
            Credits = registration.Agent.Credits,
            ShipCount = registration.Agent.ShipCount,
            LastSyncedAt = DateTimeOffset.UtcNow
        });

        foreach (var ship in registration.Ships)
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
                LastSyncedAt = DateTimeOffset.UtcNow
            });
        }

        dbContext.Contracts.Update(new CachedContract
        {
            Id = registration.Contract.Id,
            FactionSymbol = registration.Contract.FactionSymbol,
            Type = registration.Contract.Type,
            IsAccepted = registration.Contract.Accepted,
            IsFulfilled = registration.Contract.Fulfilled,
            Expiration = registration.Contract.Expiration,
            DeadlineToAccept = registration.Contract.DeadlineToAccept,
            LastSyncedAt = DateTimeOffset.UtcNow
        });

        await dbContext.SaveChangesAsync(cancellationToken);
        _agentTokenProvider.Set(registration.Token);
        _logger.LogInformation("Registered new SpaceTraders agent {AgentSymbol}.", registration.Agent.Symbol);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
