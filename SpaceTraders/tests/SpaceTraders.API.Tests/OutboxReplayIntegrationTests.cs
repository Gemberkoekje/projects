using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Testcontainers.PostgreSql;
using Wolverine;
using Wolverine.Postgresql;

namespace SpaceTraders.API.Tests;

[Trait("Category", "Integration")]
public sealed class OutboxReplayIntegrationTests : IAsyncLifetime
{
    private PostgreSqlContainer _pg = default!;
    private bool _started;

    public async Task InitializeAsync()
    {
        Skip.IfNot(IsDockerAvailable(), "Docker is not available – skipping integration tests.");

        _pg = new PostgreSqlBuilder("postgres:16-alpine").Build();
        await _pg.StartAsync();
        _started = true;
    }

    public Task DisposeAsync() => DisposeAsyncCore().AsTask();

    [SkippableFact]
    public async Task DurableScheduledLocalMessage_ReplaysAfterHostRestart()
    {
        ReplayProbeHandler.Reset();
        var connectionString = _pg.GetConnectionString();
        var dueAt = DateTimeOffset.UtcNow.AddSeconds(2);

        using (var firstHost = BuildHost(connectionString))
        {
            await firstHost.StartAsync();
            var bus = firstHost.Services.GetRequiredService<IMessageBus>();
            await bus.ScheduleAsync(new ReplayProbeMessage("SHIP-1"), dueAt);
            await firstHost.StopAsync();
        }

        await Task.Delay(TimeSpan.FromSeconds(3));

        using (var secondHost = BuildHost(connectionString))
        {
            await secondHost.StartAsync();
            var replayed = await WaitForReplayAsync(TimeSpan.FromSeconds(15));
            await secondHost.StopAsync();

            replayed.Should().BeTrue("the durable scheduled local envelope should be recovered and handled by the restarted host");
        }

        ReplayProbeHandler.HandledCount.Should().Be(1);
    }

    private async ValueTask DisposeAsyncCore()
    {
        if (!_started)
        {
            return;
        }

        await _pg.DisposeAsync();
        _started = false;
    }

    private static IHost BuildHost(string connectionString) =>
        Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddWolverine(options =>
                {
                    options.Discovery.IncludeAssembly(typeof(OutboxReplayIntegrationTests).Assembly);
                    options.PersistMessagesWithPostgresql(connectionString, "wolverine_replay");
                    options.Policies.UseDurableLocalQueues();
                });
            })
            .Build();

    private static async Task<bool> WaitForReplayAsync(TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (ReplayProbeHandler.HandledCount > 0)
            {
                return true;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250));
        }

        return false;
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

    public sealed record ReplayProbeMessage(string ShipSymbol);

    public static class ReplayProbeHandler
    {
        private static int _handledCount;

        public static int HandledCount => _handledCount;

        public static void Reset() => _handledCount = 0;

        public static Task Handle(ReplayProbeMessage message)
        {
            Interlocked.Increment(ref _handledCount);
            return Task.CompletedTask;
        }
    }
}
