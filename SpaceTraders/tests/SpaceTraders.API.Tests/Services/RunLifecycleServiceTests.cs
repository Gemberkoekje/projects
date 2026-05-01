// IDisposable analyzer warnings are intentionally suppressed in this test file.
// The ServiceProvider and related disposable test infrastructure are tracked by TestContext.Dispose().
#pragma warning disable IDISP001, IDISP004, IDISP005, IDISP007

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SpaceTraders.API.Services;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Services;

namespace SpaceTraders.API.Tests.Services;

public sealed class RunLifecycleServiceTests
{
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Builds a <see cref="RunLifecycleService"/> wired to in-memory substitutes.
    /// The returned <see cref="TestContext"/> owns the <see cref="ServiceProvider"/>
    /// and must be disposed at the end of each test.
    /// </summary>
    private static TestContext BuildService(
        IRunRepository runRepo,
        IAgentRepository agentRepo,
        ISettingsRepository settingsRepo)
    {
        var services = new ServiceCollection();
        services.AddSingleton(runRepo);
        services.AddSingleton(agentRepo);
        services.AddSingleton(settingsRepo);

        var provider = services.BuildServiceProvider(validateScopes: false);
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
        var runIdProvider = new ActiveRunIdProvider();
        var svc = new RunLifecycleService(scopeFactory, runIdProvider, NullLogger<RunLifecycleService>.Instance);

        return new TestContext(provider, svc, runIdProvider);
    }

    // -------------------------------------------------------------------------
    // Tests
    // -------------------------------------------------------------------------

    [Fact]
    public async Task StartAsync_WhenNoActiveRun_OpensNewRun()
    {
        var runRepo = Substitute.For<IRunRepository>();
        var newRunId = Guid.NewGuid();
        runRepo.GetPendingScheduledRunsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<PendingScheduledRunInfo>());
        runRepo.GetActiveRunAsync(Arg.Any<CancellationToken>()).Returns((ActiveRunInfo?)null);
        runRepo.GetRunCountAsync(Arg.Any<CancellationToken>()).Returns(0);
        runRepo.OpenRunAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<long>(),
                Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(newRunId);

        var agentRepo = Substitute.For<IAgentRepository>();
        agentRepo.GetAsync(Arg.Any<CancellationToken>())
            .Returns(new AgentModel("AGENT", null, null, 50_000L, "COSMIC", 3));

        var settingsRepo = Substitute.For<ISettingsRepository>();
        settingsRepo.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(new List<(string Key, string Value, string Type, string Description)>());

        using var ctx = BuildService(runRepo, agentRepo, settingsRepo);
        await ctx.Service.StartAsync(CancellationToken.None);
        await ctx.Service.StopAsync(CancellationToken.None);

