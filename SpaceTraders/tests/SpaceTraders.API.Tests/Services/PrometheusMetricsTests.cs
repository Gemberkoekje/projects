using System.IO;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Prometheus;
using SpaceTraders.API.Services;
using SpaceTraders.Application.Automation;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Naming;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Roles;
using SpaceTraders.Application.Services;
using SpaceTraders.Application.Trading;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Goals;
using SpaceTraders.Infrastructure.Persistence;
using SpaceTraders.Infrastructure.Persistence.Entities;
using SpaceTraders.Infrastructure.Persistence.Scoping;
using SpaceTraders.Infrastructure.Persistence.Seed;

namespace SpaceTraders.API.Tests.Services;

/// <summary>B11: the state the dashboard shows comes from the cache, every 10 seconds.</summary>
public sealed class PrometheusMetricsServiceTests
{
    private const string AgentId = "AGENT@2026-09-27";

    private readonly IAutomationMetrics _metrics = Substitute.For<IAutomationMetrics>();
    private readonly ShipNameBook _names = new(new ActiveReset(Agent()));

    [Fact]
    public async Task SampleAsync_ExportsCreditsShipsAndContracts_FromTheCache()
    {
        var blocked = new ScoutWaypointGoal { TargetWaypointSymbol = "X1-AB-2", Status = GoalStatus.Blocked, StatusReason = "runaway" };
        using var provider = BuildProvider();
        await using (var seedScope = provider.CreateAsyncScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<SpaceTradersDbContext>();
            db.Agents.Add(new CachedAgent { AgentId = AgentId, Symbol = "AGENT", StartingFaction = "COSMIC", Credits = 175_000 });
            db.Ships.Add(new CachedShip { AgentId = AgentId, Symbol = "AGENT-1", ShipType = "COMMAND", Status = "DOCKED" });
            db.Ships.Add(new CachedShip
            {
                AgentId = AgentId,
                Symbol = "AGENT-2",
                ShipType = "SATELLITE",
                Status = "IN_TRANSIT",
                ArrivesAt = TimeProvider.System.GetUtcNow().AddMinutes(5),
            });
            db.Ships.Add(new CachedShip
            {
                AgentId = AgentId,
                Symbol = "AGENT-3",
                ShipType = "EXCAVATOR",
                Status = "IN_TRANSIT",
                ArrivesAt = TimeProvider.System.GetUtcNow().AddMinutes(-1),
            });
            db.Ships.Add(new CachedShip
            {
                AgentId = AgentId,
                Symbol = "AGENT-4",
                ShipType = "COMMAND",
                Status = "IN_ORBIT",
                GoalId = blocked.GoalId,
                GoalKind = blocked.Kind.ToString(),
                GoalPayloadJson = JsonSerializer.Serialize<ShipGoal>(blocked),
                GoalStatus = (int)GoalStatus.Blocked,
            });
            db.ShipAssignments.Add(new ShipAssignmentRecord { AgentId = AgentId, ShipSymbol = "AGENT-3", Type = "Contract", ContractId = "C-1" });
            db.Contracts.Add(new CachedContract
            {
                AgentId = AgentId,
                Id = "C-1",
                FactionSymbol = "COSMIC",
                Type = "PROCUREMENT",
                IsAccepted = true,
                TermsDeadline = new DateTimeOffset(2026, 10, 08, 07, 09, 22, TimeSpan.Zero),
                DeliverablesJson = JsonSerializer.Serialize(new List<ContractDeliverableDto> { new("IRON_ORE", "X1-AB-2", 42, 7) }),
            });
            db.Contracts.Add(new CachedContract { AgentId = AgentId, Id = "C-2", FactionSymbol = "COSMIC", Type = "PROCUREMENT" });
            await db.SaveChangesAsync();
        }

        IReadOnlyCollection<ShipMetricsSample> ships = [];
        IReadOnlyCollection<ContractMetricsSample> contracts = [];
        _metrics.When(m => m.Fleet(Arg.Any<IReadOnlyCollection<ShipMetricsSample>>(), Arg.Any<DateTimeOffset>()))
            .Do(call => ships = call.Arg<IReadOnlyCollection<ShipMetricsSample>>());
        _metrics.When(m => m.Contracts(Arg.Any<IReadOnlyCollection<ContractMetricsSample>>()))
            .Do(call => contracts = call.Arg<IReadOnlyCollection<ContractMetricsSample>>());

        using var service = new PrometheusMetricsService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            _metrics,
            new ShipStateJournal(NullLogger<ShipStateJournal>.Instance),
            new PurchaseNeeds(),
            new FullHoldSavings(),
            _names,
            NullLogger<PrometheusMetricsService>.Instance);
        await service.SampleAsync(CancellationToken.None);

