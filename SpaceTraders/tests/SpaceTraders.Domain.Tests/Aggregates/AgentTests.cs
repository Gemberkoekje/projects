using FluentAssertions;
using SpaceTraders.Domain.Aggregates;
using SpaceTraders.Domain.Events;

namespace SpaceTraders.Domain.Tests.Aggregates;

public sealed class AgentTests
{
    private static Agent CreateAgent(long credits = 100_000) =>
        Agent.Reconstitute("AGENT-1", credits, "COSMIC", 2, "X1-AB-HQ");

    [Fact]
    public void UpdateCredits_DeltaAboveThreshold_RaisesCreditsChangedEvent()
    {
        var agent = CreateAgent(100_000);

        agent.UpdateCredits(120_000);

        agent.DomainEvents.Should().ContainSingle(e => e is AgentCreditsChangedEvent);
        var evt = (AgentCreditsChangedEvent)agent.DomainEvents[0];
        evt.OldCredits.Should().Be(100_000);
        evt.NewCredits.Should().Be(120_000);
    }

    [Fact]
    public void UpdateCredits_DeltaBelowThreshold_DoesNotRaiseEvent()
    {
        var agent = CreateAgent(100_000);

        agent.UpdateCredits(105_000); // delta 5000 < 10000 threshold

        agent.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void UpdateCredits_ExactlyAtThreshold_RaisesEvent()
    {
        var agent = CreateAgent(100_000);

        agent.UpdateCredits(110_000); // delta == 10000

        agent.DomainEvents.Should().ContainSingle(e => e is AgentCreditsChangedEvent);
    }

    [Fact]
    public void UpdateCredits_UpdatesCreditsValue()
    {
        var agent = CreateAgent(100_000);

        agent.UpdateCredits(200_000);

        agent.Credits.Should().Be(200_000);
    }

    [Fact]
    public void UpdateShipCount_UpdatesProperty()
    {
        var agent = CreateAgent();

        agent.UpdateShipCount(5);

        agent.ShipCount.Should().Be(5);
    }
}
