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
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Roles;
using SpaceTraders.Application.Services;
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
            NullLogger<PrometheusMetricsService>.Instance);
        await service.SampleAsync(CancellationToken.None);

        ships.Select(s => (s.Ship, s.Capabilities)).Should().BeEquivalentTo(new[]
        {
            ("AGENT-1", "Survey, Mine, Siphon, Trade"),
            ("AGENT-2", "none"),
            ("AGENT-3", "Mine, Trade"),
            ("AGENT-4", "Siphon, Trade"),
            ("AGENT-5", "Survey"),
            ("AGENT-6", "Trade"),
            ("AGENT-7", "Siphon, Trade"),
        });
    }

    /// <summary>
    /// The dashboard's settings table (slice 2.9): every setting the agent has, with its value and what it does. A value
    /// that may hold a secret is hidden, as in <c>SettingChanged</c>. A setting keeps the description it was seeded
    /// with, so the cluster's agent, registered before slice 6.3, still describes the probe plan of before; the table
    /// says what the running version does.
    /// </summary>
    [Fact]
    public async Task SampleAsync_ExportsEverySetting_WithSecretsHidden_AndWhatItDoesNow()
    {
        using var provider = BuildProvider();
        await using (var seedScope = provider.CreateAsyncScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<SpaceTradersDbContext>();
            db.Settings.Add(Setting(AgentId, "Automation.Plan.ProbeDeployment.Enabled", "true", "Run the probe plan: park a probe at every market and shipyard in the HQ system (buys probes)"));
            db.Settings.Add(Setting(AgentId, "Alerts.WebhookUrl", "https://hooks.example.com/services/T000/B000/s3cr3t", "Slack/webhook URL for operator alerts (empty = disabled)"));
            db.Settings.Add(Setting(AgentId, "Custom.ReportUrl", string.Empty, string.Empty));
            db.Settings.Add(Setting("AGENT@2026-09-20", "Automation.Enabled", "false", "Master kill-switch for automation"));
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
            NullLogger<PrometheusMetricsService>.Instance);
        await service.SampleAsync(CancellationToken.None);

        var probePlan = DefaultSettingsSeed.DescriptionOf("Automation.Plan.ProbeDeployment.Enabled");
        probePlan.Should().StartWith("Run the probe plan: a probe for every market");
        settings.Should().BeEquivalentTo(new[]
        {
            new SettingMetricsSample("Automation.Plan.ProbeDeployment.Enabled", "true", probePlan),
            new SettingMetricsSample("Alerts.WebhookUrl", "(hidden)", "Slack/webhook URL for operator alerts (empty = disabled)"),
            new SettingMetricsSample("Custom.ReportUrl", string.Empty, string.Empty),
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
            NullLogger<PrometheusMetricsService>.Instance);
        await service.SampleAsync(CancellationToken.None);

        exported.Should().Equal(new PurchaseNeedMetricsSample("ProbeDeployment", "Probes", 5, "SHIP_PROBE", "X1-AB-A2", 77_117));
    }

    /// <summary>
    /// D51: the dashboard shows the credit reserve next to the credits: the floor, and 1,000 a unit of what the ships that
    /// trade can carry. The command ship and a light shuttle carry 40 each; a mining drone counts only while the role board
    /// has it trading.
    /// </summary>
    [Theory]
    [InlineData("false", 140_000)]
    [InlineData("true", 155_000)]
    public async Task SampleAsync_ExportsTheCreditReserve_ByWhatTheShipsThatTradeCanCarry(string boardSwitch, long reserve)
    {
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
            db.Ships.Add(Equipped("AGENT-4", "SHIP_LIGHT_SHUTTLE", ["MOUNT_TURRET_I"], 40, 300));
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
        _metrics = new PrometheusAutomationMetrics(_registry);
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
        { "spacetraders_agent_credits", metrics => metrics.Credits(145_028), "spacetraders_agent_credits 145028\n" },
        { "spacetraders_credit_reserve", metrics => metrics.ReservedCredits(100_000), "spacetraders_credit_reserve 100000\n" },
        { "spacetraders_db_size_bytes", metrics => metrics.DatabaseSize(15_742_655), "spacetraders_db_size_bytes 15742655\n" },
        { "spacetraders_server_next_reset_timestamp_seconds", metrics => metrics.NextServerReset(DateTimeOffset.FromUnixTimeSeconds(1_791_118_800)), "spacetraders_server_next_reset_timestamp_seconds 1791118800\n" },
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
        text.Should().NotContain($"\n{name} ", "{0} has no value yet, and 0 isn't it", name);

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
        { "credits earned", metrics => metrics.CreditsEarned("ContractDeposit", 4_267), "spacetraders_credits_earned_total{source=\"ContractDeposit\"} " },
        { "credits spent", metrics => metrics.CreditsSpent("ShipPurchase", 46_885), "spacetraders_credits_spent_total{category=\"ShipPurchase\"} " },
        { "429s", metrics => metrics.ApiThrottled("rate_limiter"), "spacetraders_api_throttled_total{source=\"rate_limiter\"} " },
        { "API responses", metrics => metrics.ApiRequest("POST", "my/ships/{shipSymbol}/extract", "429"), "spacetraders_api_requests_total{method=\"POST\",endpoint=\"my/ships/{shipSymbol}/extract\",status=\"429\"} " },
        { "breaker trips", metrics => metrics.GoalBreakerTripped("AGENT-3"), "spacetraders_goal_breaker_trips_total{ship=\"AGENT-3\"} " },
        { "goal steps", metrics => metrics.GoalStep("ScoutWaypoint"), "spacetraders_goal_steps_total{kind=\"ScoutWaypoint\"} " },
        { "messages", metrics => metrics.MessageHandled("ContractAcceptedEvent"), "spacetraders_messages_handled_total{type=\"ContractAcceptedEvent\"} " },
        { "rate-limit waits", metrics => metrics.RateLimitWait(TimeSpan.FromSeconds(3), "read"), "spacetraders_api_rate_limit_wait_seconds_total{kind=\"read\"} " },
        { "units extracted", metrics => metrics.Extracted("AGENT-3", "COPPER_ORE", 2), "spacetraders_extracted_units_total{ship=\"AGENT-3\",good=\"COPPER_ORE\"} " },
        { "units jettisoned", metrics => metrics.Jettisoned("AGENT-3", "SILICON_CRYSTALS", 2), "spacetraders_jettisoned_units_total{ship=\"AGENT-3\",good=\"SILICON_CRYSTALS\"} " },
        { "extractions", metrics => metrics.Extraction("AGENT-3", surveyed: true), "spacetraders_extractions_total{ship=\"AGENT-3\",surveyed=\"true\"} " },
        { "surveys taken", metrics => metrics.SurveyTaken("X1-AB-XB5C", "MODERATE"), "spacetraders_surveys_taken_total{waypoint=\"X1-AB-XB5C\",size=\"MODERATE\"} " },
        { "surveys ended", metrics => metrics.SurveyEnded("X1-AB-XB5C", "expired", used: false), "spacetraders_surveys_ended_total{waypoint=\"X1-AB-XB5C\",reason=\"expired\",used=\"false\"} " },
        { "units sold", metrics => metrics.GoodsSold("X1-DC53-H51", "HYDROCARBON", 18), "spacetraders_goods_sold_units_total{system=\"X1-DC53\",waypoint=\"X1-DC53-H51\",good=\"HYDROCARBON\"} " },
        { "units bought", metrics => metrics.GoodsBought("X1-DC53-K85", "PLASTICS", 20), "spacetraders_goods_bought_units_total{system=\"X1-DC53\",waypoint=\"X1-DC53-K85\",good=\"PLASTICS\"} " },
        { "trips", metrics => metrics.TripEnded("trade"), "spacetraders_trips_total{activity=\"trade\"} " },
        { "trip profit", metrics => metrics.TripProfit("contract", 4_267), "spacetraders_trip_profit_credits_total{activity=\"contract\"} " },
        { "trip loss", metrics => metrics.TripProfit("trade", -65_232), "spacetraders_trip_loss_credits_total{activity=\"trade\"} " },
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

        text.Should().Contain("spacetraders_trip_profit_credits_total{activity=\"trade\"} 4320\n");
        text.Should().Contain("spacetraders_trip_loss_credits_total{activity=\"trade\"} 2152\n");
        text.Should().Contain("spacetraders_trip_profit_credits_total{activity=\"mining\"} 1005\n");
        text.Should().Contain("spacetraders_trip_loss_credits_total{activity=\"mining\"} 0\n");
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

        (await ExportAsync()).Should().Contain("spacetraders_credits_spent_total{category=\"ShipPurchase\"} 0\n");
        (await ExportAsync()).Should().Contain("spacetraders_credits_spent_total{category=\"ShipPurchase\"} 46885\n");
    }

    [Fact]
    public async Task ASeriesPrometheusHasSeenAtZero_CountsAtOnce()
    {
        _metrics.CreditsEarned("TradeSell", 450);
        await ExportAsync();
        (await ExportAsync()).Should().Contain("spacetraders_credits_earned_total{source=\"TradeSell\"} 450\n");

        _metrics.CreditsEarned("TradeSell", 50);

        (await ExportAsync()).Should().Contain("spacetraders_credits_earned_total{source=\"TradeSell\"} 500\n");
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
        text.Should().Contain("spacetraders_ships{role=\"COMMAND\",state=\"DOCKED\"} 0");
        text.Should().Contain("spacetraders_ships{role=\"COMMAND\",state=\"IN_TRANSIT\"} 1");
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
        text.Should().Contain("spacetraders_ship_info{ship=\"AGENT-3\",location=\"X1-AB-XB5C (ENGINEERED_ASTEROID)\",activity=\"mining COPPER_ORE\"} 1\n");
        text.Should().Contain("spacetraders_ship_cargo_units{ship=\"AGENT-3\",good=\"COPPER_ORE\"} 9\n");
        text.Should().Contain("spacetraders_ship_cargo_units{ship=\"AGENT-3\",good=\"SILICON_CRYSTALS\"} 2\n");
        text.Should().Contain("spacetraders_ship_cargo_capacity_units{ship=\"AGENT-3\"} 15\n");
        text.Should().Contain("spacetraders_ship_value_credits{ship=\"AGENT-3\"} 50000\n");
        text.Should().NotContain("spacetraders_ship_arrival_timestamp_seconds{");

        // It jettisoned the crystals, filled up, and is on its way to deliver.
        _metrics.Fleet([Drone("→ X1-AB-H51 (PLANET)", "on the way to deliver COPPER_ORE", [new("COPPER_ORE", 15)], Start.AddMinutes(4))], Start.AddMinutes(1));

        text = await ExportAsync();
        text.Should().Contain("spacetraders_ship_info{ship=\"AGENT-3\",location=\"→ X1-AB-H51 (PLANET)\",activity=\"on the way to deliver COPPER_ORE\"} 1\n");
        text.Should().NotContain("activity=\"mining COPPER_ORE\"");
        text.Should().Contain("spacetraders_ship_cargo_units{ship=\"AGENT-3\",good=\"COPPER_ORE\"} 15\n");
        text.Should().NotContain("good=\"SILICON_CRYSTALS\"");
        text.Should().Contain($"spacetraders_ship_arrival_timestamp_seconds{{ship=\"AGENT-3\"}} {Start.AddMinutes(4).ToUnixTimeSeconds()}\n");

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
        text.Should().Contain("spacetraders_ship_capabilities_info{ship=\"AGENT-2\",can=\"none\"} 1\n");
        text.Should().Contain("spacetraders_ship_capabilities_info{ship=\"AGENT-3\",can=\"Mine, Trade\"} 1\n");

        // A surveyor mount installed on the drone; the probe scrapped.
        _metrics.Fleet([drone with { Capabilities = "Survey, Mine, Trade" }], Start.AddMinutes(1));

        text = await ExportAsync();
        text.Should().Contain("spacetraders_ship_capabilities_info{ship=\"AGENT-3\",can=\"Survey, Mine, Trade\"} 1\n");
        text.Should().NotContain("can=\"Mine, Trade\"");
        text.Should().NotContain("ship=\"AGENT-2\"");
    }

    /// <summary>The markets dashboard (slice 2.8): a market's goods with their prices, volume, supply and activity.</summary>
    [Fact]
    public async Task AMarket_ShowsItsGoodsWithPricesVolumeSupplyAndActivity()
    {
        _metrics.Markets([Market(
            new TradeGoodSnapshot("COPPER_ORE", "IMPORT", 60, 55, 60, "SCARCE", "WEAK"),
            new TradeGoodSnapshot("FUEL", "EXCHANGE", 72, 68, 180, "MODERATE", string.Empty))]);

        var text = await ExportAsync();
        text.Should().Contain($"spacetraders_market_observed_timestamp_seconds{{system=\"X1-AB\",waypoint=\"X1-AB-H51\",waypoint_type=\"PLANET\"}} {Start.ToUnixTimeSeconds()}\n");
        text.Should().Contain("spacetraders_market_purchase_price{system=\"X1-AB\",waypoint=\"X1-AB-H51\",good=\"COPPER_ORE\",kind=\"IMPORT\"} 60\n");
        text.Should().Contain("spacetraders_market_sell_price{system=\"X1-AB\",waypoint=\"X1-AB-H51\",good=\"COPPER_ORE\",kind=\"IMPORT\"} 55\n");
        text.Should().Contain("spacetraders_market_trade_volume{system=\"X1-AB\",waypoint=\"X1-AB-H51\",good=\"COPPER_ORE\",kind=\"IMPORT\"} 60\n");
        text.Should().Contain("spacetraders_market_supply{system=\"X1-AB\",waypoint=\"X1-AB-H51\",good=\"COPPER_ORE\",kind=\"IMPORT\"} 1\n");
        text.Should().Contain("spacetraders_market_activity{system=\"X1-AB\",waypoint=\"X1-AB-H51\",good=\"COPPER_ORE\",kind=\"IMPORT\"} 1\n");
        text.Should().Contain("spacetraders_market_supply{system=\"X1-AB\",waypoint=\"X1-AB-H51\",good=\"FUEL\",kind=\"EXCHANGE\"} 3\n");
        text.Should().NotContain("spacetraders_market_activity{system=\"X1-AB\",waypoint=\"X1-AB-H51\",good=\"FUEL\"");

        // The next visit no longer lists copper; after a reset the new agent knows no markets yet.
        _metrics.Markets([Market(new TradeGoodSnapshot("FUEL", "EXCHANGE", 74, 70, 180, "LIMITED", "GROWING"))]);
        text = await ExportAsync();
        text.Should().NotContain("good=\"COPPER_ORE\"");
        text.Should().Contain("spacetraders_market_purchase_price{system=\"X1-AB\",waypoint=\"X1-AB-H51\",good=\"FUEL\",kind=\"EXCHANGE\"} 74\n");
        text.Should().Contain("spacetraders_market_activity{system=\"X1-AB\",waypoint=\"X1-AB-H51\",good=\"FUEL\",kind=\"EXCHANGE\"} 2\n");

        _metrics.Markets([]);
        (await ExportAsync()).Should().NotContain("waypoint=\"X1-AB-H51\"");
    }

    [Fact]
    public async Task AShipyard_ShowsItsShipTypesAndThePricesItKnows()
    {
        _metrics.Shipyards([new ShipyardMetricsSample(
            "X1-AB",
            "X1-AB-H52",
            "MOON",
            Start,
            ["SHIP_MINING_DRONE", "SHIP_PROBE"],
            [new ShipyardShipDto { Type = "SHIP_MINING_DRONE", PurchasePrice = 46_885, Supply = "MODERATE" }])]);

        var text = await ExportAsync();
        text.Should().Contain($"spacetraders_shipyard_observed_timestamp_seconds{{system=\"X1-AB\",waypoint=\"X1-AB-H52\",waypoint_type=\"MOON\"}} {Start.ToUnixTimeSeconds()}\n");
        text.Should().Contain("spacetraders_shipyard_ship_type{system=\"X1-AB\",waypoint=\"X1-AB-H52\",ship_type=\"SHIP_MINING_DRONE\"} 1\n");
        text.Should().Contain("spacetraders_shipyard_ship_type{system=\"X1-AB\",waypoint=\"X1-AB-H52\",ship_type=\"SHIP_PROBE\"} 1\n");
        text.Should().Contain("spacetraders_shipyard_ship_price{system=\"X1-AB\",waypoint=\"X1-AB-H52\",ship_type=\"SHIP_MINING_DRONE\"} 46885\n");
        text.Should().Contain("spacetraders_shipyard_ship_supply{system=\"X1-AB\",waypoint=\"X1-AB-H52\",ship_type=\"SHIP_MINING_DRONE\"} 3\n");
        text.Should().NotContain("spacetraders_shipyard_ship_price{system=\"X1-AB\",waypoint=\"X1-AB-H52\",ship_type=\"SHIP_PROBE\"}");

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
        text.Should().Contain("spacetraders_good_supply_chain{good=\"IRON\",made_from=\"IRON_ORE\",used_for=\"FAB_MATS, MACHINERY\"} 1\n");
        text.Should().Contain("spacetraders_good_supply_chain{good=\"FAB_MATS\",made_from=\"IRON, QUARTZ_SAND\",used_for=\"\"} 1\n");
        text.Should().Contain("spacetraders_good_supply_chain{good=\"IRON_ORE\",made_from=\"\",used_for=\"IRON\"} 1\n");
        text.Should().Contain("spacetraders_good_supply_chain{good=\"QUARTZ_SAND\",made_from=\"\",used_for=\"FAB_MATS\"} 1\n");
    }

    [Fact]
    public async Task TheUsableSurveys_AreCountedPerWaypoint_UsedOrNot_AndAWaypointWithoutAnyLosesItsSeries()
    {
        // Slice 6.4, the survey dashboard: surveys piling up unused mean surveying runs ahead of the miners.
        _metrics.Surveys([new SurveyMetricsSample("X1-AB-XB5C", false, 2), new SurveyMetricsSample("X1-AB-XB5C", true, 1)]);

        var text = await ExportAsync();
        text.Should().Contain("spacetraders_surveys_active{waypoint=\"X1-AB-XB5C\",used=\"false\"} 2\n");
        text.Should().Contain("spacetraders_surveys_active{waypoint=\"X1-AB-XB5C\",used=\"true\"} 1\n");

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
            new SettingMetricsSample("Automation.Plan.Mining.Enabled", "false", "Run the mining plan"),
            new SettingMetricsSample("Trade.MinProfitPerUnit", "200", "Credits per unit, after fuel, a trade trip must earn"),
        ]);

        var text = await ExportAsync();
        text.Should().Contain("spacetraders_setting_info{setting=\"Automation.Plan.Mining.Enabled\",current=\"false\",description=\"Run the mining plan\"} 1\n");
        text.Should().Contain("spacetraders_setting_info{setting=\"Trade.MinProfitPerUnit\",current=\"200\",description=\"Credits per unit, after fuel, a trade trip must earn\"} 1\n");

        _metrics.Settings([new SettingMetricsSample("Automation.Plan.Mining.Enabled", "true", "Run the mining plan")]);

        text = await ExportAsync();
        text.Should().Contain("spacetraders_setting_info{setting=\"Automation.Plan.Mining.Enabled\",current=\"true\",description=\"Run the mining plan\"} 1\n");
        text.Should().NotContain("current=\"false\"");
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
        text.Should().Contain("spacetraders_ship_role_info{ship=\"AGENT-1\",role=\"Survey\",reason=\"survey_first\"} 1\n");
        text.Should().Contain("spacetraders_ship_role_info{ship=\"AGENT-3\",role=\"Mine\",reason=\"most_profitable\"} 1\n");
        text.Should().Contain("spacetraders_ship_role_credits_per_hour{ship=\"AGENT-1\",role=\"Trade\"} 21000\n");
        text.Should().Contain("spacetraders_ship_role_credits_per_hour{ship=\"AGENT-3\",role=\"Mine\"} 2500\n");

        _metrics.Roles([new RoleMetricsSample("AGENT-1", "Trade", "most_profitable", new Dictionary<string, long> { ["Trade"] = 21_000 })]);

        text = await ExportAsync();
        text.Should().Contain("spacetraders_ship_role_info{ship=\"AGENT-1\",role=\"Trade\",reason=\"most_profitable\"} 1\n");
        text.Should().NotContain("role=\"Survey\"");
        text.Should().NotContain("ship=\"AGENT-3\"");
        text.Should().NotContain("spacetraders_ship_role_credits_per_hour{ship=\"AGENT-1\",role=\"Mine\"}");
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
            new PurchaseNeedMetricsSample("Trading", "CargoShips", 4, "SHIP_LIGHT_SHUTTLE", "X1-DC53-A2", 114_225),
        ]);

        var text = await ExportAsync();
        text.Should().Contain("spacetraders_purchase_need_credits{plan=\"Survey\",tier=\"Surveyor\",position=\"2\",ship_type=\"SHIP_SURVEYOR\",shipyard=\"X1-DC53-H52\"} 33905\n");
        text.Should().Contain("spacetraders_purchase_need_credits{plan=\"Trading\",tier=\"CargoShips\",position=\"4\",ship_type=\"SHIP_LIGHT_SHUTTLE\",shipyard=\"X1-DC53-A2\"} 114225\n");

        _metrics.PurchaseNeeds([new PurchaseNeedMetricsSample("Trading", "Alternating", 6, "SHIP_LIGHT_HAULER", "X1-DC53-A2", 354_210)]);

        text = await ExportAsync();
        text.Should().Contain("spacetraders_purchase_need_credits{plan=\"Trading\",tier=\"Alternating\",position=\"6\",ship_type=\"SHIP_LIGHT_HAULER\",shipyard=\"X1-DC53-A2\"} 354210\n");
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
        text.Should().Contain("spacetraders_contract_units_required{contract=\"C-1\",trade_symbol=\"IRON_ORE\"} 42");
        text.Should().Contain("spacetraders_contract_units_fulfilled{contract=\"C-1\",trade_symbol=\"IRON_ORE\"} 7");
        text.Should().Contain($"spacetraders_contract_deadline_timestamp_seconds{{contract=\"C-1\"}} {Start.AddDays(7).ToUnixTimeSeconds()}");

        _metrics.Contracts([]);

        (await ExportAsync()).Should().NotContain("contract=\"C-1\"");
    }

    private static MarketMetricsSample Market(params TradeGoodSnapshot[] goods)
        => new("X1-AB", "X1-AB-H51", "PLANET", Start, goods);

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

    private static string StatusLine(string ship, string role, string state, string goal, string reason, DateTimeOffset since)
        => $"spacetraders_ship_status_since_timestamp_seconds{{ship=\"{ship}\",role=\"{role}\",state=\"{state}\",goal=\"{goal}\",reason=\"{reason}\"}} {since.ToUnixTimeSeconds()}";

    private async Task<string> ExportAsync()
    {
        using var stream = new MemoryStream();
        await _registry.CollectAndExportAsTextAsync(stream);
        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
