using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Clients;

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
