using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SpaceTraders.API.Configuration;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Domain.Events;
using SpaceTraders.Infrastructure.Persistence;
using SpaceTraders.Infrastructure.Persistence.Entities;
using SpaceTraders.Infrastructure.Persistence.Scoping;
using SpaceTraders.Infrastructure.Persistence.Seed;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Clients;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Exceptions;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Accounts;
using Wolverine;

namespace SpaceTraders.API.Services;

/// <summary>
/// Bootstraps the SpaceTraders agent on startup by loading a stored or configured token,
/// or registering a new agent when none is available. Then it deletes the rows of every other
/// agent, such as the one from before a server reset (<see cref="AgentDataCleanup"/>).
/// </summary>
public sealed class AgentBootstrapService(
    IServiceScopeFactory serviceScopeFactory,
    IAgentTokenProvider agentTokenProvider,
    IAgentDataScope agentDataScope,
    IOptions<SpaceTradersBootstrapOptions> options,
    IAutomationMetrics metrics,
    ILogger<AgentBootstrapService> logger) : IHostedService
{
    private const string AgentTokenKey = "AgentToken";

    private readonly IServiceScopeFactory _serviceScopeFactory = serviceScopeFactory;
    private readonly IAgentTokenProvider _agentTokenProvider = agentTokenProvider;
    private readonly IAgentDataScope _agentDataScope = agentDataScope;
    private readonly SpaceTradersBootstrapOptions _options = options.Value;
    private readonly ILogger<AgentBootstrapService> _logger = logger;

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Read before any token is tried: a token that the server accepts after this was
        // registered in this reset.
        var resetDate = await GetServerResetDateAsync(cancellationToken);

        if (!await TryBootstrapWithKnownTokenAsync(resetDate, cancellationToken))
        {
            await RegisterNewAgentAsync(resetDate, cancellationToken);
        }

        await DeleteOtherAgentsAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task<bool> TryBootstrapWithKnownTokenAsync(string resetDate, CancellationToken cancellationToken)
    {
        var activeToken = await LoadActiveTokenAsync(cancellationToken);
        if (await TryBootstrapWithTokenAsync(activeToken, "active", resetDate, cancellationToken))
        {
            return true;
        }

        var configuredToken = _options.AgentToken ?? string.Empty;
        if (await TryBootstrapWithTokenAsync(configuredToken, "configured", resetDate, cancellationToken))
        {
            return true;
        }

        var storedToken = await LoadLatestStoredTokenAsync(cancellationToken);
        return await TryBootstrapWithTokenAsync(storedToken, "stored", resetDate, cancellationToken);
    }

    private async Task<bool> TryBootstrapWithTokenAsync(string token, string source, string resetDate, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var validation = await ValidateTokenAsync(token, cancellationToken);
        if (!validation.IsValid)
        {
            if (validation.IsResetMismatch)
            {
                _logger.LogWarning(
                    "{Source} agent token is no longer valid after a SpaceTraders reset; registering a new agent.",
                    source);

                await SetTokenResetMismatchRuntimeFlagAsync(cancellationToken);

                await using var scope = _serviceScopeFactory.CreateAsyncScope();
                var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
                await bus.PublishAsync(new TokenResetMismatchDetectedEvent(source));
                return false;
            }

            _logger.LogWarning(
                "{Source} agent token belongs to agent {ActualAgentName}, but SpaceTraders:AgentName is {ConfiguredAgentName}; registering a new agent.",
                source,
                validation.AgentSymbol,
                _options.AgentName);
            return false;
        }

        // A token keeps the id it was first stored under.
        var agentId = await FindAgentIdAsync(token, cancellationToken)
            ?? AgentIdentity.For(validation.AgentSymbol, resetDate);
        await BootstrapWithTokenAsync(agentId, token, cancellationToken);
        _logger.LogInformation("Loaded {Source} agent token for agent {AgentId}.", source, agentId);
        return true;
    }

    private async Task<TokenValidationResult> ValidateTokenAsync(string token, CancellationToken cancellationToken)
    {
        _agentTokenProvider.Set(token);

        await using var scope = _serviceScopeFactory.CreateAsyncScope();
        var apiClient = scope.ServiceProvider.GetRequiredService<ISpaceTradersApiClient>();

        try
        {
            var agent = await apiClient.GetMyAgentAsync(cancellationToken);
            var configuredAgentName = _options.AgentName ?? string.Empty;

            if (!string.IsNullOrWhiteSpace(configuredAgentName)
                && !string.Equals(agent.Symbol, configuredAgentName, StringComparison.OrdinalIgnoreCase))
            {
                return TokenValidationResult.AgentNameMismatch(agent.Symbol);
            }

            return TokenValidationResult.Valid(agent.Symbol);
        }
        catch (SpaceTradersApiException exception) when (exception.IsServerReset)
        {
            return TokenValidationResult.ResetMismatch();
        }
    }

    private async Task BootstrapWithTokenAsync(string agentId, string token, CancellationToken cancellationToken)
    {
        // Before the scope: its DbContext takes the agent from the data scope when it's created.
        _agentDataScope.Set(agentId);
        await using var scope = _serviceScopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<SpaceTradersDbContext>();
        var credential = await dbContext.Credentials.FindAsync([dbContext.AgentId, AgentTokenKey], cancellationToken);

        var credentialValues = new StoredCredential
        {
            AgentId = dbContext.AgentId,
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

        var settings = scope.ServiceProvider.GetRequiredService<ISettingsRepository>();
        await settings.SetAsync("Runtime.TokenResetMismatchDetected", "false", cancellationToken);
        await settings.SetAsync("Runtime.Alert.TokenResetMismatch", "false", cancellationToken);

        await AgentTokenSelection.SetActiveTokenAsync(dbContext, token, cancellationToken);
        _agentTokenProvider.Set(token);
    }

    private async Task<string> GetServerResetDateAsync(CancellationToken cancellationToken)
    {
        await using var scope = _serviceScopeFactory.CreateAsyncScope();
        var status = await scope.ServiceProvider.GetRequiredService<ISpaceTradersApiClient>().GetStatusAsync(cancellationToken);

        if (DateTimeOffset.TryParse(status.ServerResets?.Next, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var nextReset))
        {
            metrics.NextServerReset(nextReset);
        }

        return string.IsNullOrWhiteSpace(status.ResetDate)
            ? throw new InvalidOperationException("The SpaceTraders server status has no reset date.")
            : status.ResetDate;
    }

    private async Task<string?> FindAgentIdAsync(string token, CancellationToken cancellationToken)
    {
        await using var scope = _serviceScopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<SpaceTradersDbContext>();

        return await AgentTokenSelection.FindAgentIdAsync(dbContext, token, cancellationToken);
    }

    private async Task<bool> IsKnownAgentAsync(string agentId, CancellationToken cancellationToken)
    {
        await using var scope = _serviceScopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<SpaceTradersDbContext>();

        return await AgentTokenSelection.IsKnownAgentAsync(dbContext, agentId, cancellationToken);
    }

    private async Task DeleteOtherAgentsAsync(CancellationToken cancellationToken)
    {
        await using var scope = _serviceScopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<SpaceTradersDbContext>();

        await AgentDataCleanup.DeleteOtherAgentsAsync(dbContext, _logger, cancellationToken);
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

    private async Task SetTokenResetMismatchRuntimeFlagAsync(CancellationToken cancellationToken)
    {
        await using var scope = _serviceScopeFactory.CreateAsyncScope();
        var settings = scope.ServiceProvider.GetRequiredService<ISettingsRepository>();
        await settings.SetAsync("Runtime.TokenResetMismatchDetected", "true", cancellationToken);
        await settings.SetAsync("Runtime.Alert.TokenResetMismatch", "true", cancellationToken);
    }

    private async Task RegisterNewAgentAsync(string resetDate, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.AccountToken))
        {
            throw new InvalidOperationException("SpaceTraders:AccountToken must be configured when no stored agent token exists.");
        }

        if (string.IsNullOrWhiteSpace(_options.AgentName))
        {
            throw new InvalidOperationException("SpaceTraders:AgentName must be configured when no stored agent token exists.");
        }

        // An agent with this id is stored already. Registered under the same id, the new agent
        // would share the old agent's rows, and the cleanup would never remove them. This happens
        // when the server resets between reading its reset date and registering.
        var newAgentId = AgentIdentity.For(_options.AgentName, resetDate);
        if (await IsKnownAgentAsync(newAgentId, cancellationToken))
        {
            throw new InvalidOperationException(
                $"Not registering agent {newAgentId}: an agent with that id is stored already. The server's reset date ({resetDate}) is probably out of date; the next start reads it again.");
        }

        RegisterResponseData registration;
        await using (var apiScope = _serviceScopeFactory.CreateAsyncScope())
        {
            registration = await apiScope.ServiceProvider.GetRequiredService<ISpaceTradersApiClient>().RegisterAsync(
                new RegisterRequest
                {
                    Symbol = _options.AgentName,
                    Faction = _options.AgentFaction,
                },
                cancellationToken);
        }

        // Only now the scope for the new agent's rows: its DbContext takes the agent from the data
        // scope when it's created. Created any earlier (resolving the API client does that), it
        // stored the new agent's rows and settings under the previous agent.
        _agentDataScope.Set(AgentIdentity.For(registration.Agent.Symbol, resetDate));
        await using var scope = _serviceScopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<SpaceTradersDbContext>();

        var credential = await dbContext.Credentials.FindAsync([dbContext.AgentId, AgentTokenKey], cancellationToken);
        var credentialValues = new StoredCredential
        {
            AgentId = dbContext.AgentId,
            Key = AgentTokenKey,
            Value = registration.Token,
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

        var agent = await dbContext.Agents.FindAsync(new object[] { dbContext.AgentId, registration.Agent.Symbol }, cancellationToken);
        var agentValues = new CachedAgent
        {
            AgentId = dbContext.AgentId,
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
            var cachedShip = await dbContext.Ships.FindAsync(new object[] { dbContext.AgentId, ship.Symbol }, cancellationToken);
            if (cachedShip is null)
            {
                dbContext.Ships.Add(new CachedShip
                {
                    AgentId = dbContext.AgentId,
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

        var contract = await dbContext.Contracts.FindAsync(new object[] { dbContext.AgentId, registration.Contract.Id }, cancellationToken);
        var contractValues = new CachedContract
        {
            AgentId = dbContext.AgentId,
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

        var settings = scope.ServiceProvider.GetRequiredService<ISettingsRepository>();
        await settings.SetAsync("Runtime.TokenResetMismatchDetected", "false", cancellationToken);
        await settings.SetAsync("Runtime.Alert.TokenResetMismatch", "false", cancellationToken);

        await AgentTokenSelection.SetActiveTokenAsync(dbContext, registration.Token, cancellationToken);
        _agentTokenProvider.Set(registration.Token);

        _logger.LogInformation("Registered new SpaceTraders agent {AgentSymbol}.", registration.Agent.Symbol);
    }

    private readonly record struct TokenValidationResult(bool IsValid, bool IsResetMismatch, string AgentSymbol)
    {
        public static TokenValidationResult Valid(string agentSymbol) => new(true, false, agentSymbol);

        public static TokenValidationResult ResetMismatch() => new(false, true, string.Empty);

        public static TokenValidationResult AgentNameMismatch(string agentSymbol) => new(false, false, agentSymbol);
    }
}
