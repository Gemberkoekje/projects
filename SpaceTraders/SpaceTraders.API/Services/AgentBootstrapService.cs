using System.Globalization;
using System.Net.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SpaceTraders.API.Configuration;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Services;
using SpaceTraders.Domain.Events;
using SpaceTraders.Infrastructure.Persistence;
using SpaceTraders.Infrastructure.Persistence.Entities;
using SpaceTraders.Infrastructure.Persistence.Scoping;
using SpaceTraders.Infrastructure.Persistence.Seed;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Clients;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Exceptions;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Accounts;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Status;
using SpaceTraders.Infrastructure.SpaceTradersAPI.RateLimiting;
using Wolverine;

namespace SpaceTraders.API.Services;

/// <summary>
/// Bootstraps the SpaceTraders agent on startup by loading a stored or configured token,
/// or registering a new agent when none is available. Then it deletes the rows of every other
/// agent, such as the one from before a server reset (<see cref="AgentDataCleanup"/>).
/// </summary>
/// <remarks>
/// First it waits for the server: while it resets it answers 503 for some minutes, and a start
/// that failed on that stopped the host, five times in a row on 2026-11-15, each a Fatal line and a
/// longer back-off before the next (B93). It asks again every <see cref="ServerWaitInterval"/>,
/// with one warning for the wait; Kubernetes' startup probe gives a start ten minutes.
/// </remarks>
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

    /// <summary>How long startup waits before it asks a server that didn't answer again (B93).</summary>
    internal TimeSpan ServerWaitInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Read before any token is tried: a token that the server accepts after this was
        // registered in this reset.
        var (resetDate, nextReset) = await GetServerResetDateAsync(cancellationToken);

        if (!await TryBootstrapWithKnownTokenAsync(resetDate, cancellationToken))
        {
            await RegisterNewAgentAsync(resetDate, cancellationToken);
        }

        // Once the agent is known, so the gauge carries its reset date as every metric does (slice 2.13), and the
        // setting is the agent's: the purchases are judged by it (slice 6.70, D150), and the dashboard shows it.
        if (nextReset is { } next)
        {
            metrics.NextServerReset(next);
            await using var scope = _serviceScopeFactory.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<ISettingsRepository>()
                .SetAsync(ServerReset.Setting, next.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture), cancellationToken);
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
        await DefaultSettingsSeed.SeedAsync(dbContext, _logger, cancellationToken);

        var settings = scope.ServiceProvider.GetRequiredService<ISettingsRepository>();
        await settings.SetAsync("Runtime.TokenResetMismatchDetected", "false", cancellationToken);
        await settings.SetAsync("Runtime.Alert.TokenResetMismatch", "false", cancellationToken);

        await AgentTokenSelection.SetActiveTokenAsync(dbContext, token, cancellationToken);
        _agentTokenProvider.Set(token);
    }

    private async Task<(string ResetDate, DateTimeOffset? NextReset)> GetServerResetDateAsync(CancellationToken cancellationToken)
    {
        var status = await WaitForServerStatusAsync(cancellationToken);

        DateTimeOffset? nextReset = DateTimeOffset.TryParse(status.ServerResets?.Next, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var next)
            ? next
            : null;

        return string.IsNullOrWhiteSpace(status.ResetDate)
            ? throw new InvalidOperationException("The SpaceTraders server status has no reset date.")
            : (status.ResetDate, nextReset);
    }

    /// <summary>
    /// The server's status, asked again every <see cref="ServerWaitInterval"/> while the server
    /// doesn't answer (<see cref="IsServerAway"/>), with one warning for the wait (B93).
    /// </summary>
    private async Task<ServerStatus> WaitForServerStatusAsync(CancellationToken cancellationToken)
    {
        var since = TimeProvider.System.GetUtcNow();
        var attempts = 0;
        while (true)
        {
            attempts++;
            string awayBecause;
            try
            {
                await using var scope = _serviceScopeFactory.CreateAsyncScope();
                var status = await scope.ServiceProvider.GetRequiredService<ISpaceTradersApiClient>().GetStatusAsync(cancellationToken);
                if (attempts > 1)
                {
                    _logger.LogInformation(
                        "The SpaceTraders server answers again, after {Minutes:F1} minutes and {Attempts} attempts; startup goes on.",
                        (TimeProvider.System.GetUtcNow() - since).TotalMinutes,
                        attempts);
                }

                return status;
            }
            catch (Exception exception) when (IsServerAway(exception, cancellationToken))
            {
                // Its message only: the exception says no more, and a reset's would be left out of the log.
                awayBecause = exception.Message;
            }

            if (attempts == 1)
            {
                _logger.LogWarning(
                    "The SpaceTraders server doesn't answer ({Detail}); startup asks again every {Seconds} seconds until it does.",
                    awayBecause,
                    ServerWaitInterval.TotalSeconds);
            }
            else
            {
                _logger.LogDebug("The SpaceTraders server still doesn't answer ({Detail}), attempt {Attempt}.", awayBecause, attempts);
            }

            await Task.Delay(ServerWaitInterval, cancellationToken);
        }
    }

    /// <summary>
    /// Whether <paramref name="exception"/> says the server is away for now: a 5xx answer (503 while
    /// it resets, 502 from its DDoS protection), a pause after a 502, no connection, or a timeout.
    /// </summary>
    private static bool IsServerAway(Exception exception, CancellationToken cancellationToken) => exception switch
    {
        SpaceTradersApiException api => (int)api.StatusCode >= 500,
        ApiPausedException or HttpRequestException => true,
        TaskCanceledException => !cancellationToken.IsCancellationRequested,
        _ => false,
    };

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
        await DefaultSettingsSeed.SeedAsync(dbContext, _logger, cancellationToken);

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
