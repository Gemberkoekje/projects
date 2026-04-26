using Microsoft.EntityFrameworkCore;
using SpaceTraders.Infrastructure.Persistence;
using SpaceTraders.Infrastructure.Persistence.Scoping;
using Testcontainers.PostgreSql;

namespace SpaceTraders.Infrastructure.Tests;

/// <summary>
/// Base class that starts a real PostgreSQL container per test class and
/// provides a freshly-migrated <see cref="SpaceTradersDbContext"/> for each test.
/// </summary>
[Mutable]
public abstract class IntegrationTestBase : IAsyncLifetime, IAsyncDisposable
{
    private PostgreSqlContainer _pg = default!;
    private bool _started;

    protected SpaceTradersDbContext Db { get; private set; } = default!;

    public async Task InitializeAsync()
    {
        Skip.IfNot(IsDockerAvailable(), "Docker is not available – skipping integration tests.");

        if (_started)
        {
            throw new InvalidOperationException("The integration test database is already started.");
        }

        if (Db != default)
        {
            await Db.DisposeAsync();
        }

        _pg = new PostgreSqlBuilder("postgres:16-alpine").Build();
        await _pg.StartAsync();
        Db = BuildContext(_pg.GetConnectionString());
        await Db.Database.EnsureCreatedAsync();
        _started = true;
    }

    public Task DisposeAsync() => DisposeAsyncCore().AsTask();

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await DisposeAsyncCore();
        GC.SuppressFinalize(this);
    }

    protected virtual async ValueTask DisposeAsyncCore()
    {
        await DisposeResourcesAsync();
    }

    private async Task DisposeResourcesAsync()
    {
        if (!_started)
        {
            return;
        }

        await Db.DisposeAsync();
        Db = default!;
        await _pg.DisposeAsync();
        _started = false;
    }

    /// <summary>Creates a new <see cref="SpaceTradersDbContext"/> pointing at the same container.</summary>
    protected SpaceTradersDbContext CreateFreshContext()
    {
        if (!_started)
        {
            throw new InvalidOperationException("The integration test database has not been started.");
        }

        return BuildContext(_pg.GetConnectionString());
    }

    private static SpaceTradersDbContext BuildContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<SpaceTradersDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        var scope = new AgentDataScope();
        scope.Set("integration-test-token");
        return new SpaceTradersDbContext(options, scope);
    }

    private static bool IsDockerAvailable()
    {
        if (System.OperatingSystem.IsWindows())
        {
            try
            {
                using var pipe = new System.IO.Pipes.NamedPipeClientStream(".", "docker_engine", System.IO.Pipes.PipeDirection.InOut);
                pipe.Connect(200);
                return pipe.IsConnected;
            }
            catch
            {
                return false;
            }
        }

        return System.IO.File.Exists("/var/run/docker.sock")
            || !string.IsNullOrWhiteSpace(System.Environment.GetEnvironmentVariable("DOCKER_HOST"));
    }
}
