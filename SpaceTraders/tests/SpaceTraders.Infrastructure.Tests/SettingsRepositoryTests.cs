using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SpaceTraders.Infrastructure.Persistence.Repositories;

namespace SpaceTraders.Infrastructure.Tests;

[Trait("Category", "Integration")]
public sealed class SettingsRepositoryTests : IntegrationTestBase
{
    [SkippableFact]
    public async Task SetAsync_String_GetAsync_ReturnsValue()
    {
        var repo = new SettingsRepository(Db, NullLogger<SettingsRepository>.Instance);
        await repo.SetAsync("MaxShips", "5");

        await using var fresh = CreateFreshContext();
        var result = await new SettingsRepository(fresh, NullLogger<SettingsRepository>.Instance)
            .GetAsync<string>("MaxShips");

        result.Should().Be("5");
    }

    [SkippableFact]
    public async Task SetAsync_Int_GetAsync_DeserializesCorrectly()
    {
        var repo = new SettingsRepository(Db, NullLogger<SettingsRepository>.Instance);
        await repo.SetAsync("MaxCargo", 40);

        await using var fresh = CreateFreshContext();
        var result = await new SettingsRepository(fresh, NullLogger<SettingsRepository>.Instance)
            .GetAsync<int>("MaxCargo");

        result.Should().Be(40);
    }

    [SkippableFact]
    public async Task SetAsync_UpdateExisting_OverwritesValue()
    {
        var repo = new SettingsRepository(Db, NullLogger<SettingsRepository>.Instance);
        await repo.SetAsync("TradeThreshold", "100");
        await repo.SetAsync("TradeThreshold", "200");

        await using var fresh = CreateFreshContext();
        var result = await new SettingsRepository(fresh, NullLogger<SettingsRepository>.Instance)
            .GetAsync<string>("TradeThreshold");

        result.Should().Be("200");
    }

    [SkippableFact]
    public async Task GetAsync_MissingKey_ReturnsDefault()
    {
        var repo = new SettingsRepository(Db, NullLogger<SettingsRepository>.Instance);
        var result = await repo.GetAsync<string>("NonExistentKey");
        result.Should().BeNull();
    }

    [SkippableFact]
    public async Task GetAllAsync_ReturnsAllPersistedSettings()
    {
        var repo = new SettingsRepository(Db, NullLogger<SettingsRepository>.Instance);
        await repo.SetAsync("KeyA", "valueA");
        await repo.SetAsync("KeyB", "valueB");

        await using var fresh = CreateFreshContext();
        var all = await new SettingsRepository(fresh, NullLogger<SettingsRepository>.Instance).GetAllAsync();

        all.Should().Contain(s => s.Key == "KeyA" && s.Value == "valueA");
        all.Should().Contain(s => s.Key == "KeyB" && s.Value == "valueB");
    }
}
