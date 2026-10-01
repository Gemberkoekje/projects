using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NSubstitute;
using SpaceTraders.Application.Automation;
using SpaceTraders.Application.EventHandlers;
using SpaceTraders.Application.Events.Handlers.Ships;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Events;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Clients;
using Wolverine.Runtime;

namespace SpaceTraders.API.Tests;

/// <summary>
/// Validates that the DI container can be fully built without any missing or
/// misconfigured registrations. The test will fail at host startup if any
/// required service is unregistered or a lifetime rule is violated.
/// </summary>
public sealed class DiValidationTests : IClassFixture<DiValidationFactory>
{
    private readonly DiValidationFactory _factory;

    public DiValidationTests(DiValidationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public void Host_BuildsSuccessfully_WithAllDependenciesRegistered()
    {
        // Accessing Services forces the host to build and validates the DI container.
        // ValidateOnBuild = true (set in DiValidationFactory) causes an exception here
        // if any dependency is missing or a scope violation exists.
        var act = () => _ = _factory.Services;
        act.Should().NotThrow("all dependencies must be registered and lifetime rules must be satisfied");
    }

    [Fact]
    public void PlanServices_AreNotMessageHandlers()
    {
        // The plan services have Handle methods, but Wolverine only picks up classes named *Handler or
        // *Consumer. If it did pick them up, a plan that is switched off could still run (and buy
        // ships) from an event.
        var runtime = (WolverineRuntime)_factory.Services.GetRequiredService<IWolverineRuntime>();
        var handlerTypes = runtime.Handlers.Chains
            .SelectMany(chain => chain.HandlerCalls())
            .Select(call => call.HandlerType)
            .ToList();

        handlerTypes.Should().Contain(typeof(ShipNavigationCompletedHandler));
        handlerTypes.Should().NotContain(typeof(ContractPlanService))
            .And.NotContain(typeof(MiningAutomationService))
            .And.NotContain(typeof(TradingAutomationService));
    }

    [Fact]
    public void CreditChanges_RaiseNoCreditDropAlert()
    {
        // D12: credits only drop when the bot spends them (ships, cargo, fuel), so a credit-drop
        // alert could only report the bot's own spending. It is gone (B37).
        var runtime = (WolverineRuntime)_factory.Services.GetRequiredService<IWolverineRuntime>();
        var chains = runtime.Handlers.Chains.SelectMany(chain => chain.ByEndpoint.Prepend(chain));

        var handlerTypes = chains
            .Where(chain => chain.MessageType == typeof(AgentCreditsChangedEvent))
            .SelectMany(chain => chain.HandlerCalls())
            .Select(call => call.HandlerType)
            .ToList();

        handlerTypes.Should().Contain(typeof(AgentCreditsSampleHandler))
            .And.NotContain(typeof(AlertHandler));
    }

    [Fact]
    public void HandledMessages_AreNotLoggedAtInformation()
    {
        // Wolverine logs every handled message ("Successfully processed message ...") under the
        // message type's name rather than its own, so the "Wolverine": "Warning" override in
        // appsettings doesn't reach it. At Information that is one line per message (B12).
        var runtime = (WolverineRuntime)_factory.Services.GetRequiredService<IWolverineRuntime>();

        runtime.Handlers.Chains.Should().NotBeEmpty()
            .And.OnlyContain(chain => chain.SuccessLogLevel < LogLevel.Information);
    }
}

/// <summary>
/// WebApplicationFactory variant that enables strict DI validation at build time.
/// All external dependencies are replaced with substitutes, identical to
/// <see cref="SpaceTradersApiFactory"/>, so the only failures come from missing
/// or misconfigured internal registrations.
/// </summary>
public sealed class DiValidationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.UseSetting("ConnectionStrings:DefaultConnection", "Host=localhost;Database=test;Username=test;Password=test");
        builder.UseSetting("SPACETRADERS_INTERNAL_API_KEY", "test-key-validation");
        builder.UseSetting("Metrics:Port", "0");

        // Fail immediately at host build if any registration is missing or has a
        // scope violation (singleton consuming scoped, etc.).
        builder.UseDefaultServiceProvider(options =>
        {
            options.ValidateOnBuild = true;
            options.ValidateScopes = true;
        });

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IAgentRepository>();
            services.AddScoped(_ => Substitute.For<IAgentRepository>());

            services.RemoveAll<IShipRepository>();
            services.AddScoped(_ => Substitute.For<IShipRepository>());

            services.RemoveAll<IContractRepository>();
            services.AddScoped(_ => Substitute.For<IContractRepository>());

            services.RemoveAll<IActivityLogRepository>();
            services.AddScoped(_ => Substitute.For<IActivityLogRepository>());

            services.RemoveAll<ISettingsRepository>();
            services.AddScoped(_ => Substitute.For<ISettingsRepository>());

            services.RemoveAll<ITradeOpportunityRepository>();
            services.AddScoped(_ => Substitute.For<ITradeOpportunityRepository>());

            services.RemoveAll<IShipAssignmentRepository>();
            services.AddScoped(_ => Substitute.For<IShipAssignmentRepository>());

            services.RemoveAll<IRateLimitStatus>();
            services.AddSingleton(Substitute.For<IRateLimitStatus>());

            services.RemoveAll<ILeaderElection>();
            services.AddSingleton(Substitute.For<ILeaderElection>());

            services.RemoveAll<ICreditHistoryService>();
            services.AddSingleton(Substitute.For<ICreditHistoryService>());

            services.RemoveAll<ILeaderLeaseRepository>();
            services.AddScoped(_ => Substitute.For<ILeaderLeaseRepository>());

            services.RemoveAll<ISpaceTradersApiClient>();
            services.AddSingleton(Substitute.For<ISpaceTradersApiClient>());

            services.RemoveAll<ISpaceTradersPort>();
            services.AddScoped(_ => Substitute.For<ISpaceTradersPort>());

            services.RemoveAll<IAgentTokenProvider>();
            services.AddSingleton(Substitute.For<IAgentTokenProvider>());

            services.RemoveAll<Application.Interfaces.IActiveRunIdProvider>();
            services.AddSingleton(Substitute.For<Application.Interfaces.IActiveRunIdProvider>());

            services.RemoveAll<Application.Interfaces.IRunLifecycleManager>();
            services.AddSingleton(Substitute.For<Application.Interfaces.IRunLifecycleManager>());

            services.RemoveAll<Application.Interfaces.Repositories.IRunRepository>();
            services.AddScoped(_ => Substitute.For<Application.Interfaces.Repositories.IRunRepository>());

            services.RemoveAll<Application.Interfaces.IDashboardNotifier>();
            services.AddSingleton(Substitute.For<Application.Interfaces.IDashboardNotifier>());

            // Remove app-level hosted services so they don't run during validation.
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
