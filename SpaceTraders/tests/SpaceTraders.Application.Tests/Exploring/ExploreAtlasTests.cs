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

    [Fact]
    public void TheWayToASystem_IsTheFewestJumps_ThroughBuiltGates()
    {
        // Slice 6.28 (D101): X1-A to X1-D goes by X1-B (2 jumps), not by X1-C and X1-E (3); X1-F's gate is unbuilt.
        var state = Network();

        ExploreAtlas.TryFindJumps(state, "X1-A", "X1-D", Now, out var jumps).Should().BeTrue();
        jumps.Should().Equal(new GateJump("X1-A-G", "X1-B-G"), new GateJump("X1-B-G", "X1-D-G"));
        ExploreAtlas.TryFindJumps(state, "X1-D", "X1-A", Now, out var back).Should().BeTrue();
        back.Should().Equal(new GateJump("X1-D-G", "X1-B-G"), new GateJump("X1-B-G", "X1-A-G"));
        ExploreAtlas.TryFindJumps(state, "X1-A", "X1-A", Now, out var none).Should().BeTrue();
        none.Should().BeEmpty();
        ExploreAtlas.TryFindJumps(state, "X1-A", "X1-F", Now, out _).Should().BeFalse("X1-F's gate isn't built");
        ExploreAtlas.TryFindJumps(state, "X1-A", "X1-Z", Now, out _).Should().BeFalse("nothing is known of X1-Z");
    }

    [Fact]
    public void Reachable_CountsTheJumps_OnlyThroughBuiltGates_AndOnlyOnThroughExploredSystems()
    {
        // X1-G hangs off X1-E, which isn't explored here: its connections aren't known, so nothing beyond it is reached.
        var state = Network() with
        {
            Systems = [.. Network().Systems.Select(system => system.SystemSymbol == "X1-E" ? system with { ExploredAt = null } : system)],
        };

        ExploreAtlas.Reachable(state, "X1-A", Now).Should().BeEquivalentTo(new Dictionary<string, int>
        {
            ["X1-A"] = 0,
            ["X1-B"] = 1,
            ["X1-C"] = 1,
            ["X1-D"] = 2,
            ["X1-E"] = 2,
        });
    }

    [Fact]
    public void TheSystemsLeft_AreThoseNotExploredThatBuiltGatesReach()
    {
        // Slice 6.30 (D102): "Reachable, round up". In the network X1-G is left, and X1-F, whose gate is unbuilt, isn't yet.
        ExploreAtlas.SystemsLeft(Network(), Now).Should().Be(1);
        ExploreAtlas.SystemsLeft(State(null), Now).Should().Be(2);
        ExploreAtlas.SystemsLeft(State(Now.AddMinutes(-10)), Now).Should().Be(1, "X1-B's gate refused a jump within the hour");
    }

    [Fact]
    public void ASystemAnotherShipHasTaken_IsLeftToIt()
    {
        // Slice 6.30: each exploring ship takes the nearest system no other exploring ship has taken.
        ExploreAtlas.Next(State(null), "X1-A", Now, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "x1-b" })
            .Should().BeEquivalentTo(new { Kind = ExploreStepKind.Explore, TargetSystemSymbol = "X1-C" });
        ExploreAtlas.Next(State(null), "X1-A", Now, new HashSet<string> { "X1-B", "X1-C" }).Kind.Should().Be(ExploreStepKind.Home, "both are taken");
    }

    [Fact]
    public void ASystemWithinTheTradeReach_ComesFirst_ThoughOneBeyondItIsNearerTheShip()
    {
        // D103, asked on 2026-10-06: "first the systems within 5 jumps are explored before going further". That day the command
        // ship, always taking the system nearest to it, had explored a chain 16 jumps deep while X1-QA35 and X1-QR21, one jump
        // from home, waited. Here X1-H hangs off home, X1-G off X1-E: from X1-D, X1-G is 2 jumps and X1-H 3, but from home
        // X1-G is 3 jumps and X1-H 1.
        var network = Network();
        var state = network with
        {
            Systems =
            [
                .. network.Systems.Select(system => system.SystemSymbol == "X1-A" ? system with { Connections = [.. system.Connections!, "X1-H-G"] } : system),
                Known("X1-H", []) with { ExploredAt = null },
            ],
        };

        ExploreAtlas.Next(state, "X1-D", Now, reach: 2).Should().BeEquivalentTo(new { Kind = ExploreStepKind.Explore, TargetSystemSymbol = "X1-H", Jumps = 3 });
        ExploreAtlas.Next(state, "X1-D", Now, reach: 3).TargetSystemSymbol.Should().Be("X1-G", "both are within the reach: the nearer to the ship first");
        ExploreAtlas.Next(state, "X1-D", Now).TargetSystemSymbol.Should().Be("X1-G", "without a reach, the nearest");
        ExploreAtlas.Next(state, "X1-D", Now, new HashSet<string> { "X1-H" }, reach: 2).TargetSystemSymbol.Should().Be("X1-G", "X1-H is taken");
    }

    [Fact]
    public void TheWayHome_WhateverIsLeftToExplore()
    {
        // Slice 6.30 (D98): once an explorer explores, the command ship comes home, though X1-G is still to explore.
        ExploreAtlas.HomeFrom(Network(), "X1-D", Now).Should().BeEquivalentTo(new
        {
            Kind = ExploreStepKind.ReturnHome,
            GateWaypointSymbol = "X1-D-G",
            DestinationGateWaypointSymbol = "X1-B-G",
            TargetSystemSymbol = "X1-A",
            Jumps = 2,
        });
        ExploreAtlas.HomeFrom(Network(), "X1-A", Now).Kind.Should().Be(ExploreStepKind.Home);
    }

    [Fact]
    public void AGateThatRefusedAJumpLately_IsNoWay_ForAnHour()
    {
        // Every ship's refused jumps are recorded (JumpRefusals): a probe isn't sent through a gate that has just refused one.
        var refusals = new JumpRefusals();
        refusals.Record("X1-B-G", Now.AddMinutes(-10));
        var state = refusals.Apply(Network());

        ExploreAtlas.TryFindJumps(state, "X1-A", "X1-D", Now, out var jumps).Should().BeTrue();
        jumps.Should().HaveCount(3, "it goes round by X1-C and X1-E");
        ExploreAtlas.TryFindJumps(state, "X1-A", "X1-B", Now, out _).Should().BeFalse();
        ExploreAtlas.TryFindJumps(state, "X1-A", "X1-B", Now.AddMinutes(51), out _).Should().BeTrue();
    }

    /// <summary>
    /// Home X1-A connects to X1-B and X1-C; X1-B to X1-D; X1-C to X1-E; X1-E to X1-D and X1-G, and to X1-F, whose gate is
    /// under construction. All explored but X1-F and X1-G.
    /// </summary>
    private static ExplorePlanState Network() => new()
    {
        ShipSymbol = "SHIP-1",
        HomeSystemSymbol = "X1-A",
        Status = ExploreStatus.Exploring,
        UpdatedAt = Now,
        Systems =
        [
            Known("X1-A", ["X1-B-G", "X1-C-G"]),
            Known("X1-B", ["X1-A-G", "X1-D-G"]),
            Known("X1-C", ["X1-A-G", "X1-E-G"]),
            Known("X1-D", ["X1-B-G", "X1-E-G"]),
            Known("X1-E", ["X1-C-G", "X1-D-G", "X1-F-G", "X1-G-G"]),
            Known("X1-F", []) with { Gate = GateState.UnderConstruction, ExploredAt = null },
            Known("X1-G", []) with { ExploredAt = null },
        ],
    };

    private static KnownSystem Known(string symbol, IReadOnlyList<string> connections) => new()
    {
        SystemSymbol = symbol,
        GateWaypointSymbol = $"{symbol}-G",
        Gate = GateState.Active,
        GateCheckedAt = Now,
        Connections = connections,
        ConnectionsCheckedAt = Now,
        ExploredAt = Now,
    };

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