        _metrics.Received(1).Credits(175_000);
        ships.Select(s => new ShipMetricsSample(s.Ship, s.Role, s.State, s.Goal, s.Reason)).Should().BeEquivalentTo(new[]
        {
            new ShipMetricsSample("AGENT-1", "COMMAND", "DOCKED", "None", string.Empty),
            new ShipMetricsSample("AGENT-2", "SATELLITE", "IN_TRANSIT", "None", string.Empty),
            new ShipMetricsSample("AGENT-3", "EXCAVATOR", "IN_ORBIT", "Contract", string.Empty),
            new ShipMetricsSample("AGENT-4", "COMMAND", "IN_ORBIT", "ScoutWaypoint", "runaway"),
        });
        contracts.Should().Equal(new ContractMetricsSample("C-1", "IRON_ORE", 42, 7, new DateTimeOffset(2026, 10, 08, 07, 09, 22, TimeSpan.Zero)));
    }

    /// <summary>
    /// Slice 2.14 (D72): beside its symbol, each ship's name, which the log lines read from the name book. The two probes are
    /// one type, the starting one as startup sync stores it and the bought one as the purchase does.
    /// </summary>
    [Fact]
    public async Task SampleAsync_NamesEachShip_AndTellsTheNameBook()
    {
        using var provider = BuildProvider();
        await using (var seedScope = provider.CreateAsyncScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<SpaceTradersDbContext>();
            db.Ships.Add(new CachedShip { AgentId = AgentId, Symbol = "AGENT-1", ShipType = "COMMAND", Status = "DOCKED", FrameJson = """{"symbol":"FRAME_FRIGATE"}""" });
            db.Ships.Add(new CachedShip { AgentId = AgentId, Symbol = "AGENT-2", ShipType = "SATELLITE", Status = "DOCKED", FrameJson = """{"symbol":"FRAME_PROBE"}""" });
            db.Ships.Add(new CachedShip { AgentId = AgentId, Symbol = "AGENT-3", ShipType = "SHIP_PROBE", Status = "DOCKED" });
            await db.SaveChangesAsync();
        }

        IReadOnlyCollection<ShipMetricsSample> ships = [];
        _metrics.When(m => m.Fleet(Arg.Any<IReadOnlyCollection<ShipMetricsSample>>(), Arg.Any<DateTimeOffset>()))
            .Do(call => ships = call.Arg<IReadOnlyCollection<ShipMetricsSample>>());

        using var service = new PrometheusMetricsService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            _metrics,
            new ShipStateJournal(NullLogger<ShipStateJournal>.Instance),
            new PurchaseNeeds(),
            new FullHoldSavings(),
            _names,
            NullLogger<PrometheusMetricsService>.Instance);
        await service.SampleAsync(CancellationToken.None);

        var commandShip = ShipNames.Lists["SHIP_COMMAND_FRIGATE"];
        var probe = ShipNames.Lists["SHIP_PROBE"];
        ships.Select(ship => (ship.Ship, ship.NamedType)).Should().BeEquivalentTo(
            [("AGENT-1", "SHIP_COMMAND_FRIGATE"), ("AGENT-2", "SHIP_PROBE"), ("AGENT-3", "SHIP_PROBE")]);
        var names = ships.ToDictionary(ship => ship.Ship, ship => ship.Name);
        commandShip.Should().Contain(names["AGENT-1"][..^2]);
        probe.Should().Contain(names["AGENT-2"][..^2]);
        names["AGENT-2"].Should().EndWith("-1");
        names["AGENT-3"].Should().Be($"{names["AGENT-2"][..^2]}-2");
        _names.NameOf("AGENT-3").Should().Be(names["AGENT-3"]);
    }

    /// <summary>
    /// The dashboard's fleet table shows where each ship is (and what is there), what the bot has it
    /// do and what it carries: on 2026-10-02 a drone in orbit was mining, and nothing showed it.
    /// </summary>
    [Fact]
    public async Task SampleAsync_SaysWhereEachShipIsWhatItDoesAndWhatItCarries()
    {
        var now = TimeProvider.System.GetUtcNow();
        var scout = new ScoutWaypointGoal { TargetWaypointSymbol = "X1-AB-A2" };
        using var provider = BuildProvider();
        await using (var seedScope = provider.CreateAsyncScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<SpaceTradersDbContext>();
            db.Waypoints.Add(Waypoint("X1-AB-XB5C", "ENGINEERED_ASTEROID"));
            db.Waypoints.Add(Waypoint("X1-AB-H51", "PLANET"));
            db.Waypoints.Add(Waypoint("X1-AB-A2", "MOON"));
            db.Waypoints.Add(Waypoint("X1-AB-C38", "GAS_GIANT"));

            // The scout, on its way to the next market.
            db.Ships.Add(new CachedShip
            {
                AgentId = AgentId,
                Symbol = "AGENT-1",
                ShipType = "COMMAND",
                Status = "IN_TRANSIT",
                WaypointSymbol = "X1-AB-A2",
                DestWaypointSymbol = "X1-AB-A2",
                ArrivesAt = now.AddMinutes(2),
                CargoCapacity = 40,
                GoalId = scout.GoalId,
                GoalKind = scout.Kind.ToString(),
                GoalPayloadJson = JsonSerializer.Serialize<ShipGoal>(scout),
                GoalStatus = (int)scout.Status,
            });

            // The probe, parked without a flight: the market watch keeps H51 fresh (slice 6.3).
            db.Ships.Add(new CachedShip { AgentId = AgentId, Symbol = "AGENT-2", ShipType = "SATELLITE", Status = "DOCKED", WaypointSymbol = "X1-AB-H51" });

            // A probe roaming to the next market, and one called to a shipyard for a purchase (D30).
            var roam = new DeployProbeGoal { TargetWaypointSymbol = "X1-AB-A2" };
            db.Ships.Add(new CachedShip
            {
                AgentId = AgentId,
                Symbol = "AGENT-6",
                ShipType = "SHIP_PROBE",
                Status = "IN_TRANSIT",
                WaypointSymbol = "X1-AB-A2",
                DestWaypointSymbol = "X1-AB-A2",
                ArrivesAt = now.AddMinutes(4),
                GoalId = roam.GoalId,
                GoalKind = roam.Kind.ToString(),
                GoalPayloadJson = JsonSerializer.Serialize<ShipGoal>(roam),
                GoalStatus = (int)roam.Status,
            });
            var call = new DeployProbeGoal { TargetWaypointSymbol = "X1-AB-A2", ForPurchase = true };
            db.Ships.Add(new CachedShip
            {
                AgentId = AgentId,
                Symbol = "AGENT-7",
                ShipType = "SATELLITE",
                Status = "DOCKED",
                WaypointSymbol = "X1-AB-H51",
                GoalId = call.GoalId,
                GoalKind = call.Kind.ToString(),
                GoalPayloadJson = JsonSerializer.Serialize<ShipGoal>(call),
                GoalStatus = (int)call.Status,
            });

            // The contract's drone, in orbit at its asteroid, and a second one on its way to deliver.
            db.Ships.Add(new CachedShip
            {
                AgentId = AgentId,
                Symbol = "AGENT-3",
                ShipType = "SHIP_MINING_DRONE",
                Status = "IN_ORBIT",
                WaypointSymbol = "X1-AB-XB5C",
                CargoCurrent = 11,
                CargoCapacity = 15,
                CargoJson = """[{"Symbol":"COPPER_ORE","Units":9},{"Symbol":"SILICON_CRYSTALS","Units":2}]""",
            });
            db.Ships.Add(new CachedShip
            {
                AgentId = AgentId,
                Symbol = "AGENT-5",
                ShipType = "SHIP_MINING_DRONE",
                Status = "IN_TRANSIT",
                WaypointSymbol = "X1-AB-H51",
                DestWaypointSymbol = "X1-AB-H51",
                ArrivesAt = now.AddMinutes(3),
                CargoCurrent = 15,
                CargoCapacity = 15,
                CargoJson = """[{"Symbol":"COPPER_ORE","Units":15}]""",
            });
            // A siphon drone at its gas giant (slice 6.7).
            var siphon = new SiphonAndSellGoal { TradeSymbol = "LIQUID_HYDROGEN", SourceWaypointSymbol = "X1-AB-C38", SellWaypointSymbol = "X1-AB-H51" };
            db.Ships.Add(new CachedShip
            {
                AgentId = AgentId,
                Symbol = "AGENT-8",
                ShipType = "SHIP_SIPHON_DRONE",
                Status = "IN_ORBIT",
                WaypointSymbol = "X1-AB-C38",
                CargoCapacity = 15,
                GoalId = siphon.GoalId,
                GoalKind = siphon.Kind.ToString(),
                GoalPayloadJson = JsonSerializer.Serialize<ShipGoal>(siphon),
                GoalStatus = (int)siphon.Status,
            });
            // The command ship in its spare time (slice 6.8): mining, and selling a good of its hold.
            var gather = new GatherAndSellGoal { SourceWaypointSymbol = "X1-AB-XB5C" };
            var selling = gather with { Selling = true, SellTradeSymbol = "QUARTZ_SAND", SellWaypointSymbol = "X1-AB-F49" };
            foreach (var (symbol, goal) in new[] { ("AGENT-9", gather), ("AGENT-10", selling) })
            {
                db.Ships.Add(new CachedShip
                {
                    AgentId = AgentId,
                    Symbol = symbol,
                    ShipType = "COMMAND",
                    Status = "IN_ORBIT",
                    WaypointSymbol = "X1-AB-XB5C",
                    CargoCapacity = 40,
                    GoalId = goal.GoalId,
                    GoalKind = goal.Kind.ToString(),
                    GoalPayloadJson = JsonSerializer.Serialize<ShipGoal>(goal),
                    GoalStatus = (int)goal.Status,
                });
            }

            // A mining drone and a siphon drone drifting to markets out of their CRUISE reach (slice 6.10c, D45), and the survey
            // ship drifting to where most drones mine (D54): hours away.
            var mineFar = new MineAndSellGoal { TradeSymbol = "GOLD_ORE", SourceWaypointSymbol = "X1-AB-B14", SellWaypointSymbol = "X1-AB-B7", Drifting = true };
            var siphonFar = new SiphonAndSellGoal { TradeSymbol = "LIQUID_NITROGEN", SourceWaypointSymbol = "X1-AB-D90", SellWaypointSymbol = "X1-AB-F48", Drifting = true };
            var surveyorFar = new MoveToWaypointGoal { TargetWaypointSymbol = "X1-AB-B7", Drifting = true };
            foreach (var (symbol, shipType, goal) in new (string, string, ShipGoal)[]
            {
                ("AGENT-11", "SHIP_MINING_DRONE", mineFar),
                ("AGENT-12", "SHIP_SIPHON_DRONE", siphonFar),
                ("AGENT-13", "SHIP_SURVEYOR", surveyorFar),
            })
            {
                var market = goal switch
                {
                    MineAndSellGoal mine => mine.SellWaypointSymbol,
                    SiphonAndSellGoal siphoning => siphoning.SellWaypointSymbol,
                    _ => ((MoveToWaypointGoal)goal).TargetWaypointSymbol,
                };
                db.Ships.Add(new CachedShip
                {
                    AgentId = AgentId,
                    Symbol = symbol,
                    ShipType = shipType,
                    Status = "IN_TRANSIT",
                    WaypointSymbol = market,
                    DestWaypointSymbol = market,
                    ArrivesAt = now.AddHours(2),
                    FlightMode = "DRIFT",
                    CargoCapacity = 15,
                    GoalId = goal.GoalId,
                    GoalKind = goal.Kind.ToString(),
                    GoalPayloadJson = JsonSerializer.Serialize(goal),
                    GoalStatus = (int)goal.Status,
                });
            }

            db.ShipAssignments.Add(ContractAssignment("AGENT-3"));
            db.ShipAssignments.Add(ContractAssignment("AGENT-5"));
            await db.SaveChangesAsync();
        }

        IReadOnlyCollection<ShipMetricsSample> ships = [];
        _metrics.When(m => m.Fleet(Arg.Any<IReadOnlyCollection<ShipMetricsSample>>(), Arg.Any<DateTimeOffset>()))
            .Do(call => ships = call.Arg<IReadOnlyCollection<ShipMetricsSample>>());

        using var service = new PrometheusMetricsService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            _metrics,
            new ShipStateJournal(NullLogger<ShipStateJournal>.Instance),
            new PurchaseNeeds(),
            new FullHoldSavings(),
            _names,
            NullLogger<PrometheusMetricsService>.Instance);
        await service.SampleAsync(CancellationToken.None);

        ships.Select(s => (s.Ship, s.Location, s.Activity)).Should().BeEquivalentTo(new[]
        {
            ("AGENT-1", "→ X1-AB-A2 (MOON)", "scouting"),
            ("AGENT-2", "X1-AB-H51 (PLANET)", "watching its market"),
            ("AGENT-3", "X1-AB-XB5C (ENGINEERED_ASTEROID)", "mining COPPER_ORE"),
            ("AGENT-5", "→ X1-AB-H51 (PLANET)", "on the way to deliver COPPER_ORE"),
            ("AGENT-6", "→ X1-AB-A2 (MOON)", "scouting"),
            ("AGENT-7", "X1-AB-H51 (PLANET)", "called to a shipyard"),
            ("AGENT-8", "X1-AB-C38 (GAS_GIANT)", "siphoning for LIQUID_HYDROGEN"),
            ("AGENT-9", "X1-AB-XB5C (ENGINEERED_ASTEROID)", "mining in its spare time"),
            ("AGENT-10", "X1-AB-XB5C (ENGINEERED_ASTEROID)", "selling QUARTZ_SAND"),
            ("AGENT-11", "→ X1-AB-B7", "drifting to X1-AB-B7 to mine GOLD_ORE"),
            ("AGENT-12", "→ X1-AB-F48", "drifting to X1-AB-F48 to siphon for LIQUID_NITROGEN"),
            ("AGENT-13", "→ X1-AB-B7", "drifting to X1-AB-B7"),
        });
        var drone = ships.Single(s => s.Ship == "AGENT-3");
        drone.CargoCapacity.Should().Be(15);
        drone.Cargo.Should().BeEquivalentTo(new[] { new CargoItemModel("COPPER_ORE", 9), new CargoItemModel("SILICON_CRYSTALS", 2) });
        drone.ArrivesAt.Should().Be(default);
        ships.Single(s => s.Ship == "AGENT-1").ArrivesAt.Should().BeCloseTo(now.AddMinutes(2), TimeSpan.FromSeconds(1));
    }

    /// <summary>
    /// A ship is worth what was paid for it and for the mounts and modules installed on it, as the
    /// ledger has it; a starting ship cost nothing. Feeds the total value graph.
    /// </summary>
    [Fact]
    public async Task SampleAsync_ValuesEachShipAtWhatWasPaidForItAndItsEquipment()
    {
        using var provider = BuildProvider();
        await using (var seedScope = provider.CreateAsyncScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<SpaceTradersDbContext>();
            db.Ships.Add(new CachedShip { AgentId = AgentId, Symbol = "AGENT-1", ShipType = "COMMAND", Status = "DOCKED" });
            db.Ships.Add(new CachedShip { AgentId = AgentId, Symbol = "AGENT-3", ShipType = "SHIP_MINING_DRONE", Status = "DOCKED" });
            db.LedgerEntries.Add(Ledger("AGENT-3", LedgerCategory.ShipPurchase, -45_000));
            db.LedgerEntries.Add(Ledger("AGENT-3", LedgerCategory.MountPurchase, -3_000));
            db.LedgerEntries.Add(Ledger("AGENT-3", LedgerCategory.ModulePurchase, -2_000));
            db.LedgerEntries.Add(Ledger("AGENT-3", LedgerCategory.FuelPurchase, -100));
            db.LedgerEntries.Add(Ledger("AGENT-3", LedgerCategory.TradeBuy, -900));
            db.LedgerEntries.Add(Ledger("AGENT-1", LedgerCategory.TradeSell, 700));
            await db.SaveChangesAsync();
        }

        IReadOnlyCollection<ShipMetricsSample> ships = [];
        _metrics.When(m => m.Fleet(Arg.Any<IReadOnlyCollection<ShipMetricsSample>>(), Arg.Any<DateTimeOffset>()))
            .Do(call => ships = call.Arg<IReadOnlyCollection<ShipMetricsSample>>());

        using var service = new PrometheusMetricsService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            _metrics,
            new ShipStateJournal(NullLogger<ShipStateJournal>.Instance),
            new PurchaseNeeds(),
            new FullHoldSavings(),
            _names,
            NullLogger<PrometheusMetricsService>.Instance);
        await service.SampleAsync(CancellationToken.None);

        ships.Select(s => (s.Ship, s.Value)).Should().BeEquivalentTo(new[]
        {
            ("AGENT-1", 0L),
            ("AGENT-3", 50_000L),
        });
    }

    /// <summary>
    /// The fleet table shows what each ship can do next to what it does. Its cached type is the registration role after
    /// startup sync, EXCAVATOR for a mining drone and a siphon drone alike, though a siphon drone can't mine: what a ship
    /// can do is what its mounts, hold and tank allow, whichever plans are on.
    /// </summary>
    [Fact]
    public async Task SampleAsync_SaysWhatEachShipCanDo_ByItsEquipment_NotItsCachedType()
    {
        using var provider = BuildProvider();
        await using (var seedScope = provider.CreateAsyncScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<SpaceTradersDbContext>();
            db.Ships.Add(Equipped("AGENT-1", "COMMAND", ["MOUNT_SENSOR_ARRAY_II", "MOUNT_GAS_SIPHON_II", "MOUNT_MINING_LASER_II", "MOUNT_SURVEYOR_II"], cargo: 40, fuel: 400));
            db.Ships.Add(Equipped("AGENT-2", "SATELLITE", [], cargo: 0, fuel: 0));
            db.Ships.Add(Equipped("AGENT-3", "EXCAVATOR", ["MOUNT_MINING_LASER_I"], cargo: 15, fuel: 80));
            db.Ships.Add(Equipped("AGENT-4", "EXCAVATOR", ["MOUNT_GAS_SIPHON_I"], cargo: 15, fuel: 80));
            db.Ships.Add(Equipped("AGENT-5", "SURVEYOR", ["MOUNT_SURVEYOR_I"], cargo: 0, fuel: 80));
            db.Ships.Add(Equipped("AGENT-6", "HAULER", ["MOUNT_SENSOR_ARRAY_I"], cargo: 80, fuel: 600));

            // Bought since the last restart: cached with the shipyard's type, and no mounts until the next startup sync.
            db.Ships.Add(Equipped("AGENT-7", "SHIP_SIPHON_DRONE", [], cargo: 15, fuel: 80));
            await db.SaveChangesAsync();
        }

        IReadOnlyCollection<ShipMetricsSample> ships = [];
        _metrics.When(m => m.Fleet(Arg.Any<IReadOnlyCollection<ShipMetricsSample>>(), Arg.Any<DateTimeOffset>()))
            .Do(call => ships = call.Arg<IReadOnlyCollection<ShipMetricsSample>>());

        using var service = new PrometheusMetricsService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            _metrics,
            new ShipStateJournal(NullLogger<ShipStateJournal>.Instance),
            new PurchaseNeeds(),
            new FullHoldSavings(),
            _names,
            NullLogger<PrometheusMetricsService>.Instance);
        await service.SampleAsync(CancellationToken.None);

        // Slice 6.6 (D65): a ship with a hold and a tank can build the jump gate, unless it is a drone.
        ships.Select(s => (s.Ship, s.Capabilities)).Should().BeEquivalentTo(new[]
        {
            ("AGENT-1", "Survey, Mine, Siphon, Trade, Construct"),
            ("AGENT-2", "none"),
            ("AGENT-3", "Mine, Trade"),
            ("AGENT-4", "Siphon, Trade"),
            ("AGENT-5", "Survey"),
            ("AGENT-6", "Trade, Construct"),
            ("AGENT-7", "Siphon, Trade"),
        });
    }

    /// <summary>
    /// The dashboard's settings table (slice 2.9): every setting the agent has, with its value, the value the next run
    /// starts with (D69) and what it does. A value that may hold a secret is hidden, as in <c>SettingChanged</c>. A
    /// setting keeps the description it was seeded with, so the cluster's agent, registered before slice 6.3, still
    /// describes the probe plan of before; the table says what the running version does.
    /// </summary>
    [Fact]
    public async Task SampleAsync_ExportsEverySetting_WithSecretsHidden_WhatTheNextRunStartsWith_AndWhatItDoesNow()
    {
        using var provider = BuildProvider();
        await using (var seedScope = provider.CreateAsyncScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<SpaceTradersDbContext>();
            db.Settings.Add(Setting(AgentId, "Automation.Plan.ProbeDeployment.Enabled", "true", "Run the probe plan: park a probe at every market and shipyard in the HQ system (buys probes)"));
            db.Settings.Add(Setting(AgentId, "Automation.MiningShipPercentage", "0.5", "Fraction of mining-capable ships assigned to resource extraction roles"));
            db.Settings.Add(Setting(AgentId, "Alerts.WebhookUrl", "https://hooks.example.com/services/T000/B000/s3cr3t", "Slack/webhook URL for operator alerts (empty = disabled)"));
            db.Settings.Add(Setting(AgentId, "Custom.ReportUrl", string.Empty, string.Empty));
            db.Settings.Add(Setting("AGENT@2026-09-20", "Automation.Enabled", "false", "Master kill-switch for automation"));
            db.NextRunSettings.Add(new NextRunSetting { Key = "Automation.MiningShipPercentage", Value = "0.6" });
            db.NextRunSettings.Add(new NextRunSetting { Key = "Alerts.WebhookUrl", Value = "https://hooks.example.com/services/T000/B000/s3cr3t" });
            await db.SaveChangesAsync();
        }

        IReadOnlyCollection<SettingMetricsSample> settings = [];
        _metrics.When(m => m.Settings(Arg.Any<IReadOnlyCollection<SettingMetricsSample>>()))
            .Do(call => settings = call.Arg<IReadOnlyCollection<SettingMetricsSample>>());

        using var service = new PrometheusMetricsService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            _metrics,
            new ShipStateJournal(NullLogger<ShipStateJournal>.Instance),
            new PurchaseNeeds(),
            new FullHoldSavings(),
            _names,
            NullLogger<PrometheusMetricsService>.Instance);
        await service.SampleAsync(CancellationToken.None);

        var probePlan = DefaultSettingsSeed.DescriptionOf("Automation.Plan.ProbeDeployment.Enabled");
        probePlan.Should().StartWith("Run the probe plan: a probe for every market");
        settings.Should().BeEquivalentTo(new[]
        {
            new SettingMetricsSample("Automation.Plan.ProbeDeployment.Enabled", "true", "true", probePlan),
            new SettingMetricsSample("Automation.MiningShipPercentage", "0.5", "0.6", "Fraction of mining-capable ships assigned to resource extraction roles"),
            new SettingMetricsSample("Alerts.WebhookUrl", "(hidden)", "(hidden)", "Slack/webhook URL for operator alerts (empty = disabled)"),
            new SettingMetricsSample("Custom.ReportUrl", string.Empty, string.Empty, string.Empty),
        });
    }

    /// <summary>
    /// The roles table (slice 6.9) shows the role board while it is on. Switched off, the plans no longer read the roles
    /// its state keeps, so the table shows none rather than roles nobody follows.
    /// </summary>
    [Theory]
    [InlineData("true", 1)]
    [InlineData("false", 0)]
    public async Task SampleAsync_ExportsTheRoleBoard_OnlyWhileItIsOn(string boardSwitch, int ships)
    {
        var state = new RolePlanState
        {
            EvaluatedAt = TimeProvider.System.GetUtcNow(),
            Ships = [new RoleShipState { ShipSymbol = "AGENT-1", Role = FleetRole.Survey, Reason = "survey_first", Since = TimeProvider.System.GetUtcNow() }],
        };
        using var provider = BuildProvider();
        await using (var seedScope = provider.CreateAsyncScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<SpaceTradersDbContext>();
            db.Settings.Add(Setting(AgentId, "Automation.Plan.Roles.Enabled", boardSwitch, "Run the role board"));
            db.PlanStates.Add(new PlanStateRecord { AgentId = AgentId, PlanType = PlanTypes.Roles, StateJson = JsonSerializer.Serialize(state) });
            await db.SaveChangesAsync();
        }

        IReadOnlyCollection<RoleMetricsSample>? roles = null;
        _metrics.When(m => m.Roles(Arg.Any<IReadOnlyCollection<RoleMetricsSample>>()))
            .Do(call => roles = call.Arg<IReadOnlyCollection<RoleMetricsSample>>());

        using var service = new PrometheusMetricsService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            _metrics,
            new ShipStateJournal(NullLogger<ShipStateJournal>.Instance),
            new PurchaseNeeds(),
            new FullHoldSavings(),
            _names,
            NullLogger<PrometheusMetricsService>.Instance);
        await service.SampleAsync(CancellationToken.None);

        roles.Should().NotBeNull().And.HaveCount(ships);
    }

    /// <summary>
    /// Slice 6.10b (D43): the dashboard shows what each plan would buy, so that credits saved up for a cargo ship, while
    /// probes and drones wait, read as the order the user asked for and not as a bot that stopped buying.
    /// </summary>
    [Fact]
    public async Task SampleAsync_ExportsWhatEachPlanWouldBuy_InTheOrderShipsAreBoughtIn()
    {
        using var provider = BuildProvider();
        var needs = new PurchaseNeeds();
        needs.Report(AutomationPlan.ProbeDeployment, new PurchaseNeed(PurchaseTier.Probes, "SHIP_PROBE", "X1-AB-A2", 77_117), TimeProvider.System.GetUtcNow());
        needs.Report(AutomationPlan.Mining, PurchaseNeed.None, TimeProvider.System.GetUtcNow());
        IReadOnlyCollection<PurchaseNeedMetricsSample> exported = [];
        _metrics.When(m => m.PurchaseNeeds(Arg.Any<IReadOnlyCollection<PurchaseNeedMetricsSample>>()))
            .Do(call => exported = call.Arg<IReadOnlyCollection<PurchaseNeedMetricsSample>>());

        using var service = new PrometheusMetricsService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            _metrics,
            new ShipStateJournal(NullLogger<ShipStateJournal>.Instance),
            needs,
            new FullHoldSavings(),
            _names,
            NullLogger<PrometheusMetricsService>.Instance);
        await service.SampleAsync(CancellationToken.None);

        // Slice 6.6 (D64) put the jump gate's materials at 6, after the cargo ships: the probes moved to 7.
        exported.Should().Equal(new PurchaseNeedMetricsSample("ProbeDeployment", "Probes", 7, "SHIP_PROBE", "X1-AB-A2", 77_117));
    }

    /// <summary>
    /// D51: the dashboard shows the credit reserve next to the credits: the floor, and 1,000 a unit of what the ships that
    /// trade can carry. The command ship and a light shuttle carry 40 each; a mining drone counts only while the role board
    /// has it trading. D56: and the dearest full hold a trader saves up for. D57: and what the trade trips on their way to
    /// buy hold back.
    /// </summary>
    [Theory]
    [InlineData("false", 0, 0, false, 140_000)]
    [InlineData("true", 0, 0, false, 155_000)]
    [InlineData("false", 130_312, 0, false, 270_312)]
    [InlineData("false", 0, 130_160, false, 270_160)]
    [InlineData("false", 0, 130_160, true, 140_000)]
    public async Task SampleAsync_ExportsTheCreditReserve_ByWhatTheShipsThatTradeCanCarry(string boardSwitch, long saving, long hold, bool bought, long reserve)
    {
        var savings = new FullHoldSavings();
        if (saving > 0)
        {
            savings.SaveFor("AGENT-1", TradeRoutePlanner.RouteKey("EQUIPMENT", "X1-AB-K85", "X1-AB-D41"), saving);
        }

        var state = new RolePlanState
        {
            EvaluatedAt = TimeProvider.System.GetUtcNow(),
            Ships = [new RoleShipState { ShipSymbol = "AGENT-3", Role = FleetRole.Trade, Reason = "most_profitable", Since = TimeProvider.System.GetUtcNow() }],
        };
        using var provider = BuildProvider();
        await using (var seedScope = provider.CreateAsyncScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<SpaceTradersDbContext>();
            db.Ships.Add(Equipped("AGENT-1", "COMMAND", ["MOUNT_SURVEYOR_II", "MOUNT_MINING_LASER_II"], 40, 400));
            db.Ships.Add(Equipped("AGENT-2", "SATELLITE", [], 0, 0));
            db.Ships.Add(Equipped("AGENT-3", "EXCAVATOR", ["MOUNT_MINING_LASER_I"], 15, 80));
            var shuttle = Equipped("AGENT-4", "SHIP_LIGHT_SHUTTLE", ["MOUNT_TURRET_I"], 40, 300);
            if (hold > 0)
            {
                ShipGoal trip = new TradeBetweenMarketsGoal { TradeSymbol = "EQUIPMENT", BuyWaypointSymbol = "X1-AB-K85", SellWaypointSymbol = "X1-AB-D41", Units = 40, ReservedCredits = hold, CargoBought = bought };
                shuttle.GoalId = trip.GoalId;
                shuttle.GoalKind = trip.Kind.ToString();
                shuttle.GoalPayloadJson = JsonSerializer.Serialize(trip);
                shuttle.GoalStatus = (int)trip.Status;
            }

            db.Ships.Add(shuttle);
            db.Settings.Add(Setting(AgentId, "FleetExpansion.MinCreditReserve", "60000", "The floor"));
            db.Settings.Add(Setting(AgentId, "FleetExpansion.ReservePerTradingCargoUnit", "1000", "A unit"));
            db.Settings.Add(Setting(AgentId, "Automation.Plan.Roles.Enabled", boardSwitch, "Run the role board"));
            db.PlanStates.Add(new PlanStateRecord { AgentId = AgentId, PlanType = PlanTypes.Roles, StateJson = JsonSerializer.Serialize(state) });
            await db.SaveChangesAsync();
        }

        using var service = new PrometheusMetricsService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            _metrics,
            new ShipStateJournal(NullLogger<ShipStateJournal>.Instance),
            new PurchaseNeeds(),
            savings,
            _names,
            NullLogger<PrometheusMetricsService>.Instance);
        await service.SampleAsync(CancellationToken.None);

        _metrics.Received(1).ReservedCredits(reserve);
    }

    private static AgentSetting Setting(string agentId, string key, string value, string description)
        => new() { AgentId = agentId, Key = key, Value = value, Type = "string", Description = description };

    /// <summary>A docked ship with its mounts, hold and tank, cached as startup sync and the ship repository store them.</summary>
    private static CachedShip Equipped(string symbol, string shipType, string[] mounts, int cargo, int fuel) => new()
    {
        AgentId = AgentId,
        Symbol = symbol,
        ShipType = shipType,
        Status = "DOCKED",
        WaypointSymbol = "X1-AB-H51",
        MountsJson = JsonSerializer.Serialize(mounts),
        CargoCapacity = cargo,
        FuelCurrent = fuel,
        FuelCapacity = fuel,
    };

    private static LedgerEntry Ledger(string ship, LedgerCategory category, long amount)
        => new() { AgentId = AgentId, ShipSymbol = ship, Category = category, Amount = amount, OccurredAt = TimeProvider.System.GetUtcNow() };

    private static CachedWaypoint Waypoint(string symbol, string type)
        => new() { AgentId = AgentId, Symbol = symbol, SystemSymbol = "X1-AB", Type = type };

    private static ShipAssignmentRecord ContractAssignment(string ship) => new()
    {
        AgentId = AgentId,
        ShipSymbol = ship,
        Type = "Contract",
        OriginWaypoint = "X1-AB-XB5C",
        DestWaypoint = "X1-AB-H51",
        CargoSymbol = "COPPER_ORE",
        ContractId = "C-1",
    };

    /// <summary>
    /// Slice 6.6: the fleet view says what a builder does, the jump gate's materials feed the dashboard's progress, and what a
    /// construction trip on its way to buy holds back is in the credit reserve (D64).
    /// </summary>
    [Fact]
    public async Task SampleAsync_SaysWhatABuilderDoes_AndExportsTheJumpGatesProgress()
    {
        using var provider = BuildProvider();
        await using (var seedScope = provider.CreateAsyncScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<SpaceTradersDbContext>();
            var buying = new SupplyConstructionGoal { TradeSymbol = "FAB_MATS", ConstructionSiteWaypointSymbol = "X1-AB-I55", BuyWaypointSymbol = "X1-AB-F49", Units = 80, ReservedCredits = 168_000 };
            var supplying = new SupplyConstructionGoal { TradeSymbol = "ADVANCED_CIRCUITRY", ConstructionSiteWaypointSymbol = "X1-AB-I55", BuyWaypointSymbol = "X1-AB-D42", Units = 40, CargoBought = true };
            foreach (var (symbol, goal) in new[] { ("AGENT-6", buying), ("AGENT-7", supplying) })
            {
                db.Ships.Add(new CachedShip
                {
                    AgentId = AgentId,
                    Symbol = symbol,
                    ShipType = "SHIP_LIGHT_HAULER",
                    Status = "DOCKED",
                    WaypointSymbol = "X1-AB-H51",
                    GoalId = goal.GoalId,
                    GoalKind = goal.Kind.ToString(),
                    GoalPayloadJson = JsonSerializer.Serialize<ShipGoal>(goal),
                    GoalStatus = (int)goal.Status,
                });
            }

            db.ConstructionSites.Add(new CachedConstruction
            {
                AgentId = AgentId,
                WaypointSymbol = "X1-AB-I55",
                SystemSymbol = "X1-AB",
                MaterialsJson = JsonSerializer.Serialize(new List<ConstructionMaterialModel> { new("FAB_MATS", 1_600, 400), new("ADVANCED_CIRCUITRY", 400, 0) }),
                LastObservedAt = TimeProvider.System.GetUtcNow(),
            });
            await db.SaveChangesAsync();
        }

        IReadOnlyCollection<ShipMetricsSample> ships = [];
        IReadOnlyCollection<ConstructionMetricsSample> materials = [];
        _metrics.When(m => m.Fleet(Arg.Any<IReadOnlyCollection<ShipMetricsSample>>(), Arg.Any<DateTimeOffset>()))
            .Do(call => ships = call.Arg<IReadOnlyCollection<ShipMetricsSample>>());
        _metrics.When(m => m.Construction(Arg.Any<IReadOnlyCollection<ConstructionMetricsSample>>()))
            .Do(call => materials = call.Arg<IReadOnlyCollection<ConstructionMetricsSample>>());

        using var service = new PrometheusMetricsService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            _metrics,
            new ShipStateJournal(NullLogger<ShipStateJournal>.Instance),
            new PurchaseNeeds(),
            new FullHoldSavings(),
            _names,
            NullLogger<PrometheusMetricsService>.Instance);
        await service.SampleAsync(CancellationToken.None);

        ships.Select(s => (s.Ship, s.Activity)).Should().BeEquivalentTo(new[]
        {
            ("AGENT-6", "buying FAB_MATS at X1-AB-F49 for X1-AB-I55"),
            ("AGENT-7", "supplying ADVANCED_CIRCUITRY to X1-AB-I55"),
        });
        materials.Should().BeEquivalentTo(new[]
        {
            new ConstructionMetricsSample("X1-AB-I55", "FAB_MATS", 1_600, 400),
            new ConstructionMetricsSample("X1-AB-I55", "ADVANCED_CIRCUITRY", 400, 0),
        });
        _metrics.Received(1).ReservedCredits(168_000);
    }

    private static AgentDataScope Agent()
    {
        var agent = new AgentDataScope();
        agent.Set(AgentId);
        return agent;
    }

    private static ServiceProvider BuildProvider()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var services = new ServiceCollection();
        services.AddSingleton<IAgentDataScope>(_ =>
        {
            var scope = new AgentDataScope();
            scope.Set(AgentId);
            return scope;
        });
        services.AddDbContext<SpaceTradersDbContext>(options => options.UseInMemoryDatabase(databaseName));
        return services.BuildServiceProvider();
    }
}

public sealed class PrometheusAutomationMetricsTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 01, 12, 00, 00, TimeSpan.Zero);

    private readonly CollectorRegistry _registry = Metrics.NewCustomRegistry();
    private readonly PrometheusAutomationMetrics _metrics;

    public PrometheusAutomationMetricsTests()
    {
        _metrics = new PrometheusAutomationMetrics(_registry, Agent("AGENT@2026-09-27"));
    }

    [Fact]
    public async Task AScrape_ListsEveryMetric_BeforeAnyHasAValue()
    {
        var text = await ExportAsync();

        foreach (var name in MetricsEndpointTests.ExpectedMetrics)
        {
            text.Should().Contain($"# TYPE {name} ");
        }
    }

    /// <summary>Every gauge without labels, each with a value it had on the cluster.</summary>
    public static TheoryData<string, Action<IAutomationMetrics>, string> UnlabelledGauges => new()
    {
        { "spacetraders_agent_credits", metrics => metrics.Credits(145_028), "spacetraders_agent_credits{reset_date=\"2026-09-27\"} 145028\n" },
        { "spacetraders_credit_reserve", metrics => metrics.ReservedCredits(100_000), "spacetraders_credit_reserve{reset_date=\"2026-09-27\"} 100000\n" },
        { "spacetraders_db_size_bytes", metrics => metrics.DatabaseSize(15_742_655), "spacetraders_db_size_bytes{reset_date=\"2026-09-27\"} 15742655\n" },
        { "spacetraders_server_next_reset_timestamp_seconds", metrics => metrics.NextServerReset(DateTimeOffset.FromUnixTimeSeconds(1_791_118_800)), "spacetraders_server_next_reset_timestamp_seconds{reset_date=\"2026-09-27\"} 1791118800\n" },
    };

    /// <summary>
    /// B52: a gauge without labels was published at 0 from the start, before the bot knew its value, and Prometheus's
    /// first scrape of a new pod could come before the first sample. On 2026-10-03 at 07:28Z the dashboard read 0 credits
    /// for a minute after a deploy: "Value gained per hour" fell from 56,896 to -517,672, to show the same amount as a
    /// gain an hour later. Such a gauge is listed from the start, but has a series only once it has a value.
    /// </summary>
    [Theory]
    [MemberData(nameof(UnlabelledGauges))]
    public async Task AGaugeWithoutLabels_HasNoSeries_UntilItHasAValue(string name, Action<IAutomationMetrics> set, string series)
    {
        var text = await ExportAsync();
        text.Should().Contain($"# TYPE {name} gauge\n");
        text.Should().NotContain($"\n{name}{{", "{0} has no value yet, and 0 isn't it", name);

        set(_metrics);

        (await ExportAsync()).Should().Contain(series);
    }

    /// <summary>
    /// Every counter, each with what its first increment looked like on the cluster: the contract's
    /// deposit and the drone, booked before Prometheus first scraped the pod; a single 429; a single
    /// circuit breaker trip.
    /// </summary>
    public static TheoryData<string, Action<IAutomationMetrics>, string> FirstIncrements => new()
    {
        { "credits earned", metrics => metrics.CreditsEarned("ContractDeposit", 4_267), "spacetraders_credits_earned_total{reset_date=\"2026-09-27\",source=\"ContractDeposit\"} " },
        { "credits spent", metrics => metrics.CreditsSpent("ShipPurchase", 46_885), "spacetraders_credits_spent_total{reset_date=\"2026-09-27\",category=\"ShipPurchase\"} " },
        { "429s", metrics => metrics.ApiThrottled("rate_limiter"), "spacetraders_api_throttled_total{reset_date=\"2026-09-27\",source=\"rate_limiter\"} " },
        { "API requests initiated", metrics => metrics.ApiRequestInitiated("POST", "my/ships/{shipSymbol}/extract"), "spacetraders_api_requests_initiated_total{reset_date=\"2026-09-27\",method=\"POST\",endpoint=\"my/ships/{shipSymbol}/extract\"} " },
        { "API responses", metrics => metrics.ApiRequest("POST", "my/ships/{shipSymbol}/extract", "429"), "spacetraders_api_requests_total{reset_date=\"2026-09-27\",method=\"POST\",endpoint=\"my/ships/{shipSymbol}/extract\",status=\"429\"} " },
        { "breaker trips", metrics => metrics.GoalBreakerTripped("AGENT-3"), "spacetraders_goal_breaker_trips_total{reset_date=\"2026-09-27\",ship=\"AGENT-3\"} " },
        { "goal steps", metrics => metrics.GoalStep("ScoutWaypoint"), "spacetraders_goal_steps_total{reset_date=\"2026-09-27\",kind=\"ScoutWaypoint\"} " },
        { "messages", metrics => metrics.MessageHandled("ContractAcceptedEvent"), "spacetraders_messages_handled_total{reset_date=\"2026-09-27\",type=\"ContractAcceptedEvent\"} " },
        { "rate-limit waits", metrics => metrics.RateLimitWait(TimeSpan.FromSeconds(3), "read"), "spacetraders_api_rate_limit_wait_seconds_total{reset_date=\"2026-09-27\",kind=\"read\"} " },
        { "units extracted", metrics => metrics.Extracted("AGENT-3", "COPPER_ORE", 2), "spacetraders_extracted_units_total{reset_date=\"2026-09-27\",ship=\"AGENT-3\",good=\"COPPER_ORE\"} " },
        { "units jettisoned", metrics => metrics.Jettisoned("AGENT-3", "SILICON_CRYSTALS", 2), "spacetraders_jettisoned_units_total{reset_date=\"2026-09-27\",ship=\"AGENT-3\",good=\"SILICON_CRYSTALS\"} " },
        { "extractions", metrics => metrics.Extraction("AGENT-3", surveyed: true), "spacetraders_extractions_total{reset_date=\"2026-09-27\",ship=\"AGENT-3\",surveyed=\"true\"} " },
        { "surveys taken", metrics => metrics.SurveyTaken("X1-AB-XB5C", "MODERATE"), "spacetraders_surveys_taken_total{reset_date=\"2026-09-27\",waypoint=\"X1-AB-XB5C\",size=\"MODERATE\"} " },
        { "surveys ended", metrics => metrics.SurveyEnded("X1-AB-XB5C", "expired", used: false), "spacetraders_surveys_ended_total{reset_date=\"2026-09-27\",waypoint=\"X1-AB-XB5C\",reason=\"expired\",used=\"false\"} " },
        { "units sold", metrics => metrics.GoodsSold("X1-DC53-H51", "HYDROCARBON", 18), "spacetraders_goods_sold_units_total{reset_date=\"2026-09-27\",system=\"X1-DC53\",waypoint=\"X1-DC53-H51\",good=\"HYDROCARBON\"} " },
        { "units bought", metrics => metrics.GoodsBought("X1-DC53-K85", "PLASTICS", 20), "spacetraders_goods_bought_units_total{reset_date=\"2026-09-27\",system=\"X1-DC53\",waypoint=\"X1-DC53-K85\",good=\"PLASTICS\"} " },
        { "trips", metrics => metrics.TripEnded("trade"), "spacetraders_trips_total{reset_date=\"2026-09-27\",activity=\"trade\"} " },
        { "trip profit", metrics => metrics.TripProfit("contract", 4_267), "spacetraders_trip_profit_credits_total{reset_date=\"2026-09-27\",activity=\"contract\"} " },
        { "trip loss", metrics => metrics.TripProfit("trade", -65_232), "spacetraders_trip_loss_credits_total{reset_date=\"2026-09-27\",activity=\"trade\"} " },
    };

    /// <summary>
    /// D46: a counter can't go down, so what trips lose is counted apart from what they make, and profit minus loss is
    /// what an activity made after fuel. Both series exist once an activity has a trip: PromQL's <c>profit - loss</c>
    /// would drop an activity that had no loss series yet.
    /// </summary>
    [Fact]
    public async Task WhatTripsMake_AndWhatTheyLose_AreCountedApart_ByActivity()
    {
        _metrics.TripProfit("trade", 4_320);
        _metrics.TripProfit("trade", -2_152);
        _metrics.TripProfit("mining", 1_005);
        await ExportAsync();

        var text = await ExportAsync();

        text.Should().Contain("spacetraders_trip_profit_credits_total{reset_date=\"2026-09-27\",activity=\"trade\"} 4320\n");
        text.Should().Contain("spacetraders_trip_loss_credits_total{reset_date=\"2026-09-27\",activity=\"trade\"} 2152\n");
        text.Should().Contain("spacetraders_trip_profit_credits_total{reset_date=\"2026-09-27\",activity=\"mining\"} 1005\n");
        text.Should().Contain("spacetraders_trip_loss_credits_total{reset_date=\"2026-09-27\",activity=\"mining\"} 0\n");
    }

    /// <summary>
    /// B43: Prometheus's increase() and rate() count what a series gains between two scrapes, never
    /// the value Prometheus first sees. A series that held its first increment when it was first
    /// scraped never showed it on the dashboard: the contract's 4,267 and the drone's 46,885 were
    /// missing from the ledger panels, and a single 429 or breaker trip could never show at all.
    /// </summary>
    [Theory]
    [MemberData(nameof(FirstIncrements))]
    public async Task ACountersFirstIncrement_ReachesPrometheusAfterItHasScrapedTheSeriesAtZero(
        string counter, Action<IAutomationMetrics> increment, string series)
    {
        increment(_metrics);

        (await ExportAsync()).Should().Contain(series + "0", "the first scrape of {0} must see the series at 0", counter);
        (await ExportAsync()).Should().NotContain(series + "0\n", "the scrape after that must see {0}' first increment", counter);
    }

    [Fact]
    public async Task ASeriesThatFirstAppearsLater_IsScrapedAtZeroFirstToo()
    {
        await ExportAsync();
        await ExportAsync();

        _metrics.CreditsSpent("ShipPurchase", 46_885);

        (await ExportAsync()).Should().Contain("spacetraders_credits_spent_total{reset_date=\"2026-09-27\",category=\"ShipPurchase\"} 0\n");
        (await ExportAsync()).Should().Contain("spacetraders_credits_spent_total{reset_date=\"2026-09-27\",category=\"ShipPurchase\"} 46885\n");
    }

    [Fact]
    public async Task ASeriesPrometheusHasSeenAtZero_CountsAtOnce()
    {
        _metrics.CreditsEarned("TradeSell", 450);
        await ExportAsync();
        (await ExportAsync()).Should().Contain("spacetraders_credits_earned_total{reset_date=\"2026-09-27\",source=\"TradeSell\"} 450\n");

        _metrics.CreditsEarned("TradeSell", 50);

        (await ExportAsync()).Should().Contain("spacetraders_credits_earned_total{reset_date=\"2026-09-27\",source=\"TradeSell\"} 500\n");
    }

    [Fact]
    public async Task AShip_KeepsWhenItEnteredItsState_UntilItsStateChanges()
    {
        _metrics.Fleet([new ShipMetricsSample("AGENT-1", "COMMAND", "DOCKED", "None", string.Empty)], Start);
        _metrics.Fleet([new ShipMetricsSample("AGENT-1", "COMMAND", "DOCKED", "None", string.Empty)], Start.AddMinutes(1));

        (await ExportAsync()).Should().Contain(StatusLine("AGENT-1", "COMMAND", "DOCKED", "None", string.Empty, Start));

        _metrics.Fleet([new ShipMetricsSample("AGENT-1", "COMMAND", "IN_TRANSIT", "None", string.Empty)], Start.AddMinutes(2));

        var text = await ExportAsync();
        text.Should().Contain(StatusLine("AGENT-1", "COMMAND", "IN_TRANSIT", "None", string.Empty, Start.AddMinutes(2)));
        text.Should().NotContain("state=\"DOCKED\",goal");
        text.Should().Contain("spacetraders_ships{reset_date=\"2026-09-27\",role=\"COMMAND\",state=\"DOCKED\"} 0");
        text.Should().Contain("spacetraders_ships{reset_date=\"2026-09-27\",role=\"COMMAND\",state=\"IN_TRANSIT\"} 1");
    }

    [Fact]
    public async Task AShipThatIsGone_LosesItsSeries()
    {
        _metrics.Fleet([new ShipMetricsSample("AGENT-1", "COMMAND", "DOCKED", "None", string.Empty)], Start);

        _metrics.Fleet([], Start.AddMinutes(1));

        (await ExportAsync()).Should().NotContain("ship=\"AGENT-1\"");
    }

    /// <summary>
    /// The fleet table: where a ship is, what it does, and its hold. Series that no longer hold are
    /// removed, so the table shows one row per ship and the hold only what is in it.
    /// </summary>
    [Fact]
    public async Task AShip_ShowsWhereItIsWhatItDoesAndWhatItCarries()
    {
        _metrics.Fleet([Drone("X1-AB-XB5C (ENGINEERED_ASTEROID)", "mining COPPER_ORE", [new("COPPER_ORE", 9), new("SILICON_CRYSTALS", 2)])], Start);

        var text = await ExportAsync();
        text.Should().Contain("spacetraders_ship_info{reset_date=\"2026-09-27\",ship=\"AGENT-3\",location=\"X1-AB-XB5C (ENGINEERED_ASTEROID)\",activity=\"mining COPPER_ORE\"} 1\n");
        text.Should().Contain("spacetraders_ship_cargo_units{reset_date=\"2026-09-27\",ship=\"AGENT-3\",good=\"COPPER_ORE\"} 9\n");
        text.Should().Contain("spacetraders_ship_cargo_units{reset_date=\"2026-09-27\",ship=\"AGENT-3\",good=\"SILICON_CRYSTALS\"} 2\n");
        text.Should().Contain("spacetraders_ship_cargo_capacity_units{reset_date=\"2026-09-27\",ship=\"AGENT-3\"} 15\n");
        text.Should().Contain("spacetraders_ship_value_credits{reset_date=\"2026-09-27\",ship=\"AGENT-3\"} 50000\n");
        text.Should().NotContain("spacetraders_ship_arrival_timestamp_seconds{");

        // It jettisoned the crystals, filled up, and is on its way to deliver.
        _metrics.Fleet([Drone("→ X1-AB-H51 (PLANET)", "on the way to deliver COPPER_ORE", [new("COPPER_ORE", 15)], Start.AddMinutes(4))], Start.AddMinutes(1));

        text = await ExportAsync();
        text.Should().Contain("spacetraders_ship_info{reset_date=\"2026-09-27\",ship=\"AGENT-3\",location=\"→ X1-AB-H51 (PLANET)\",activity=\"on the way to deliver COPPER_ORE\"} 1\n");
        text.Should().NotContain("activity=\"mining COPPER_ORE\"");
        text.Should().Contain("spacetraders_ship_cargo_units{reset_date=\"2026-09-27\",ship=\"AGENT-3\",good=\"COPPER_ORE\"} 15\n");
        text.Should().NotContain("good=\"SILICON_CRYSTALS\"");
        text.Should().Contain($"spacetraders_ship_arrival_timestamp_seconds{{reset_date=\"2026-09-27\",ship=\"AGENT-3\"}} {Start.AddMinutes(4).ToUnixTimeSeconds()}\n");

        // Arrived: no arrival time any more.
        _metrics.Fleet([Drone("X1-AB-H51 (PLANET)", "delivering COPPER_ORE", [new("COPPER_ORE", 15)])], Start.AddMinutes(4));

        (await ExportAsync()).Should().NotContain("spacetraders_ship_arrival_timestamp_seconds{");
    }

    /// <summary>
    /// The fleet table shows what each ship can do next to what it does, one series per ship: a ship whose equipment
    /// changes loses its old series, and a ship that is gone loses its own.
    /// </summary>
    [Fact]
    public async Task WhatAShipCanDo_IsOneSeriesPerShip_ThatFollowsItsEquipment()
    {
        var probe = new ShipMetricsSample("AGENT-2", "SATELLITE", "DOCKED", "None", string.Empty) { Capabilities = "none" };
        var drone = Drone("X1-AB-XB5C (ENGINEERED_ASTEROID)", "mining COPPER_ORE", []) with { Capabilities = "Mine, Trade" };
        _metrics.Fleet([probe, drone], Start);

        var text = await ExportAsync();
        text.Should().Contain("spacetraders_ship_capabilities_info{reset_date=\"2026-09-27\",ship=\"AGENT-2\",can=\"none\"} 1\n");
        text.Should().Contain("spacetraders_ship_capabilities_info{reset_date=\"2026-09-27\",ship=\"AGENT-3\",can=\"Mine, Trade\"} 1\n");

        // A surveyor mount installed on the drone; the probe scrapped.
        _metrics.Fleet([drone with { Capabilities = "Survey, Mine, Trade" }], Start.AddMinutes(1));

        text = await ExportAsync();
        text.Should().Contain("spacetraders_ship_capabilities_info{reset_date=\"2026-09-27\",ship=\"AGENT-3\",can=\"Survey, Mine, Trade\"} 1\n");
        text.Should().NotContain("can=\"Mine, Trade\"");
        text.Should().NotContain("ship=\"AGENT-2\"");
    }

    /// <summary>
    /// Slice 2.14 (D72): the Fleet and Roles tables show each ship's name beside its symbol, one series per ship; a ship whose
    /// name isn't known yet has none, and a ship that is gone loses its own.
    /// </summary>
    [Fact]
    public async Task AShipsName_IsOneSeriesPerShip_BesideItsSymbol()
    {
        var probe = new ShipMetricsSample("AGENT-2", "SATELLITE", "DOCKED", "None", string.Empty) { Name = "SPUTNIK-1", NamedType = "SHIP_PROBE" };
        var drone = Drone("X1-AB-XB5C (ENGINEERED_ASTEROID)", "mining COPPER_ORE", []) with { Name = "PICKAXE-1", NamedType = "SHIP_MINING_DRONE" };
        var unnamed = new ShipMetricsSample("AGENT-4", "COMMAND", "DOCKED", "None", string.Empty);
        _metrics.Fleet([probe, drone, unnamed], Start);

        var text = await ExportAsync();
        text.Should().Contain("spacetraders_ship_name_info{reset_date=\"2026-09-27\",ship=\"AGENT-2\",name=\"SPUTNIK-1\",type=\"SHIP_PROBE\"} 1\n");
        text.Should().Contain("spacetraders_ship_name_info{reset_date=\"2026-09-27\",ship=\"AGENT-3\",name=\"PICKAXE-1\",type=\"SHIP_MINING_DRONE\"} 1\n");
        text.Should().NotContain("spacetraders_ship_name_info{reset_date=\"2026-09-27\",ship=\"AGENT-4\"");

        // The probe gone; the drone's name the same.
        _metrics.Fleet([drone, unnamed], Start.AddMinutes(1));

        text = await ExportAsync();
        text.Should().Contain("ship=\"AGENT-3\",name=\"PICKAXE-1\"");
        text.Should().NotContain("SPUTNIK-1");
    }

    /// <summary>The markets dashboard (slice 2.8): a market's goods with their prices, volume, supply and activity.</summary>
    [Fact]
    public async Task AMarket_ShowsItsGoodsWithPricesVolumeSupplyAndActivity()
    {
        _metrics.Markets([Market(
            new TradeGoodSnapshot("COPPER_ORE", "IMPORT", 60, 55, 60, "SCARCE", "WEAK"),
            new TradeGoodSnapshot("FUEL", "EXCHANGE", 72, 68, 180, "MODERATE", string.Empty))]);

        var text = await ExportAsync();
        text.Should().Contain($"spacetraders_market_observed_timestamp_seconds{{reset_date=\"2026-09-27\",system=\"X1-AB\",waypoint=\"X1-AB-H51\",waypoint_type=\"PLANET\"}} {Start.ToUnixTimeSeconds()}\n");
        text.Should().Contain("spacetraders_market_purchase_price{reset_date=\"2026-09-27\",system=\"X1-AB\",waypoint=\"X1-AB-H51\",good=\"COPPER_ORE\",kind=\"IMPORT\"} 60\n");
        text.Should().Contain("spacetraders_market_sell_price{reset_date=\"2026-09-27\",system=\"X1-AB\",waypoint=\"X1-AB-H51\",good=\"COPPER_ORE\",kind=\"IMPORT\"} 55\n");
        text.Should().Contain("spacetraders_market_trade_volume{reset_date=\"2026-09-27\",system=\"X1-AB\",waypoint=\"X1-AB-H51\",good=\"COPPER_ORE\",kind=\"IMPORT\"} 60\n");
        text.Should().Contain("spacetraders_market_supply{reset_date=\"2026-09-27\",system=\"X1-AB\",waypoint=\"X1-AB-H51\",good=\"COPPER_ORE\",kind=\"IMPORT\"} 1\n");
        text.Should().Contain("spacetraders_market_activity{reset_date=\"2026-09-27\",system=\"X1-AB\",waypoint=\"X1-AB-H51\",good=\"COPPER_ORE\",kind=\"IMPORT\"} 1\n");
        text.Should().Contain("spacetraders_market_supply{reset_date=\"2026-09-27\",system=\"X1-AB\",waypoint=\"X1-AB-H51\",good=\"FUEL\",kind=\"EXCHANGE\"} 3\n");
        text.Should().NotContain("spacetraders_market_activity{reset_date=\"2026-09-27\",system=\"X1-AB\",waypoint=\"X1-AB-H51\",good=\"FUEL\"");

        // The next visit no longer lists copper; after a reset the new agent knows no markets yet.
        _metrics.Markets([Market(new TradeGoodSnapshot("FUEL", "EXCHANGE", 74, 70, 180, "LIMITED", "GROWING"))]);
        text = await ExportAsync();
        text.Should().NotContain("good=\"COPPER_ORE\"");
        text.Should().Contain("spacetraders_market_purchase_price{reset_date=\"2026-09-27\",system=\"X1-AB\",waypoint=\"X1-AB-H51\",good=\"FUEL\",kind=\"EXCHANGE\"} 74\n");
        text.Should().Contain("spacetraders_market_activity{reset_date=\"2026-09-27\",system=\"X1-AB\",waypoint=\"X1-AB-H51\",good=\"FUEL\",kind=\"EXCHANGE\"} 2\n");

        _metrics.Markets([]);
        (await ExportAsync()).Should().NotContain("waypoint=\"X1-AB-H51\"");
    }

    /// <summary>The systems dashboard (asked on 2026-10-04): what each known system offers, and its gate.</summary>
    [Fact]
    public async Task ASystem_ShowsItsStateGateJumpsSitesRawGoodsAndTrades()
    {
        var explored = new SpaceTraders.Application.Exploring.SystemSample
        {
            System = "X1-KR90",
            State = "explored",
            Gate = "X1-KR90-AF5F",
            GateState = "active",
            Jumps = 1,
            ExploredAt = Start,
            Connections = ["X1-DC53", "X1-VR15"],
            Markets = 7,
            Shipyards = 1,
            Uncharted = 0,
            WaypointTypes = new Dictionary<string, int> { ["ASTEROID"] = 11, ["JUMP_GATE"] = 1 },
            GatheringSites = new Dictionary<string, int> { ["IRON_ORE"] = 4 },
            RawGoods = [new SpaceTraders.Application.Exploring.RawGoodSample { Good = "IRON_ORE", Price = 61, Market = "X1-KR90-A1", Supply = "SCARCE" }],
            Trades = [new SpaceTraders.Application.Exploring.TradeSample { Good = "FOOD", BuyAt = "X1-KR90-B2", SellAt = "X1-KR90-A1", Margin = 420, Volume = 20 }],
        };
        var beyond = new SpaceTraders.Application.Exploring.SystemSample { System = "X1-HZ59", State = "gate_under_construction", Gate = "X1-HZ59-I59", GateState = "under_construction", Jumps = 1 };

        _metrics.Systems([explored, beyond]);

        var text = await ExportAsync();
        text.Should().Contain("spacetraders_system_info{reset_date=\"2026-09-27\",system=\"X1-KR90\",state=\"explored\",gate=\"X1-KR90-AF5F\",gate_state=\"active\"} 1\n");
        text.Should().Contain("spacetraders_system_info{reset_date=\"2026-09-27\",system=\"X1-HZ59\",state=\"gate_under_construction\",gate=\"X1-HZ59-I59\",gate_state=\"under_construction\"} 1\n");
        text.Should().Contain("spacetraders_system_jumps_from_home{reset_date=\"2026-09-27\",system=\"X1-KR90\"} 1\n");
        text.Should().Contain($"spacetraders_system_explored_timestamp_seconds{{reset_date=\"2026-09-27\",system=\"X1-KR90\"}} {Start.ToUnixTimeSeconds()}\n");
        text.Should().NotContain("spacetraders_system_explored_timestamp_seconds{reset_date=\"2026-09-27\",system=\"X1-HZ59\"}");
        text.Should().Contain("spacetraders_system_connection_info{reset_date=\"2026-09-27\",system=\"X1-KR90\",to=\"X1-VR15\"} 1\n");
        text.Should().Contain("spacetraders_system_facilities{reset_date=\"2026-09-27\",system=\"X1-KR90\",kind=\"market\"} 7\n");
        text.Should().Contain("spacetraders_system_facilities{reset_date=\"2026-09-27\",system=\"X1-KR90\",kind=\"shipyard\"} 1\n");
        text.Should().Contain("spacetraders_system_waypoints{reset_date=\"2026-09-27\",system=\"X1-KR90\",type=\"ASTEROID\"} 11\n");
        text.Should().Contain("spacetraders_system_gathering_sites{reset_date=\"2026-09-27\",system=\"X1-KR90\",good=\"IRON_ORE\"} 4\n");
        text.Should().Contain("spacetraders_system_raw_good_price{reset_date=\"2026-09-27\",system=\"X1-KR90\",good=\"IRON_ORE\",market=\"X1-KR90-A1\"} 61\n");
        text.Should().Contain("spacetraders_system_raw_good_supply{reset_date=\"2026-09-27\",system=\"X1-KR90\",good=\"IRON_ORE\"} 1\n");
        text.Should().Contain("spacetraders_system_trade_margin{reset_date=\"2026-09-27\",system=\"X1-KR90\",good=\"FOOD\",buy_at=\"X1-KR90-B2\",sell_at=\"X1-KR90-A1\"} 420\n");
        text.Should().Contain("spacetraders_system_trade_volume{reset_date=\"2026-09-27\",system=\"X1-KR90\",good=\"FOOD\",buy_at=\"X1-KR90-B2\",sell_at=\"X1-KR90-A1\"} 20\n");

        // Iron is no longer bought best at A1, and FOOD no longer pays; after a reset the new agent knows no systems yet.
        _metrics.Systems([explored with { RawGoods = [new SpaceTraders.Application.Exploring.RawGoodSample { Good = "IRON_ORE", Price = 58, Market = "X1-KR90-C3", Supply = "LIMITED" }], Trades = [] }, beyond]);
        text = await ExportAsync();
        text.Should().NotContain("market=\"X1-KR90-A1\"");
        text.Should().Contain("spacetraders_system_raw_good_price{reset_date=\"2026-09-27\",system=\"X1-KR90\",good=\"IRON_ORE\",market=\"X1-KR90-C3\"} 58\n");
        text.Should().NotContain("spacetraders_system_trade_margin{reset_date=\"2026-09-27\",system=\"X1-KR90\"");

        _metrics.Systems([]);
        (await ExportAsync()).Should().NotContain("system=\"X1-KR90\"");
    }

    [Fact]
    public async Task AShipyard_ShowsItsShipTypesAndThePricesItKnows()
    {
        _metrics.Shipyards([Shipyard(["SHIP_MINING_DRONE", "SHIP_PROBE"], new ShipyardShipMetricsSample("SHIP_MINING_DRONE", 46_885, "MODERATE"))]);

        var text = await ExportAsync();
        text.Should().Contain($"spacetraders_shipyard_observed_timestamp_seconds{{reset_date=\"2026-09-27\",system=\"X1-AB\",waypoint=\"X1-AB-H52\",waypoint_type=\"MOON\"}} {Start.ToUnixTimeSeconds()}\n");
        text.Should().Contain("spacetraders_shipyard_ship_type{reset_date=\"2026-09-27\",system=\"X1-AB\",waypoint=\"X1-AB-H52\",ship_type=\"SHIP_MINING_DRONE\"} 1\n");
        text.Should().Contain("spacetraders_shipyard_ship_type{reset_date=\"2026-09-27\",system=\"X1-AB\",waypoint=\"X1-AB-H52\",ship_type=\"SHIP_PROBE\"} 1\n");
        text.Should().Contain("spacetraders_shipyard_ship_price{reset_date=\"2026-09-27\",system=\"X1-AB\",waypoint=\"X1-AB-H52\",ship_type=\"SHIP_MINING_DRONE\"} 46885\n");
        text.Should().Contain("spacetraders_shipyard_ship_supply{reset_date=\"2026-09-27\",system=\"X1-AB\",waypoint=\"X1-AB-H52\",ship_type=\"SHIP_MINING_DRONE\"} 3\n");
        text.Should().NotContain("spacetraders_shipyard_ship_price{reset_date=\"2026-09-27\",system=\"X1-AB\",waypoint=\"X1-AB-H52\",ship_type=\"SHIP_PROBE\"}");

        _metrics.Shipyards([]);
        (await ExportAsync()).Should().NotContain("waypoint=\"X1-AB-H52\"");
    }

    /// <summary>
    /// Slice 2.11: the shipyards table shows each ship for sale's tank and hold, what it could do in the fleet, and its
    /// equipment, in one info series per ship type that follows the listing. A ship type listed again without details
    /// (no ship of ours there) keeps only its type; one that is no longer listed loses its series.
    /// </summary>
    [Fact]
    public async Task AShipForSale_ShowsItsTankHoldRolesAndEquipment_WhileTheShipyardListsThem()
    {
        var drone = new ShipyardShipMetricsSample("SHIP_MINING_DRONE", 46_885, "MODERATE")
        {
            FuelCapacity = 80,
            CargoCapacity = 15,
            Can = "Mine, Trade",
            Equipment = "MINING_LASER_I, MINERAL_PROCESSOR_I",
        };
        _metrics.Shipyards([Shipyard(["SHIP_MINING_DRONE"], drone)]);

        var text = await ExportAsync();
        text.Should().Contain("spacetraders_shipyard_ship_fuel_capacity_units{reset_date=\"2026-09-27\",system=\"X1-AB\",waypoint=\"X1-AB-H52\",ship_type=\"SHIP_MINING_DRONE\"} 80\n");
        text.Should().Contain("spacetraders_shipyard_ship_cargo_capacity_units{reset_date=\"2026-09-27\",system=\"X1-AB\",waypoint=\"X1-AB-H52\",ship_type=\"SHIP_MINING_DRONE\"} 15\n");
        text.Should().Contain("spacetraders_shipyard_ship_info{reset_date=\"2026-09-27\",system=\"X1-AB\",waypoint=\"X1-AB-H52\",ship_type=\"SHIP_MINING_DRONE\",can=\"Mine, Trade\",equipment=\"MINING_LASER_I, MINERAL_PROCESSOR_I\"} 1\n");

        // A listing that changes keeps one series.
        _metrics.Shipyards([Shipyard(["SHIP_MINING_DRONE"], drone with { Can = "Survey, Mine, Trade", Equipment = "MINING_LASER_I, SURVEYOR_I, MINERAL_PROCESSOR_I" })]);

        text = await ExportAsync();
        text.Should().Contain("spacetraders_shipyard_ship_info{reset_date=\"2026-09-27\",system=\"X1-AB\",waypoint=\"X1-AB-H52\",ship_type=\"SHIP_MINING_DRONE\",can=\"Survey, Mine, Trade\",equipment=\"MINING_LASER_I, SURVEYOR_I, MINERAL_PROCESSOR_I\"} 1\n");
        text.Should().NotContain("can=\"Mine, Trade\"");

        // Listed again without details: only its type is left.
        _metrics.Shipyards([Shipyard(["SHIP_MINING_DRONE"])]);

        text = await ExportAsync();
        text.Should().Contain("spacetraders_shipyard_ship_type{reset_date=\"2026-09-27\",system=\"X1-AB\",waypoint=\"X1-AB-H52\",ship_type=\"SHIP_MINING_DRONE\"} 1\n");
        text.Should().NotContain("spacetraders_shipyard_ship_fuel_capacity_units{");
        text.Should().NotContain("spacetraders_shipyard_ship_cargo_capacity_units{");
        text.Should().NotContain("spacetraders_shipyard_ship_info{");

        // Listed in full again, then no longer listed.
        _metrics.Shipyards([Shipyard(["SHIP_MINING_DRONE"], drone)]);
        _metrics.Shipyards([]);

        (await ExportAsync()).Should().NotContain("waypoint=\"X1-AB-H52\"");
    }

    [Fact]
    public async Task TheSupplyChain_ShowsWhatEachGoodIsMadeFromAndWhatIsMadeFromIt()
    {
        _metrics.SupplyChain(new Dictionary<string, IReadOnlyList<string>>
        {
            ["IRON"] = ["IRON_ORE"],
            ["MACHINERY"] = ["IRON"],
            ["FAB_MATS"] = ["QUARTZ_SAND", "IRON"],
        });

        var text = await ExportAsync();
        text.Should().Contain("spacetraders_good_supply_chain{reset_date=\"2026-09-27\",good=\"IRON\",made_from=\"IRON_ORE\",used_for=\"FAB_MATS, MACHINERY\"} 1\n");
        text.Should().Contain("spacetraders_good_supply_chain{reset_date=\"2026-09-27\",good=\"FAB_MATS\",made_from=\"IRON, QUARTZ_SAND\",used_for=\"\"} 1\n");
        text.Should().Contain("spacetraders_good_supply_chain{reset_date=\"2026-09-27\",good=\"IRON_ORE\",made_from=\"\",used_for=\"IRON\"} 1\n");
        text.Should().Contain("spacetraders_good_supply_chain{reset_date=\"2026-09-27\",good=\"QUARTZ_SAND\",made_from=\"\",used_for=\"FAB_MATS\"} 1\n");
    }

    [Fact]
    public async Task TheUsableSurveys_AreCountedPerWaypoint_UsedOrNot_AndAWaypointWithoutAnyLosesItsSeries()
    {
        // Slice 6.4, the survey dashboard: surveys piling up unused mean surveying runs ahead of the miners.
        _metrics.Surveys([new SurveyMetricsSample("X1-AB-XB5C", false, 2), new SurveyMetricsSample("X1-AB-XB5C", true, 1)]);

        var text = await ExportAsync();
        text.Should().Contain("spacetraders_surveys_active{reset_date=\"2026-09-27\",waypoint=\"X1-AB-XB5C\",used=\"false\"} 2\n");
        text.Should().Contain("spacetraders_surveys_active{reset_date=\"2026-09-27\",waypoint=\"X1-AB-XB5C\",used=\"true\"} 1\n");

        _metrics.Surveys([]);

        (await ExportAsync()).Should().NotContain("spacetraders_surveys_active{");
    }

    /// <summary>
    /// The settings table (slice 2.9): one row per setting, so a setting whose value changes loses its old series, and
    /// one that is gone (the old agent's, after a reset) loses its row.
    /// </summary>
    [Fact]
    public async Task ASetting_ShowsItsValueAndWhatItDoes_InOneSeriesWhateverItsValue()
    {
        _metrics.Settings(
        [
            new SettingMetricsSample("Automation.Plan.Mining.Enabled", "false", "true", "Run the mining plan"),
            new SettingMetricsSample("Trade.MinProfitPerUnit", "200", "200", "Credits per unit, after fuel, a trade trip must earn"),
        ]);

        var text = await ExportAsync();
        text.Should().Contain("spacetraders_setting_info{reset_date=\"2026-09-27\",setting=\"Automation.Plan.Mining.Enabled\",current=\"false\",next_run=\"true\",description=\"Run the mining plan\"} 1\n");
        text.Should().Contain("spacetraders_setting_info{reset_date=\"2026-09-27\",setting=\"Trade.MinProfitPerUnit\",current=\"200\",next_run=\"200\",description=\"Credits per unit, after fuel, a trade trip must earn\"} 1\n");

        // D69: a value chosen for the next run alone changes the row too.
        _metrics.Settings([new SettingMetricsSample("Automation.Plan.Mining.Enabled", "false", "false", "Run the mining plan")]);

        text = await ExportAsync();
        text.Should().Contain("spacetraders_setting_info{reset_date=\"2026-09-27\",setting=\"Automation.Plan.Mining.Enabled\",current=\"false\",next_run=\"false\",description=\"Run the mining plan\"} 1\n");
        text.Should().NotContain("next_run=\"true\"");
        text.Should().NotContain("setting=\"Trade.MinProfitPerUnit\"");
    }

    /// <summary>
    /// The roles table (slice 6.9): one row per ship with its role and why, and what each role it could take would earn
    /// it per hour; a ship whose role changes loses its old row, and a role it can no longer take its estimate.
    /// </summary>
    [Fact]
    public async Task AShipsRole_IsOneSeries_WithAnEstimatePerRoleItCouldTake()
    {
        _metrics.Roles(
        [
            new RoleMetricsSample("AGENT-1", "Survey", "survey_first", new Dictionary<string, long> { ["Mine"] = 4_000, ["Trade"] = 21_000 }),
            new RoleMetricsSample("AGENT-3", "Mine", "most_profitable", new Dictionary<string, long> { ["Mine"] = 2_500, ["Trade"] = 0 }),
        ]);

        var text = await ExportAsync();
        text.Should().Contain("spacetraders_ship_role_info{reset_date=\"2026-09-27\",ship=\"AGENT-1\",role=\"Survey\",reason=\"survey_first\"} 1\n");
        text.Should().Contain("spacetraders_ship_role_info{reset_date=\"2026-09-27\",ship=\"AGENT-3\",role=\"Mine\",reason=\"most_profitable\"} 1\n");
        text.Should().Contain("spacetraders_ship_role_credits_per_hour{reset_date=\"2026-09-27\",ship=\"AGENT-1\",role=\"Trade\"} 21000\n");
        text.Should().Contain("spacetraders_ship_role_credits_per_hour{reset_date=\"2026-09-27\",ship=\"AGENT-3\",role=\"Mine\"} 2500\n");

        _metrics.Roles([new RoleMetricsSample("AGENT-1", "Trade", "most_profitable", new Dictionary<string, long> { ["Trade"] = 21_000 })]);

        text = await ExportAsync();
        text.Should().Contain("spacetraders_ship_role_info{reset_date=\"2026-09-27\",ship=\"AGENT-1\",role=\"Trade\",reason=\"most_profitable\"} 1\n");
        text.Should().NotContain("role=\"Survey\"");
        text.Should().NotContain("ship=\"AGENT-3\"");
        text.Should().NotContain("spacetraders_ship_role_credits_per_hour{reset_date=\"2026-09-27\",ship=\"AGENT-1\",role=\"Mine\"}");
    }

    /// <summary>
    /// Slice 6.6: what the jump gate requires and what it has, per material, for the dashboard's jump gate progress; a
    /// material that is gone loses its series.
    /// </summary>
    [Fact]
    public async Task TheJumpGatesMaterials_AreOneSeriesEach_UntilTheyAreGone()
    {
        _metrics.Construction(
        [
            new ConstructionMetricsSample("X1-DC53-I55", "FAB_MATS", 1_600, 400),
            new ConstructionMetricsSample("X1-DC53-I55", "ADVANCED_CIRCUITRY", 400, 0),
        ]);

        var text = await ExportAsync();
        text.Should().Contain("spacetraders_construction_units_required{reset_date=\"2026-09-27\",site=\"X1-DC53-I55\",trade_symbol=\"FAB_MATS\"} 1600\n");
        text.Should().Contain("spacetraders_construction_units_fulfilled{reset_date=\"2026-09-27\",site=\"X1-DC53-I55\",trade_symbol=\"FAB_MATS\"} 400\n");
        text.Should().Contain("spacetraders_construction_units_fulfilled{reset_date=\"2026-09-27\",site=\"X1-DC53-I55\",trade_symbol=\"ADVANCED_CIRCUITRY\"} 0\n");

        _metrics.Construction([new ConstructionMetricsSample("X1-DC53-I55", "FAB_MATS", 1_600, 1_600)]);

        text = await ExportAsync();
        text.Should().Contain("spacetraders_construction_units_fulfilled{reset_date=\"2026-09-27\",site=\"X1-DC53-I55\",trade_symbol=\"FAB_MATS\"} 1600\n");
        text.Should().NotContain("trade_symbol=\"ADVANCED_CIRCUITRY\"");
    }

    /// <summary>
    /// What each plan would buy (slice 6.10b, D43): one series per plan, worth what the ship costs; a plan whose need
    /// changes loses its old series, and one that needs nothing loses its own.
    /// </summary>
    [Fact]
    public async Task APlansPurchaseNeed_IsOneSeries_WorthWhatTheShipCosts()
    {
        _metrics.PurchaseNeeds(
        [
            new PurchaseNeedMetricsSample("Survey", "Surveyor", 2, "SHIP_SURVEYOR", "X1-DC53-H52", 33_905),
            new PurchaseNeedMetricsSample("Trading", "CargoShips", 5, "SHIP_LIGHT_SHUTTLE", "X1-DC53-A2", 114_225),
        ]);

        var text = await ExportAsync();
        text.Should().Contain("spacetraders_purchase_need_credits{reset_date=\"2026-09-27\",plan=\"Survey\",tier=\"Surveyor\",position=\"2\",ship_type=\"SHIP_SURVEYOR\",shipyard=\"X1-DC53-H52\"} 33905\n");
        text.Should().Contain("spacetraders_purchase_need_credits{reset_date=\"2026-09-27\",plan=\"Trading\",tier=\"CargoShips\",position=\"5\",ship_type=\"SHIP_LIGHT_SHUTTLE\",shipyard=\"X1-DC53-A2\"} 114225\n");

        _metrics.PurchaseNeeds([new PurchaseNeedMetricsSample("Trading", "Alternating", 7, "SHIP_LIGHT_HAULER", "X1-DC53-A2", 354_210)]);

        text = await ExportAsync();
        text.Should().Contain("spacetraders_purchase_need_credits{reset_date=\"2026-09-27\",plan=\"Trading\",tier=\"Alternating\",position=\"7\",ship_type=\"SHIP_LIGHT_HAULER\",shipyard=\"X1-DC53-A2\"} 354210\n");
        text.Should().NotContain("plan=\"Survey\"");
        text.Should().NotContain("SHIP_LIGHT_SHUTTLE");
    }

    [Fact]
    public void TheRoleBoardsState_GivesTheRoleSamples_AndNoStateGivesNone()
    {
        var state = new RolePlanState
        {
            EvaluatedAt = Start,
            Ships =
            [
                new RoleShipState
                {
                    ShipSymbol = "AGENT-1",
                    Role = FleetRole.Trade,
                    Reason = "most_profitable",
                    Since = Start,
                    Estimates = [new RoleEstimateState { Role = FleetRole.Mine, CreditsPerHour = 4_000 }, new RoleEstimateState { Role = FleetRole.Trade, CreditsPerHour = 21_000 }],
                },
            ],
        };

        var sample = PrometheusMetricsService.RoleSamples(JsonSerializer.Serialize(state)).Should().ContainSingle().Subject;
        (sample.Ship, sample.Role, sample.Reason).Should().Be(("AGENT-1", "Trade", "most_profitable"));
        sample.CreditsPerHour.Should().BeEquivalentTo(new Dictionary<string, long> { ["Mine"] = 4_000, ["Trade"] = 21_000 });
        PrometheusMetricsService.RoleSamples(null).Should().BeEmpty();
        PrometheusMetricsService.RoleSamples("{not json").Should().BeEmpty();
    }

    [Fact]
    public async Task AContractThatIsGone_LosesItsSeries()
    {
        _metrics.Contracts([new ContractMetricsSample("C-1", "IRON_ORE", 42, 7, Start.AddDays(7))]);
        var text = await ExportAsync();
        text.Should().Contain("spacetraders_contract_units_required{reset_date=\"2026-09-27\",contract=\"C-1\",trade_symbol=\"IRON_ORE\"} 42");
        text.Should().Contain("spacetraders_contract_units_fulfilled{reset_date=\"2026-09-27\",contract=\"C-1\",trade_symbol=\"IRON_ORE\"} 7");
        text.Should().Contain($"spacetraders_contract_deadline_timestamp_seconds{{reset_date=\"2026-09-27\",contract=\"C-1\"}} {Start.AddDays(7).ToUnixTimeSeconds()}");

        _metrics.Contracts([]);

        (await ExportAsync()).Should().NotContain("contract=\"C-1\"");
    }

    private static MarketMetricsSample Market(params TradeGoodSnapshot[] goods)
        => new("X1-AB", "X1-AB-H51", "PLANET", Start, goods);

    private static ShipyardMetricsSample Shipyard(string[] shipTypes, params ShipyardShipMetricsSample[] ships)
        => new("X1-AB", "X1-AB-H52", "MOON", Start, shipTypes, ships);

    private static ShipMetricsSample Drone(string location, string activity, CargoItemModel[] cargo, DateTimeOffset arrivesAt = default)
        => new("AGENT-3", "SHIP_MINING_DRONE", arrivesAt == default ? "IN_ORBIT" : "IN_TRANSIT", "Contract", string.Empty)
        {
            Location = location,
            Activity = activity,
            ArrivesAt = arrivesAt,
            CargoCapacity = 15,
            Cargo = cargo,
            Value = 50_000,
        };

    [Fact]
    public async Task EverySeries_CarriesTheAgentsResetDate()
    {
        // Slice 2.13 (D70): the bot registers the same symbol after every reset, so AGENT-3 is a ship of every run. The reset
        // date keeps one run's series apart from the next on the dashboards.
        _metrics.Credits(145_028);
        _metrics.ReservedCredits(100_000);
        _metrics.ApiRequest("GET", "my/agent", "200");
        _metrics.Anomaly("ShipStuck", "AGENT-3", active: true);
        _metrics.GoodsSold("X1-AB-H51", "COPPER_ORE", 15);
        _metrics.TripProfit("mining", 340);
        _metrics.Fleet([Drone("X1-AB-XB5C (ENGINEERED_ASTEROID)", "mining COPPER_ORE", [new CargoItemModel("COPPER_ORE", 9)])], Start);

        var samples = (await ExportAsync()).Split('\n').Where(line => line.StartsWith("spacetraders_", StringComparison.Ordinal)).ToList();

        samples.Should().HaveCountGreaterThan(10)
            .And.OnlyContain(line => line.Contains("{reset_date=\"2026-09-27\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task BeforeTheAgentIsKnown_ASeriesHasAnEmptyResetDate()
    {
        // Bootstrap's own API calls come before it has picked the agent. Prometheus stores an empty value as no label.
        var registry = Metrics.NewCustomRegistry();
        var metrics = new PrometheusAutomationMetrics(registry, new AgentDataScope());

        metrics.Credits(1);

        (await ExportAsync(registry)).Should().Contain("spacetraders_agent_credits{reset_date=\"\"} 1\n");
    }

    private static AgentDataScope Agent(string agentId)
    {
        var agent = new AgentDataScope();
        agent.Set(agentId);
        return agent;
    }

    private static string StatusLine(string ship, string role, string state, string goal, string reason, DateTimeOffset since)
        => $"spacetraders_ship_status_since_timestamp_seconds{{reset_date=\"2026-09-27\",ship=\"{ship}\",role=\"{role}\",state=\"{state}\",goal=\"{goal}\",reason=\"{reason}\"}} {since.ToUnixTimeSeconds()}";

    private Task<string> ExportAsync() => ExportAsync(_registry);

    private static async Task<string> ExportAsync(CollectorRegistry registry)
    {
        using var stream = new MemoryStream();
        await registry.CollectAndExportAsTextAsync(stream);
        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
