// IDisposable analyzer warnings are intentionally suppressed in this test file.
// The ServiceProvider is tracked by TestContext.Dispose().
#pragma warning disable IDISP001, IDISP004, IDISP005, IDISP007

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SpaceTraders.Application.Automation;
using SpaceTraders.Application.Interfaces.Repositories;

namespace SpaceTraders.Application.Tests.Automation;

public sealed class DataRetentionServiceTests
{
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static TestContext BuildService(
        IMarketPriceSampleRepository marketRepo,
        IAgentCreditsSampleRepository creditsRepo,
        ILedgerRepository ledgerRepo,
        IShipTaskRecordRepository shipTaskRepo)
    {
        var services = new ServiceCollection();
        services.AddSingleton(marketRepo);
        services.AddSingleton(creditsRepo);
        services.AddSingleton(ledgerRepo);
        services.AddSingleton(shipTaskRepo);

        var provider = services.BuildServiceProvider(validateScopes: false);
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
        var svc = new DataRetentionService(scopeFactory, NullLogger<DataRetentionService>.Instance);

        return new TestContext(provider, svc);
    }

    // -------------------------------------------------------------------------
    // Tests
    // -------------------------------------------------------------------------

    [Fact]
    public async Task PruneAllAsync_CallsAllFourRepositories()
    {
        var marketRepo = Substitute.For<IMarketPriceSampleRepository>();
        var creditsRepo = Substitute.For<IAgentCreditsSampleRepository>();
        var ledgerRepo = Substitute.For<ILedgerRepository>();
        var shipTaskRepo = Substitute.For<IShipTaskRecordRepository>();

        using var ctx = BuildService(marketRepo, creditsRepo, ledgerRepo, shipTaskRepo);

        await ctx.Service.PruneAllAsync(CancellationToken.None);

        await marketRepo.Received(1).PruneAsync(
            Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
        await creditsRepo.Received(1).PruneAsync(
            Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
        await ledgerRepo.Received(1).PruneAsync(
            Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
        await shipTaskRepo.Received(1).PruneAsync(
            Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PruneAllAsync_PassesCorrectCutoffsToMarketPriceSamples()
    {
        var marketRepo = Substitute.For<IMarketPriceSampleRepository>();
        var creditsRepo = Substitute.For<IAgentCreditsSampleRepository>();
        var ledgerRepo = Substitute.For<ILedgerRepository>();
        var shipTaskRepo = Substitute.For<IShipTaskRecordRepository>();

        using var ctx = BuildService(marketRepo, creditsRepo, ledgerRepo, shipTaskRepo);

        var before = DateTimeOffset.UtcNow;
        await ctx.Service.PruneAllAsync(CancellationToken.None);
        var after = DateTimeOffset.UtcNow;

        await marketRepo.Received(1).PruneAsync(
            Arg.Is<DateTimeOffset>(rawCutoff =>
                rawCutoff >= before.AddDays(-DataRetentionService.MarketSampleRawRetentionDays) &&
                rawCutoff <= after.AddDays(-DataRetentionService.MarketSampleRawRetentionDays)),
            Arg.Is<DateTimeOffset>(aggCutoff =>
                aggCutoff >= before.AddDays(-DataRetentionService.MarketSampleAggregateRetentionDays) &&
                aggCutoff <= after.AddDays(-DataRetentionService.MarketSampleAggregateRetentionDays)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PruneAllAsync_PassesCorrectCutoffsToAgentCreditsSamples()
    {
        var marketRepo = Substitute.For<IMarketPriceSampleRepository>();
        var creditsRepo = Substitute.For<IAgentCreditsSampleRepository>();
        var ledgerRepo = Substitute.For<ILedgerRepository>();
        var shipTaskRepo = Substitute.For<IShipTaskRecordRepository>();

        using var ctx = BuildService(marketRepo, creditsRepo, ledgerRepo, shipTaskRepo);

        var before = DateTimeOffset.UtcNow;
        await ctx.Service.PruneAllAsync(CancellationToken.None);
        var after = DateTimeOffset.UtcNow;

        await creditsRepo.Received(1).PruneAsync(
            Arg.Is<DateTimeOffset>(rawCutoff =>
                rawCutoff >= before.AddDays(-DataRetentionService.CreditSampleRawRetentionDays) &&
                rawCutoff <= after.AddDays(-DataRetentionService.CreditSampleRawRetentionDays)),
            Arg.Is<DateTimeOffset>(aggCutoff =>
                aggCutoff >= before.AddDays(-DataRetentionService.CreditSampleAggregateRetentionDays) &&
                aggCutoff <= after.AddDays(-DataRetentionService.CreditSampleAggregateRetentionDays)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PruneAllAsync_PassesCorrectCutoffToLedgerEntries()
    {
        var marketRepo = Substitute.For<IMarketPriceSampleRepository>();
        var creditsRepo = Substitute.For<IAgentCreditsSampleRepository>();
        var ledgerRepo = Substitute.For<ILedgerRepository>();
        var shipTaskRepo = Substitute.For<IShipTaskRecordRepository>();

        using var ctx = BuildService(marketRepo, creditsRepo, ledgerRepo, shipTaskRepo);

        var before = DateTimeOffset.UtcNow;
        await ctx.Service.PruneAllAsync(CancellationToken.None);
        var after = DateTimeOffset.UtcNow;

        await ledgerRepo.Received(1).PruneAsync(
            Arg.Is<DateTimeOffset>(cutoff =>
                cutoff >= before.AddDays(-DataRetentionService.LedgerEntryRetentionDays) &&
                cutoff <= after.AddDays(-DataRetentionService.LedgerEntryRetentionDays)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PruneAllAsync_PassesCorrectCutoffToShipTaskRecords()
    {
        var marketRepo = Substitute.For<IMarketPriceSampleRepository>();
        var creditsRepo = Substitute.For<IAgentCreditsSampleRepository>();
        var ledgerRepo = Substitute.For<ILedgerRepository>();
        var shipTaskRepo = Substitute.For<IShipTaskRecordRepository>();

        using var ctx = BuildService(marketRepo, creditsRepo, ledgerRepo, shipTaskRepo);

        var before = DateTimeOffset.UtcNow;
        await ctx.Service.PruneAllAsync(CancellationToken.None);
        var after = DateTimeOffset.UtcNow;

        await shipTaskRepo.Received(1).PruneAsync(
            Arg.Is<DateTimeOffset>(cutoff =>
                cutoff >= before.AddDays(-DataRetentionService.ShipTaskRecordRetentionDays) &&
                cutoff <= after.AddDays(-DataRetentionService.ShipTaskRecordRetentionDays)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PruneAllAsync_PropagatesException_WhenRepositoryThrows()
    {
        var marketRepo = Substitute.For<IMarketPriceSampleRepository>();
        marketRepo.PruneAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<int>(new InvalidOperationException("DB error")));

        var creditsRepo = Substitute.For<IAgentCreditsSampleRepository>();
        var ledgerRepo = Substitute.For<ILedgerRepository>();
        var shipTaskRepo = Substitute.For<IShipTaskRecordRepository>();

        using var ctx = BuildService(marketRepo, creditsRepo, ledgerRepo, shipTaskRepo);

        var act = async () => await ctx.Service.PruneAllAsync(CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("DB error");
    }

    // -------------------------------------------------------------------------
    // Test context
    // -------------------------------------------------------------------------

    private sealed class TestContext(ServiceProvider provider, DataRetentionService service) : IDisposable
    {
        public DataRetentionService Service { get; } = service;

        public void Dispose()
        {
            Service.Dispose();
            provider.Dispose();
        }
    }
}
