using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Sync;
using SpaceTraders.Infrastructure.Persistence.Repositories;

namespace SpaceTraders.Application.Tests.Sync;

public sealed class SyncAgentHandlerTests
{
    [Fact]
    public async Task Handle_PersistsAgent()
    {
        var port = Substitute.For<ISpaceTradersPort>();
        port.GetMyAgentAsync(Arg.Any<CancellationToken>())
            .Returns(new AgentModel("AGENT-1", null, "X1-AB-HQ", 50_000, "COSMIC", 2));

        await using var db = TestDbContextFactory.Create();
        var repo = new AgentRepository(db);
        await new SyncAgentHandler(port, repo, NullLogger<SyncAgentHandler>.Instance)
            .Handle(new SyncAgentCommand(), CancellationToken.None);

        var agent = await db.Agents.FindAsync("AGENT-1");
        agent.Should().NotBeNull();
        agent!.Credits.Should().Be(50_000);
    }

    [Fact]
    public async Task Handle_UpdatesExistingAgent()
    {
        var port = Substitute.For<ISpaceTradersPort>();
        port.GetMyAgentAsync(Arg.Any<CancellationToken>())
            .Returns(new AgentModel("AGENT-1", null, null, 99_000, "COSMIC", 3));

        await using var db = TestDbContextFactory.Create();
        var repo = new AgentRepository(db);

        // First sync
        await new SyncAgentHandler(port, repo, NullLogger<SyncAgentHandler>.Instance)
            .Handle(new SyncAgentCommand(), CancellationToken.None);

        // Second sync with new credits
        port.GetMyAgentAsync(Arg.Any<CancellationToken>())
            .Returns(new AgentModel("AGENT-1", null, null, 120_000, "COSMIC", 3));
        await new SyncAgentHandler(port, repo, NullLogger<SyncAgentHandler>.Instance)
            .Handle(new SyncAgentCommand(), CancellationToken.None);

        db.Agents.Should().HaveCount(1);
        var agent = await db.Agents.FindAsync("AGENT-1");
        agent!.Credits.Should().Be(120_000);
    }

    [Fact]
    public async Task Handle_DoesNotIssueNonAgentCalls()
    {
        var port = Substitute.For<ISpaceTradersPort>();
        port.GetMyAgentAsync(Arg.Any<CancellationToken>())
            .Returns(new AgentModel("AGENT-1", null, null, 50_000, "COSMIC", 1));

        await using var db = TestDbContextFactory.Create();
        var repo = new AgentRepository(db);
        await new SyncAgentHandler(port, repo, NullLogger<SyncAgentHandler>.Instance)
            .Handle(new SyncAgentCommand(), CancellationToken.None);

        await port.DidNotReceive().GetMyShipsAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
        await port.DidNotReceive().GetMyContractsAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }
}
