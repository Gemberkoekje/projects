using Microsoft.EntityFrameworkCore;
using SpaceTraders.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace SpaceTraders.Infrastructure.Tests;

/// <summary>
/// Base class that starts a real PostgreSQL container per test class and
/// provides a freshly-migrated <see cref="SpaceTradersDbContext"/> for each test.
/// </summary>
public abstract class IntegrationTestBase : IAsyncLifetime
{
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder("postgres:16-alpine").Build();

    protected SpaceTradersDbContext Db { get; private set; } = default!;

    public async Task InitializeAsync()
    {
        Skip.IfNot(IsDockerAvailable(), "Docker is not available – skipping integration tests.");
        await _pg.StartAsync();
        Db = BuildContext(_pg.GetConnectionString());
        await Db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await Db.DisposeAsync();
        await _pg.DisposeAsync();
    }

    /// <summary>Creates a new <see cref="SpaceTradersDbContext"/> pointing at the same container.</summary>
    protected SpaceTradersDbContext CreateFreshContext() => BuildContext(_pg.GetConnectionString());

    private static SpaceTradersDbContext BuildContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<SpaceTradersDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        return new SpaceTradersDbContext(options);
    }

    private static bool IsDockerAvailable()
    {
        try
        {
            using var client = new System.Net.Http.HttpClient();
            client.Timeout = TimeSpan.FromSeconds(2);
            // A simple connectivity check: if the Docker socket exists we can proceed.
            return System.IO.File.Exists("/var/run/docker.sock")
                || System.Environment.GetEnvironmentVariable("DOCKER_HOST") is not null;
        }
        catch
        {
            return false;
        }
    }
}
