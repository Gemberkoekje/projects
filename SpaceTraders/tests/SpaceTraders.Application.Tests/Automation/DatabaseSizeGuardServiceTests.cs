using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SpaceTraders.Application.Automation;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Infrastructure.Persistence;
using SpaceTraders.Infrastructure.Persistence.Repositories;
using SpaceTraders.Infrastructure.Persistence.Seed;

namespace SpaceTraders.Application.Tests.Automation;

/// <summary>D8: the bot can't fill the shared Postgres volume.</summary>
public sealed class DatabaseSizeGuardServiceTests : IDisposable
{
    private const long Megabyte = 1024 * 1024;

    private readonly string _databaseName = Guid.NewGuid().ToString("N");
    private readonly IDatabaseSize _size = Substitute.For<IDatabaseSize>();
    private readonly IAutomationMetrics _metrics = Substitute.For<IAutomationMetrics>();
    private readonly ListLogger _logger = new();
    private readonly ServiceProvider _provider;
    private readonly DatabaseSizeGuardService _guard;

    public DatabaseSizeGuardServiceTests()
    {
        _provider = new ServiceCollection()
            .AddScoped(_ => TestDbContextFactory.Create(_databaseName))
            .AddScoped<ISettingsRepository>(services => new SettingsRepository(services.GetRequiredService<SpaceTradersDbContext>(), NullLogger<SettingsRepository>.Instance))
            .AddSingleton(_size)
            .BuildServiceProvider();
        _guard = new DatabaseSizeGuardService(_provider.GetRequiredService<IServiceScopeFactory>(), _metrics, _logger);
    }

    [Fact]
    public async Task AboveTheHardLimit_SwitchesAutomationOff()
    {
        await SeedDefaultSettingsAsync();
        SizeIs(3073 * Megabyte);

        await _guard.CheckAsync(CancellationToken.None);

        (await SettingAsync("Automation.Enabled")).Should().Be("false");
        _logger.Entries.Should().ContainSingle(entry => entry.Level == LogLevel.Error && entry.Message.StartsWith("DbSizeHardLimit", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AboveTheHardLimit_LeavesAutomationAlone_OnceItIsOff()
    {
        await SeedDefaultSettingsAsync();
        SizeIs(3073 * Megabyte);
        await _guard.CheckAsync(CancellationToken.None);

        await _guard.CheckAsync(CancellationToken.None);

        _logger.Entries.Count(entry => entry.Level == LogLevel.Error).Should().Be(1);
    }

    [Fact]
    public async Task AboveTheSoftLimit_WarnsOnce_AndLeavesAutomationOn()
    {
        await SeedDefaultSettingsAsync();
        SizeIs(1025 * Megabyte);

        await _guard.CheckAsync(CancellationToken.None);
        await _guard.CheckAsync(CancellationToken.None);

        (await SettingAsync("Automation.Enabled")).Should().Be("true");
        _logger.Entries.Should().ContainSingle(entry => entry.Level == LogLevel.Warning && entry.Message.StartsWith("DbSizeSoftLimit", StringComparison.Ordinal));
    }

    [Fact]
    public async Task UnderTheSoftLimit_ExportsTheSize_AndLogsNothing()
    {
        await SeedDefaultSettingsAsync();
        SizeIs(100 * Megabyte);

        await _guard.CheckAsync(CancellationToken.None);

        _metrics.Received(1).DatabaseSize(100 * Megabyte);
        (await SettingAsync("Automation.Enabled")).Should().Be("true");
        _logger.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task TheLimitsAreSettings()
    {
        await SeedDefaultSettingsAsync();
        await using (var scope = _provider.CreateAsyncScope())
        {
            var settings = scope.ServiceProvider.GetRequiredService<ISettingsRepository>();
            await settings.SetAsync(DatabaseSizeGuardService.SoftLimitSetting, "10");
            await settings.SetAsync(DatabaseSizeGuardService.HardLimitSetting, "20");
        }

        SizeIs(15 * Megabyte);
        await _guard.CheckAsync(CancellationToken.None);
        SizeIs(21 * Megabyte);
        await _guard.CheckAsync(CancellationToken.None);

        _logger.Entries.Select(entry => entry.Level).Should().Equal(LogLevel.Warning, LogLevel.Error);
        (await SettingAsync("Automation.Enabled")).Should().Be("false");
    }

    [Fact]
    public async Task StartAsync_ChecksBeforeTheRestOfStartupGoesOn()
    {
        // A database over the hard limit has automation off before the tick starts.
        await SeedDefaultSettingsAsync();
        SizeIs(3073 * Megabyte);

        await _guard.StartAsync(CancellationToken.None);

        (await SettingAsync("Automation.Enabled")).Should().Be("false");
        await _guard.StopAsync(CancellationToken.None);
    }

    public void Dispose()
    {
        _guard.Dispose();
        _provider.Dispose();
    }

    private void SizeIs(long bytes) => _size.GetBytesAsync(Arg.Any<CancellationToken>()).Returns(bytes);

    private async Task SeedDefaultSettingsAsync()
    {
        await using var db = TestDbContextFactory.Create(_databaseName);
        await DefaultSettingsSeed.SeedAsync(db);
    }

    private async Task<string?> SettingAsync(string key)
    {
        await using var scope = _provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ISettingsRepository>().GetRawAsync(key);
    }

    private sealed class ListLogger : ILogger<DatabaseSizeGuardService>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));
    }
}
