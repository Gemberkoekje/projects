using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SpaceTraders.API.Configuration;
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
        if (await TryBootstrapWithTokenAsync(activeToken, "active", cancellationToken))
        {
            return;
        }

        var configuredToken = _options.AgentToken ?? string.Empty;
        if (await TryBootstrapWithTokenAsync(configuredToken, "configured", cancellationToken))
        {
            return;
        }

        var storedToken = await LoadLatestStoredTokenAsync(cancellationToken);
        if (await TryBootstrapWithTokenAsync(storedToken, "stored", cancellationToken))
        {
            return;
        }

        await RegisterNewAgentAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task<bool> TryBootstrapWithTokenAsync(string token, string source, CancellationToken cancellationToken)
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

        await BootstrapWithTokenAsync(token, cancellationToken);
        _logger.LogInformation("Loaded {Source} agent token for agent {AgentSymbol}.", source, validation.AgentSymbol);
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
        catch (SpaceTradersApiException exception) when (IsTokenResetDateMismatch(exception))
        {
            return TokenValidationResult.ResetMismatch();
        }
    }

    private static bool IsTokenResetDateMismatch(SpaceTradersApiException exception)
        => exception.StatusCode == HttpStatusCode.Unauthorized
            && exception.Message.Contains("Token reset_date does not match the server", StringComparison.OrdinalIgnoreCase);

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

        var settings = scope.ServiceProvider.GetRequiredService<ISettingsRepository>();
        await settings.SetAsync("Runtime.TokenResetMismatchDetected", "false", cancellationToken);
        await settings.SetAsync("Runtime.Alert.TokenResetMismatch", "false", cancellationToken);

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

    private async Task SetTokenResetMismatchRuntimeFlagAsync(CancellationToken cancellationToken)
    {
        await using var scope = _serviceScopeFactory.CreateAsyncScope();
        var settings = scope.ServiceProvider.GetRequiredService<ISettingsRepository>();
        await settings.SetAsync("Runtime.TokenResetMismatchDetected", "true", cancellationToken);
        await settings.SetAsync("Runtime.Alert.TokenResetMismatch", "true", cancellationToken);
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

        var credential = await dbContext.Credentials.FindAsync([dbContext.AgentToken, AgentTokenKey], cancellationToken);
        var credentialValues = new StoredCredential
        {
            AgentToken = dbContext.AgentToken,
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
