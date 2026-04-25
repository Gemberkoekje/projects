using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Clients;

namespace SpaceTraders.API.Tests;

/// <summary>
/// Integration tests for the SpaceTraders.API host using WebApplicationFactory.
/// Uses the "Testing" environment so that <see cref="Program"/> skips database init.
/// All external dependencies (DB repositories, SpaceTraders API) are replaced with fakes.
/// </summary>
public sealed class ApiIntegrationTests : IClassFixture<SpaceTradersApiFactory>
{
    private readonly HttpClient _client;
    private readonly HttpClient _clientWithKey;

    public ApiIntegrationTests(SpaceTradersApiFactory factory)
    {
        _client = factory.CreateClient();
        _clientWithKey = factory.CreateClient();
        _clientWithKey.DefaultRequestHeaders.Add("X-Api-Key", SpaceTradersApiFactory.TestApiKey);
    }

    // ── Health checks – no auth required ────────────────────────────────────

    [Fact]
    public async Task HealthLive_ReturnsOkWithoutApiKey()
    {
        var response = await _client.GetAsync("/health/live");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task HealthReady_DoesNotRequireApiKey()
    {
        // Health probes must never return 401 – whether they return 200 or 503
        // depends on DB connectivity which is not available in tests.
        var response = await _client.GetAsync("/health/ready");
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task HealthStartup_DoesNotRequireApiKey()
    {
        var response = await _client.GetAsync("/health/startup");
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }

    // ── API key auth ─────────────────────────────────────────────────────────

    [Fact]
    public async Task StatusAgent_WithoutApiKey_Returns401()
    {
        var response = await _client.GetAsync("/status/agent");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── Status endpoints ─────────────────────────────────────────────────────

    [Fact]
    public async Task StatusAgent_WithValidApiKey_Returns200OrNotFound()
    {
        var response = await _clientWithKey.GetAsync("/status/agent");
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task StatusShips_WithValidApiKey_Returns200()
    {
        var response = await _clientWithKey.GetAsync("/status/ships");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task StatusContracts_WithValidApiKey_Returns200()
    {
        var response = await _clientWithKey.GetAsync("/status/contracts");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── Settings endpoints ────────────────────────────────────────────────────

    [Fact]
    public async Task Settings_Get_WithValidApiKey_Returns200()
    {
        var response = await _clientWithKey.GetAsync("/settings/");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Settings_Put_WithValidApiKey_Returns200()
    {
        var response = await _clientWithKey.PutAsJsonAsync("/settings/Automation.Enabled", new { Value = "false" });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── Control endpoints ─────────────────────────────────────────────────────

    [Fact]
    public async Task Control_AutomationEnable_WithValidApiKey_Returns200()
    {
        var response = await _clientWithKey.PostAsync("/control/automation/enable", null);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Control_AutomationDisable_WithValidApiKey_Returns200()
    {
        var response = await _clientWithKey.PostAsync("/control/automation/disable", null);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}

/// <summary>
/// Custom WebApplicationFactory that:
/// <list type="bullet">
///   <item>Sets ASPNETCORE_ENVIRONMENT to "Testing" so <see cref="Program"/> skips
///         database initialisation (EnsureCreated / seed).</item>
///   <item>Replaces all repository interfaces with NSubstitute mocks.</item>
///   <item>Replaces the SpaceTraders API client/port with fakes.</item>
///   <item>Suppresses only the application-level background services,
///         leaving Wolverine's own infrastructure services intact.</item>
/// </list>
/// </summary>
public sealed class SpaceTradersApiFactory : WebApplicationFactory<Program>
{
    public const string TestApiKey = "test-key-12345";

    // Shared mocks – configure return values in tests as needed
    public ISettingsRepository SettingsRepository { get; } = Substitute.For<ISettingsRepository>();
    public IAgentRepository AgentRepository { get; } = Substitute.For<IAgentRepository>();
    public IShipRepository ShipRepository { get; } = Substitute.For<IShipRepository>();
    public IContractRepository ContractRepository { get; } = Substitute.For<IContractRepository>();
    public IActivityLogRepository ActivityLogRepository { get; } = Substitute.For<IActivityLogRepository>();
    public ITradeOpportunityRepository TradeOpportunityRepository { get; } = Substitute.For<ITradeOpportunityRepository>();
    public IShipAssignmentRepository ShipAssignmentRepository { get; } = Substitute.For<IShipAssignmentRepository>();
    public IRateLimitStatus RateLimitStatus { get; } = Substitute.For<IRateLimitStatus>();
    public ILeaderElection LeaderElection { get; } = Substitute.For<ILeaderElection>();
    public ICreditHistoryService CreditHistory { get; } = Substitute.For<ICreditHistoryService>();

    public SpaceTradersApiFactory()
    {
        // Default stub responses
        AgentRepository.GetAsync(default).ReturnsForAnyArgs((Application.Ports.AgentModel?)null);
        ShipRepository.GetAllAsync(default).ReturnsForAnyArgs(Array.Empty<Application.Ports.ShipModel>());
        ContractRepository.GetActiveAsync(default).ReturnsForAnyArgs(Array.Empty<ContractDto>());
        ActivityLogRepository.GetPagedAsync(1, 50, null, default).ReturnsForAnyArgs(Array.Empty<ActivityLogDto>());
        SettingsRepository.GetAllAsync(default).ReturnsForAnyArgs(Array.Empty<(string, string, string, string)>());
        TradeOpportunityRepository.GetBestRouteForCapacityAsync(default, default, default, default)
            .ReturnsForAnyArgs((TradeOpportunityDto?)null);
        LeaderElection.IsLeader.Returns(true);
        CreditHistory.GetHistory().Returns(Array.Empty<(DateTimeOffset, long)>());
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.UseSetting("ConnectionStrings:DefaultConnection", "Host=localhost;Database=test;Username=test;Password=test");
        builder.UseSetting("SPACETRADERS_INTERNAL_API_KEY", TestApiKey);

        builder.ConfigureTestServices(services =>
        {
            // ── Replace repository implementations with mocks ─────────────────
            services.RemoveAll<IAgentRepository>();
            services.AddScoped(_ => AgentRepository);

            services.RemoveAll<IShipRepository>();
            services.AddScoped(_ => ShipRepository);

            services.RemoveAll<IContractRepository>();
            services.AddScoped(_ => ContractRepository);

            services.RemoveAll<IActivityLogRepository>();
            services.AddScoped(_ => ActivityLogRepository);

            services.RemoveAll<ISettingsRepository>();
            services.AddScoped(_ => SettingsRepository);

            services.RemoveAll<ITradeOpportunityRepository>();
            services.AddScoped(_ => TradeOpportunityRepository);

            services.RemoveAll<IShipAssignmentRepository>();
            services.AddScoped(_ => ShipAssignmentRepository);

            services.RemoveAll<IRateLimitStatus>();
            services.AddSingleton(RateLimitStatus);

            services.RemoveAll<ILeaderElection>();
            services.AddSingleton(LeaderElection);

            services.RemoveAll<ICreditHistoryService>();
            services.AddSingleton(CreditHistory);

            services.RemoveAll<ILeaderLeaseRepository>();
            services.AddScoped(_ => Substitute.For<ILeaderLeaseRepository>());

            // ── SpaceTraders API client / port ────────────────────────────────
            services.RemoveAll<ISpaceTradersApiClient>();
            services.AddSingleton(Substitute.For<ISpaceTradersApiClient>());

            services.RemoveAll<ISpaceTradersPort>();
            services.AddScoped(_ => Substitute.For<ISpaceTradersPort>());

            services.RemoveAll<IAgentTokenProvider>();
            services.AddSingleton(Substitute.For<IAgentTokenProvider>());

            // ── Suppress only application-level background services ───────────
            // (Wolverine's own IHostedService is kept so the bus starts correctly)
            var appServices = services
                .Where(d =>
                    d.ServiceType == typeof(IHostedService) &&
                    d.ImplementationType?.Namespace?.StartsWith("SpaceTraders", StringComparison.Ordinal) == true)
                .ToList();

            foreach (var svc in appServices)
                services.Remove(svc);
        });
    }
}
