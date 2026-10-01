using NSubstitute;
using SpaceTraders.Application.Automation;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Health;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Goals;

namespace SpaceTraders.Application.Tests.Health;

/// <summary>Evaluates one rule the way the monitor does: a context per evaluation, and one memory across them.</summary>
internal sealed class RuleHarness
{
    public static readonly DateTimeOffset Start = new(2026, 10, 01, 12, 00, 00, TimeSpan.Zero);

    private readonly HealthClock _clock = new();
    private DateTimeOffset _startedAt = DateTimeOffset.MinValue;

    /// <summary>The plans that are on, with automation; every plan unless a test switches one off.</summary>
    public HashSet<AutomationPlan> PlansOn { get; } = [.. Enum.GetValues<AutomationPlan>()];

    public async Task<IReadOnlyList<HealthViolation>> EvaluateAsync(IHealthRule rule, DateTimeOffset now)
    {
        if (_startedAt == DateTimeOffset.MinValue)
        {
            _startedAt = now;
        }

        var context = new HealthCheckContext(now, _startedAt, PlansOn, apiPaused: false, _clock);
        var violations = await rule.EvaluateAsync(context, CancellationToken.None);
        _clock.EndEvaluation();
        return violations;
    }
}

/// <summary>A fleet of substitutes for the rules that load ships, goals and assignments.</summary>
internal sealed class FleetFixture
{
    public FleetFixture()
    {
        Ships.GetAllAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<ShipModel>());
        Assignments.GetAllActiveAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<ShipAssignmentDto>());
    }

    public IShipRepository Ships { get; } = Substitute.For<IShipRepository>();

    public IShipGoalRepository Goals { get; } = Substitute.For<IShipGoalRepository>();

    public IShipAssignmentRepository Assignments { get; } = Substitute.For<IShipAssignmentRepository>();

    public HealthFleet Fleet => new(Ships, Goals, Assignments);

    /// <summary>A mining drone in orbit at an asteroid, last updated at <paramref name="lastSyncedAt"/>.</summary>
    public static ShipModel Drone(string symbol, DateTimeOffset lastSyncedAt, DateTimeOffset? arrivesAt = null)
        => new(
            symbol,
            "X1-AB",
            "X1-AB-A1",
            "IN_ORBIT",
            "CRUISE",
            FuelCurrent: 100,
            FuelCapacity: 100,
            ArrivesAt: arrivesAt,
            CargoCapacity: 15,
            LastSyncedAt: lastSyncedAt,
            ShipType: "SHIP_MINING_DRONE");

    /// <summary>The starting probe as startup sync stores it: its registration role as its type (B25).</summary>
    public static ShipModel StartingProbe(string symbol, string waypoint, DateTimeOffset lastSyncedAt)
        => new(symbol, "X1-AB", waypoint, "DOCKED", "CRUISE", FuelCurrent: 0, FuelCapacity: 0, LastSyncedAt: lastSyncedAt, ShipType: "SATELLITE");

    public static ShipAssignmentDto ContractAssignment(string ship, string contractId)
        => new(ship, "Contract", "X1-AB-A1", "X1-AB-H58", "IRON_ORE", contractId, 0, RuleHarness.Start.AddHours(-1), null, RequiredUnits: 15);

    public void Have(params ShipModel[] ships) => Ships.GetAllAsync(Arg.Any<CancellationToken>()).Returns(ships);

    public void Give(string ship, ShipGoal goal) => Goals.GetActiveGoalAsync(ship, Arg.Any<CancellationToken>()).Returns(goal);

    public void Assign(params ShipAssignmentDto[] assignments) => Assignments.GetAllActiveAsync(Arg.Any<CancellationToken>()).Returns(assignments);
}

/// <summary>The contract plan and its contract, as substitutes.</summary>
internal sealed class ContractFixture
{
    public const string ContractId = "C-1";

    public IContractMineralPlanRepository Plans { get; } = Substitute.For<IContractMineralPlanRepository>();

    public IContractRepository Contracts { get; } = Substitute.For<IContractRepository>();

    public static ContractMineralPlanState Plan(ContractMineralPlanStatus status, DateTimeOffset updatedAt, string tradeSymbol = "IRON_ORE", string stopReason = "")
        => new()
        {
            PlanId = Guid.NewGuid(),
            ContractId = ContractId,
            ShipSymbol = status == ContractMineralPlanStatus.Active ? "SHIP-3" : string.Empty,
            TradeSymbol = tradeSymbol,
            SourceWaypoint = "X1-AB-A1",
            DestinationWaypoint = "X1-AB-H58",
            UnitsRequired = 42,
            UnitsFulfilled = 11,
            Status = status,
            CreatedAt = updatedAt,
            UpdatedAt = updatedAt,
            StopReason = stopReason.Length == 0 ? null : stopReason,
        };

    public static ContractDto Contract(int fulfilled, DateTimeOffset deadline, bool isFulfilled = false, string tradeSymbol = "IRON_ORE")
        => new(
            ContractId,
            "COSMIC",
            "PROCUREMENT",
            IsAccepted: true,
            IsFulfilled: isFulfilled,
            Expiration: null,
            DeadlineToAccept: null,
            TermsDeadline: deadline,
            DeliverablesJson: System.Text.Json.JsonSerializer.Serialize(new[] { new ContractDeliverableDto(tradeSymbol, "X1-AB-H58", 42, fulfilled) }));

    public void Is(ContractMineralPlanState plan, ContractDto contract)
    {
        Plans.GetAsync(Arg.Any<CancellationToken>()).Returns(plan);
        Contracts.FindAsync(contract.Id, Arg.Any<CancellationToken>()).Returns(contract);
    }
}
