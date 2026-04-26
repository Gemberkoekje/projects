using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SpaceTraders.API.Configuration;
using SpaceTraders.Infrastructure.Persistence;
using SpaceTraders.Infrastructure.Persistence.Entities;
using SpaceTraders.Infrastructure.Persistence.Scoping;
using SpaceTraders.Infrastructure.Persistence.Seed;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Clients;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Accounts;

namespace SpaceTraders.API.Services;

/// <summary>
/// Bootstraps the SpaceTraders agent on startup by loading a stored or configured token,
/// or registering a new agent when none is available.
/// </summary>
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

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var activeToken = await LoadActiveTokenAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(activeToken))
        {
            await BootstrapWithTokenAsync(activeToken, cancellationToken);
            _logger.LogInformation("Loaded active agent token from database.");
            return;
        }

        if (!string.IsNullOrWhiteSpace(_options.AgentToken))
        {
            await BootstrapWithTokenAsync(_options.AgentToken, cancellationToken);
            _logger.LogInformation("Using configured agent token.");
            return;
        }

        var storedToken = await LoadLatestStoredTokenAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(storedToken))
        {
            await BootstrapWithTokenAsync(storedToken, cancellationToken);
            _logger.LogInformation("Loaded agent token from database.");
            return;
        }

        await RegisterNewAgentAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task BootstrapWithTokenAsync(string token, CancellationToken cancellationToken)
    {
        await using var scope = _serviceScopeFactory.CreateAsyncScope();
        var dataScope = scope.ServiceProvider.GetRequiredService<IAgentDataScope>();
        dataScope.Set(token);

        var dbContext = scope.ServiceProvider.GetRequiredService<SpaceTradersDbContext>();
        var credential = await dbContext.Credentials.FindAsync([dbContext.AgentToken, AgentTokenKey], cancellationToken);

        var credentialValues = new StoredCredential
        {
            AgentToken = dbContext.AgentToken,
            Key = AgentTokenKey,
            Value = token,
            StoredAt = TimeProvider.System.GetUtcNow(),
        };

        if (credential is null)
        {
            dbContext.Credentials.Add(credentialValues);
        }
        else
        {
            dbContext.Entry(credential).CurrentValues.SetValues(credentialValues);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await DefaultSettingsSeed.SeedAsync(dbContext, cancellationToken);
        await AgentTokenSelection.SetActiveTokenAsync(dbContext, token, cancellationToken);
        _agentTokenProvider.Set(token);
    }

    private async Task<string> LoadActiveTokenAsync(CancellationToken cancellationToken)
    {
        await using var scope = _serviceScopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<SpaceTradersDbContext>();

        return await AgentTokenSelection.GetActiveTokenAsync(dbContext, cancellationToken);
    }

    private async Task<string> LoadLatestStoredTokenAsync(CancellationToken cancellationToken)
    {
        await using var scope = _serviceScopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<SpaceTradersDbContext>();

        return await AgentTokenSelection.GetLatestAgentTokenAsync(dbContext, cancellationToken);
    }

    private async Task RegisterNewAgentAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.AccountToken))
        {
            throw new InvalidOperationException("SpaceTraders:AccountToken must be configured when no stored agent token exists.");
        }

        if (string.IsNullOrWhiteSpace(_options.AgentName))
        {
            throw new InvalidOperationException("SpaceTraders:AgentName must be configured when no stored agent token exists.");
        }

        await using var scope = _serviceScopeFactory.CreateAsyncScope();
        var apiClient = scope.ServiceProvider.GetRequiredService<ISpaceTradersApiClient>();

        var registration = await apiClient.RegisterAsync(
            new RegisterRequest
            {
                Symbol = _options.AgentName,
                Faction = _options.AgentFaction,
            },
            cancellationToken);

        var dataScope = scope.ServiceProvider.GetRequiredService<IAgentDataScope>();
        dataScope.Set(registration.Token);

        var dbContext = scope.ServiceProvider.GetRequiredService<SpaceTradersDbContext>();

        dbContext.Credentials.Add(new StoredCredential
        {
            AgentToken = dbContext.AgentToken,
            Key = AgentTokenKey,
            Value = registration.Token,
            StoredAt = TimeProvider.System.GetUtcNow(),
        });

        var agent = await dbContext.Agents.FindAsync(new object[] { dbContext.AgentToken, registration.Agent.Symbol }, cancellationToken);
        var agentValues = new CachedAgent
        {
            AgentToken = dbContext.AgentToken,
            Symbol = registration.Agent.Symbol,
            AccountId = registration.Agent.AccountId,
            HeadquartersSymbol = registration.Agent.Headquarters,
            StartingFaction = registration.Agent.StartingFaction,
            Credits = registration.Agent.Credits,
            ShipCount = registration.Agent.ShipCount,
            LastSyncedAt = TimeProvider.System.GetUtcNow(),
        };

        if (agent is null)
        {
            dbContext.Agents.Add(agentValues);
        }
        else
        {
            dbContext.Entry(agent).CurrentValues.SetValues(agentValues);
        }

        foreach (var ship in registration.Ships)
        {
            var cachedShip = await dbContext.Ships.FindAsync(new object[] { dbContext.AgentToken, ship.Symbol }, cancellationToken);
            if (cachedShip is null)
            {
                dbContext.Ships.Add(new CachedShip
                {
                    AgentToken = dbContext.AgentToken,
                    Symbol = ship.Symbol,
                    SystemSymbol = ship.Nav?.SystemSymbol,
                    WaypointSymbol = ship.Nav?.WaypointSymbol,
                    Status = ship.Nav?.Status,
                    FlightMode = ship.Nav?.FlightMode,
                    FuelCurrent = ship.Fuel?.Current ?? 0,
                    FuelCapacity = ship.Fuel?.Capacity ?? 0,
                    LastSyncedAt = TimeProvider.System.GetUtcNow(),
                });
            }
            else
            {
                cachedShip.SystemSymbol = ship.Nav?.SystemSymbol;
                cachedShip.WaypointSymbol = ship.Nav?.WaypointSymbol;
                cachedShip.Status = ship.Nav?.Status;
                cachedShip.FlightMode = ship.Nav?.FlightMode;
                cachedShip.FuelCurrent = ship.Fuel?.Current ?? 0;
                cachedShip.FuelCapacity = ship.Fuel?.Capacity ?? 0;
                cachedShip.LastSyncedAt = TimeProvider.System.GetUtcNow();
            }
        }

        var contract = await dbContext.Contracts.FindAsync(new object[] { dbContext.AgentToken, registration.Contract.Id }, cancellationToken);
        var contractValues = new CachedContract
        {
            AgentToken = dbContext.AgentToken,
            Id = registration.Contract.Id,
            FactionSymbol = registration.Contract.FactionSymbol,
            Type = registration.Contract.Type,
            IsAccepted = registration.Contract.Accepted,
            IsFulfilled = registration.Contract.Fulfilled,
            Expiration = registration.Contract.Expiration,
            DeadlineToAccept = registration.Contract.DeadlineToAccept,
            LastSyncedAt = TimeProvider.System.GetUtcNow(),
        };

        if (contract is null)
        {
            dbContext.Contracts.Add(contractValues);
        }
        else
        {
            dbContext.Entry(contract).CurrentValues.SetValues(contractValues);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await DefaultSettingsSeed.SeedAsync(dbContext, cancellationToken);
        await AgentTokenSelection.SetActiveTokenAsync(dbContext, registration.Token, cancellationToken);
        _agentTokenProvider.Set(registration.Token);
        _logger.LogInformation("Registered new SpaceTraders agent {AgentSymbol}.", registration.Agent.Symbol);
    }
}
