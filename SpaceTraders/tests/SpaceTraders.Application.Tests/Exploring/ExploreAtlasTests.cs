using FluentAssertions;
using SpaceTraders.Application.Exploring;

namespace SpaceTraders.Application.Tests.Exploring;

/// <summary>Where the explore plan goes next (asked on 2026-10-04), from what it knows of the gates.</summary>
public sealed class ExploreAtlasTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 04, 08, 00, 00, TimeSpan.Zero);

    [Fact]
    public void AGateTheApiRefused_IsLeftAloneForAnHour()
    {
        var refused = State(Now.AddMinutes(-59));
        var later = State(Now.AddMinutes(-61));

        ExploreAtlas.Next(refused, "X1-A", Now).Should().BeEquivalentTo(new { Kind = ExploreStepKind.Explore, TargetSystemSymbol = "X1-C" });
        ExploreAtlas.Next(later, "X1-A", Now).Should().BeEquivalentTo(new { Kind = ExploreStepKind.Explore, TargetSystemSymbol = "X1-B" });
    }

    [Fact]
    public void ANeighboursGateNeverLookedAt_HoldsTheChoice()
    {
        var state = State(null) with
        {
            Systems = [.. State(null).Systems, new KnownSystem { SystemSymbol = "X1-D", GateWaypointSymbol = "X1-D-G" }],
        };
        state = state with { Systems = [.. state.Systems.Select(system => system.SystemSymbol == "X1-A" ? system with { Connections = ["X1-B-G", "X1-C-G", "X1-D-G"] } : system)] };

        ExploreAtlas.Next(state, "X1-A", Now).Kind.Should().Be(ExploreStepKind.Wait);
    }

    [Fact]
    public void ALookThatFailed_DoesNotHoldTheChoice()
    {
        var state = State(null) with
        {
            Systems = [.. State(null).Systems, new KnownSystem { SystemSymbol = "X1-D", GateWaypointSymbol = "X1-D-G", GateCheckedAt = Now.AddMinutes(-1) }],
        };
        state = state with { Systems = [.. state.Systems.Select(system => system.SystemSymbol == "X1-A" ? system with { Connections = ["X1-B-G", "X1-C-G", "X1-D-G"] } : system)] };

        ExploreAtlas.Next(state, "X1-A", Now).Kind.Should().Be(ExploreStepKind.Explore);
    }

    [Fact]
    public void JumpsFromHome_CountOneMoreToAGateUnderConstruction()
    {
        var state = State(null) with
        {
            Systems = [.. State(null).Systems.Select(system => system.SystemSymbol == "X1-C" ? system with { Gate = GateState.UnderConstruction } : system)],
        };

        ExploreAtlas.JumpsFromHome(state, Now).Should().BeEquivalentTo(new Dictionary<string, int>
        {
            ["X1-A"] = 0,
            ["X1-B"] = 1,
            ["X1-C"] = 1,
        });
        ExploreAtlas.Next(state, "X1-A", Now).TargetSystemSymbol.Should().Be("X1-B", "X1-C's gate isn't built");
    }

    /// <summary>Home X1-A, explored, connects to X1-B and X1-C, both built and not explored; the jump to X1-B was refused at <paramref name="refusedAt"/>.</summary>
    private static ExplorePlanState State(DateTimeOffset? refusedAt) => new()
    {
        ShipSymbol = "SHIP-1",
        HomeSystemSymbol = "X1-A",
        Status = ExploreStatus.Waiting,
        UpdatedAt = Now,
        Systems =
        [
            new KnownSystem
            {
                SystemSymbol = "X1-A",
                GateWaypointSymbol = "X1-A-G",
                Gate = GateState.Active,
                GateCheckedAt = Now,
                Connections = ["X1-B-G", "X1-C-G"],
                ConnectionsCheckedAt = Now,
                ExploredAt = Now,
            },
            new KnownSystem { SystemSymbol = "X1-B", GateWaypointSymbol = "X1-B-G", Gate = GateState.Active, GateCheckedAt = Now, JumpRefusedAt = refusedAt },
            new KnownSystem { SystemSymbol = "X1-C", GateWaypointSymbol = "X1-C-G", Gate = GateState.Active, GateCheckedAt = Now },
        ],
    };
}
