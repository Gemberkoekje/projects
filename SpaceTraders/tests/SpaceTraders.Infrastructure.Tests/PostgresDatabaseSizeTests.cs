using FluentAssertions;
using SpaceTraders.Infrastructure.Persistence;

namespace SpaceTraders.Infrastructure.Tests;

[Trait("Category", "Integration")]
public sealed class PostgresDatabaseSizeTests : IntegrationTestBase
{
    [SkippableFact]
    public async Task GetBytesAsync_ReadsTheDatabaseSize()
    {
        var bytes = await new PostgresDatabaseSize(Db).GetBytesAsync();

        // An empty Postgres database with the schema is a few megabytes.
        bytes.Should().BeInRange(1024 * 1024, 100L * 1024 * 1024);
    }
}
