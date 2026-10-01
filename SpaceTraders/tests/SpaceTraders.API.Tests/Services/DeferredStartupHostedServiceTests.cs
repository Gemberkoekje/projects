using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SpaceTraders.API.Services;
using SpaceTraders.Application.Automation;

namespace SpaceTraders.API.Tests.Services;

public sealed class DeferredStartupHostedServiceTests
{
    [Fact]
    public async Task AThrowingStartupStep_StopsTheHost()
    {
        // Agent bootstrap is the first step outside the database work, which "Testing" skips.
        using var services = new ServiceCollection()
            .AddSingleton<AgentBootstrapService>(_ => throw new InvalidOperationException("Agent bootstrap failed."))
            .BuildServiceProvider();
        using var started = new CancellationTokenSource();
        var lifetime = Substitute.For<IHostApplicationLifetime>();
        lifetime.ApplicationStarted.Returns(started.Token);
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns("Testing");
        var state = new StartupInitializationState();

        var sut = new DeferredStartupHostedService(services, lifetime, environment, state, NullLogger<DeferredStartupHostedService>.Instance);
        await sut.StartAsync(CancellationToken.None);
        await started.CancelAsync();
        await sut.StopAsync(CancellationToken.None);

        state.HasFailed.Should().BeTrue();
        lifetime.Received(1).StopApplication();
    }

    [Fact]
    public async Task DataRetention_IsStarted_EvenWhenAgentBootstrapFails()
    {
        // B3: pruning started only after every other startup step had succeeded.
        using var services = new ServiceCollection()
            .AddSingleton<AgentBootstrapService>(_ => throw new InvalidOperationException("Agent bootstrap failed."))
            .AddSingleton(provider => new DataRetentionService(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<DataRetentionService>.Instance))
            .BuildServiceProvider();
        using var started = new CancellationTokenSource();
        var lifetime = Substitute.For<IHostApplicationLifetime>();
        lifetime.ApplicationStarted.Returns(started.Token);
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns("Testing");

        var sut = new DeferredStartupHostedService(services, lifetime, environment, new StartupInitializationState(), NullLogger<DeferredStartupHostedService>.Instance);
        await sut.StartAsync(CancellationToken.None);
        await started.CancelAsync();
        await sut.StopAsync(CancellationToken.None);

        services.GetRequiredService<DataRetentionService>().ExecuteTask.Should().NotBeNull();
    }
}