        await runRepo.Received(1).OpenRunAsync(
            Arg.Any<string>(), Arg.Any<string>(), 50_000L,
            Arg.Any<string?>(), Arg.Any<CancellationToken>());
        ctx.RunIdProvider.ActiveRunId.Should().Be(newRunId);
    }

    [Fact]
    public async Task StartAsync_WhenActiveRunExists_ResumesExistingRun()
    {
        var existingRunId = Guid.NewGuid();
        var existingRun = new ActiveRunInfo(existingRunId, "Run 1", "default", DateTimeOffset.UtcNow, 100_000L);

        var runRepo = Substitute.For<IRunRepository>();
        runRepo.GetPendingScheduledRunsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<PendingScheduledRunInfo>());
        runRepo.GetActiveRunAsync(Arg.Any<CancellationToken>()).Returns(existingRun);

        var agentRepo = Substitute.For<IAgentRepository>();
        var settingsRepo = Substitute.For<ISettingsRepository>();

        using var ctx = BuildService(runRepo, agentRepo, settingsRepo);
        await ctx.Service.StartAsync(CancellationToken.None);
        await ctx.Service.StopAsync(CancellationToken.None);

        await runRepo.DidNotReceive().OpenRunAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<long>(),
            Arg.Any<string?>(), Arg.Any<CancellationToken>());
        ctx.RunIdProvider.ActiveRunId.Should().Be(existingRunId);
    }

    [Fact]
    public async Task StartAsync_WithRestartPendingScheduledRun_PromotesScheduledRun()
    {
        var scheduledRunId = Guid.NewGuid();
        var pending = new PendingScheduledRunInfo(
            scheduledRunId, "Scheduled Run 1", "mining-0.5",
            null, null, true);

        var existingRunId = Guid.NewGuid();
        var existingRun = new ActiveRunInfo(existingRunId, "Run 1", "default", DateTimeOffset.UtcNow.AddHours(-1), 100_000L);
        var newRunId = Guid.NewGuid();

        var runRepo = Substitute.For<IRunRepository>();
        runRepo.GetPendingScheduledRunsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new[] { pending });
        runRepo.GetActiveRunAsync(Arg.Any<CancellationToken>()).Returns(existingRun);
        runRepo.GetRunCountAsync(Arg.Any<CancellationToken>()).Returns(1);
        runRepo.OpenRunAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<long>(),
                Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(newRunId);

        var agentRepo = Substitute.For<IAgentRepository>();
        agentRepo.GetAsync(Arg.Any<CancellationToken>())
            .Returns(new AgentModel("AGENT", null, null, 150_000L, "COSMIC", 5));

        var settingsRepo = Substitute.For<ISettingsRepository>();
        settingsRepo.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(new List<(string Key, string Value, string Type, string Description)>());

        using var ctx = BuildService(runRepo, agentRepo, settingsRepo);
        await ctx.Service.StartAsync(CancellationToken.None);
        await ctx.Service.StopAsync(CancellationToken.None);

        await runRepo.Received(1).CloseRunAsync(existingRunId, 150_000L, Arg.Any<CancellationToken>());
        await runRepo.Received(1).OpenRunAsync(
            "Scheduled Run 1", "mining-0.5", 150_000L,
            Arg.Any<string?>(), Arg.Any<CancellationToken>());
        await runRepo.Received(1).DeletePromotedScheduledRunAsync(scheduledRunId, Arg.Any<CancellationToken>());
        ctx.RunIdProvider.ActiveRunId.Should().Be(newRunId);
    }

    [Fact]
    public async Task RotateForSettingsChange_WithStrategyRelevantKey_ClosesAndReopensRun()
    {
        var existingRunId = Guid.NewGuid();
        var existingRun = new ActiveRunInfo(existingRunId, "Run 1", "default", DateTimeOffset.UtcNow.AddHours(-2), 100_000L);
        var newRunId = Guid.NewGuid();

        var runRepo = Substitute.For<IRunRepository>();
        runRepo.GetPendingScheduledRunsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<PendingScheduledRunInfo>());
        runRepo.GetActiveRunAsync(Arg.Any<CancellationToken>()).Returns(existingRun);
        runRepo.GetRunCountAsync(Arg.Any<CancellationToken>()).Returns(1);
        runRepo.OpenRunAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<long>(),
                Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(newRunId);

        var agentRepo = Substitute.For<IAgentRepository>();
        agentRepo.GetAsync(Arg.Any<CancellationToken>())
            .Returns(new AgentModel("AGENT", null, null, 200_000L, "COSMIC", 6));

        var settingsRepo = Substitute.For<ISettingsRepository>();
        settingsRepo.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(new List<(string Key, string Value, string Type, string Description)>());

        using var ctx = BuildService(runRepo, agentRepo, settingsRepo);
        await ctx.Service.StartAsync(CancellationToken.None);
        ctx.RunIdProvider.ActiveRunId.Should().Be(existingRunId, "startup resumes existing run");

        await ctx.Service.RotateForSettingsChangeAsync("Trade.MinProfitPerUnit", CancellationToken.None);
        await ctx.Service.StopAsync(CancellationToken.None);

        await runRepo.Received(1).CloseRunAsync(existingRunId, Arg.Any<long>(), Arg.Any<CancellationToken>());
        await runRepo.Received(1).OpenRunAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<long>(),
            Arg.Any<string?>(), Arg.Any<CancellationToken>());
        ctx.RunIdProvider.ActiveRunId.Should().Be(newRunId);
    }

    [Fact]
    public async Task RotateForSettingsChange_WithRuntimeKey_DoesNotRotate()
    {
        var existingRunId = Guid.NewGuid();
        var existingRun = new ActiveRunInfo(existingRunId, "Run 1", "default", DateTimeOffset.UtcNow, 100_000L);

        var runRepo = Substitute.For<IRunRepository>();
        runRepo.GetPendingScheduledRunsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<PendingScheduledRunInfo>());
        runRepo.GetActiveRunAsync(Arg.Any<CancellationToken>()).Returns(existingRun);
        runRepo.GetRunCountAsync(Arg.Any<CancellationToken>()).Returns(1);
        runRepo.OpenRunAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<long>(),
                Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Guid.NewGuid());

        var agentRepo = Substitute.For<IAgentRepository>();
        agentRepo.GetAsync(Arg.Any<CancellationToken>())
            .Returns(new AgentModel("AGENT", null, null, 100_000L, "COSMIC", 3));

        var settingsRepo = Substitute.For<ISettingsRepository>();
        settingsRepo.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(new List<(string Key, string Value, string Type, string Description)>());

        using var ctx = BuildService(runRepo, agentRepo, settingsRepo);
        await ctx.Service.StartAsync(CancellationToken.None);

        await ctx.Service.RotateForSettingsChangeAsync("Runtime.Alert.ApiUnavailable", CancellationToken.None);
        await ctx.Service.StopAsync(CancellationToken.None);

        await runRepo.DidNotReceive().CloseRunAsync(
            Arg.Any<Guid>(), Arg.Any<long>(), Arg.Any<CancellationToken>());
        ctx.RunIdProvider.ActiveRunId.Should().Be(existingRunId);
    }

    // -------------------------------------------------------------------------
    // Test context: owns the ServiceProvider lifetime
    // -------------------------------------------------------------------------

    private sealed class TestContext(ServiceProvider provider, RunLifecycleService service, ActiveRunIdProvider runIdProvider)
        : IDisposable
    {
        public RunLifecycleService Service { get; } = service;
        public ActiveRunIdProvider RunIdProvider { get; } = runIdProvider;

        public void Dispose() => provider.Dispose();
    }
}
