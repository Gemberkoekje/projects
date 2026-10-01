using DotNet.Testcontainers.Configurations;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Testcontainers.PostgreSql;

namespace SpaceTraders.API.Tests;

/// <summary>
/// B2: Wolverine stored every published message in Postgres, and with its durability agent off it
/// never deleted a handled one. Messages now stay in memory.
/// </summary>
[Trait("Category", "Integration")]
public sealed class MessageStorageIntegrationTests : IAsyncLifetime
{
    private PostgreSqlContainer _pg = default!;
    private bool _started;

    public async Task InitializeAsync()
    {
        // Testcontainers finds Docker the way it will start the container: DOCKER_HOST, the Unix socket, or
        // Docker Desktop's named pipe on Windows (B36).
        Skip.IfNot(TestcontainersSettings.OS.DockerEndpointAuthConfig is not null, "Docker is not available – skipping integration tests.");

        _pg = new PostgreSqlBuilder("postgres:16-alpine").Build();
        await _pg.StartAsync();
        _started = true;
    }

    public async Task DisposeAsync()
    {
        if (_started)
        {
            await _pg.DisposeAsync();
        }
    }

    [SkippableFact]
    public async Task StartingTheHost_CreatesNoMessageTables()
    {
        using (var factory = new ProductionHostFactory(_pg.GetConnectionString()))
        {
            // Builds and starts the host, with the same Wolverine setup as on the cluster.
            _ = factory.Services;
        }

        await using var connection = new NpgsqlConnection(_pg.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("select count(*) from information_schema.tables where table_schema = 'wolverine'", connection);
        var tables = (long)(await command.ExecuteScalarAsync())!;

        tables.Should().Be(0);
    }

    private sealed class ProductionHostFactory(string connectionString) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("ConnectionStrings:DefaultConnection", connectionString);
            builder.UseSetting("Metrics:Port", "0");
            builder.ConfigureTestServices(services =>
            {
                // Only the framework's own hosted services run: no startup chain, no game API calls.
                var appServices = services
                    .Where(d => d.ServiceType == typeof(IHostedService)
                        && d.ImplementationType?.Namespace?.StartsWith("SpaceTraders", StringComparison.Ordinal) == true)
                    .ToList();
                foreach (var service in appServices)
                {
                    services.Remove(service);
                }
            });
        }
    }
}
