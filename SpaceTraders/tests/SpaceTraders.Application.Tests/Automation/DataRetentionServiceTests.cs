using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SpaceTraders.Application.Automation;
using SpaceTraders.Application.Interfaces;

namespace SpaceTraders.Application.Tests.Automation;

public sealed class DataRetentionServiceTests
{
    private readonly IDataRetention _retention = Substitute.For<IDataRetention>();

    public DataRetentionServiceTests()
    {
        _retention.PrunedTables.Returns(["first_table", "second_table", "third_table"]);
    }

    [Fact]
    public async Task PruneAllAsync_PrunesEveryTableThatHasRowsToPrune()
    {
        using var provider = BuildProvider();
        using var service = new DataRetentionService(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<DataRetentionService>.Instance);
        var before = TimeProvider.System.GetUtcNow();

        await service.PruneAllAsync(CancellationToken.None);

        foreach (var table in _retention.PrunedTables)
        {
            await _retention.Received(1).PruneAsync(table, Arg.Is<DateTimeOffset>(now => now >= before), Arg.Any<CancellationToken>());
        }
    }

    [Fact]
    public async Task PruneAllAsync_PrunesTheOtherTables_WhenOneFails()
    {
        // B3: one failing table stopped the tables after it.
        _retention.PruneAsync("first_table", Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<int>(new InvalidOperationException("DB error")));
        using var provider = BuildProvider();
        using var service = new DataRetentionService(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<DataRetentionService>.Instance);

        await service.PruneAllAsync(CancellationToken.None);

        await _retention.Received(1).PruneAsync("second_table", Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
        await _retention.Received(1).PruneAsync("third_table", Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    private ServiceProvider BuildProvider()
        => new ServiceCollection().AddScoped(_ => _retention).BuildServiceProvider();
}
