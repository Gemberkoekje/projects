using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SpaceTraders.Application.Sync;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Clients;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Agents;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Common;

namespace SpaceTraders.Application.Tests.Sync;

public sealed class SyncAgentHandlerTests
{
    private static Agent MakeAgent(string symbol = "AGENT-1", long credits = 50_000) =>
        new() { Symbol = symbol, Credits = credits, StartingFaction = "COSMIC", ShipCount = 1 };

    [Fact]
    public async Task Handle_NewAgent_PersistsToDatabase()
    {
        var apiClient = Substitute.For<ISpaceTradersApiClient>();
        apiClient.GetMyAgentAsync(Arg.Any<CancellationToken>())
                 .Returns(MakeAgent(credits: 50_000));

        await using var db = TestDbContextFactory.Create();
        var handler = new SyncAgentHandler(apiClient, db, NullLogger<SyncAgentHandler>.Instance);

        await handler.Handle(new SyncAgentCommand(), CancellationToken.None);

        var cached = await db.Agents.FindAsync("AGENT-1");
        cached.Should().NotBeNull();
        cached!.Credits.Should().Be(50_000);
    }

    [Fact]
    public async Task Handle_ExistingAgent_UpdatesCredits()
    {
        var apiClient = Substitute.For<ISpaceTradersApiClient>();
        apiClient.GetMyAgentAsync(Arg.Any<CancellationToken>())
                 .Returns(MakeAgent(credits: 75_000));

        await using var db = TestDbContextFactory.Create();
        db.Agents.Add(new Infrastructure.Persistence.Entities.CachedAgent
        {
            Symbol = "AGENT-1",
            StartingFaction = "COSMIC",
            Credits = 50_000,
            ShipCount = 1,
            LastSyncedAt = DateTimeOffset.UtcNow.AddMinutes(-5)
        });
        await db.SaveChangesAsync();

        var handler = new SyncAgentHandler(apiClient, db, NullLogger<SyncAgentHandler>.Instance);

        await handler.Handle(new SyncAgentCommand(), CancellationToken.None);

        var cached = await db.Agents.FindAsync("AGENT-1");
        cached!.Credits.Should().Be(75_000);
    }

    [Fact]
    public async Task Handle_DoesNotIssueFollowUpGetCalls()
    {
        var apiClient = Substitute.For<ISpaceTradersApiClient>();
        apiClient.GetMyAgentAsync(Arg.Any<CancellationToken>())
                 .Returns(MakeAgent());

        await using var db = TestDbContextFactory.Create();
        var handler = new SyncAgentHandler(apiClient, db, NullLogger<SyncAgentHandler>.Instance);

        await handler.Handle(new SyncAgentCommand(), CancellationToken.None);

        // Only one GET call total – GetMyAgentAsync
        await apiClient.Received(1).GetMyAgentAsync(Arg.Any<CancellationToken>());
        await apiClient.DidNotReceive().GetMyShipsAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
        await apiClient.DidNotReceive().GetMyContractsAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }
}
