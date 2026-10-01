using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SpaceTraders.API.Services;

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
}
