using System.Text.Json;
using FluentAssertions;
using NSubstitute;
using SpaceTraders.Application.Automation;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Services;
using SpaceTraders.Domain.Goals;
using Wolverine;
using static SpaceTraders.Application.Tests.Mining.MiningFixture;

namespace SpaceTraders.Application.Tests.Automation;

/// <summary>
/// Slice 6.4, D23: every free miner joins the active contract, contract before market mining; ore mined
/// beyond what it needs is sold once it is fulfilled. A ship that can survey surveys instead (D20).
/// </summary>
public sealed class ContractMinersTests
{
    private const string ContractId = "C-1";

    private readonly IContractMineralPlanRepository _plans = Substitute.For<IContractMineralPlanRepository>();
    private readonly IContractRepository _contracts = Substitute.For<IContractRepository>();
    private readonly IShipRepository _ships = Substitute.For<IShipRepository>();
    private readonly IShipAssignmentRepository _assignments = Substitute.For<IShipAssignmentRepository>();
    private readonly IShipGoalRepository _goals = Substitute.For<IShipGoalRepository>();
    private readonly ISettingsRepository _settings = Substitute.For<ISettingsRepository>();
    private readonly LogRecorder _log = new();
    private readonly List<ShipAssignmentDto> _open = [];

    public ContractMinersTests()
    {
        _plans.GetAsync(Arg.Any<CancellationToken>()).Returns(Plan());
        ContractIs(fulfilled: false, unitsFulfilled: 45);
        _assignments.GetAllActiveAsync(Arg.Any<CancellationToken>()).Returns(_ => _open.ToList());
        _assignments.FindAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => _open.FirstOrDefault(a => a.ShipSymbol == call.Arg<string>()));
        _assignments.When(assignments => assignments.UpsertAsync(Arg.Any<ShipAssignmentDto>(), Arg.Any<CancellationToken>()))
            .Do(call =>
            {
                var assignment = call.Arg<ShipAssignmentDto>();
                _open.RemoveAll(a => a.ShipSymbol == assignment.ShipSymbol);
                if (!assignment.CompletedAt.HasValue)
                {
                    _open.Add(assignment);
                }
            });
        _open.Add(Assignment("SHIP-3"));
    }

    [Fact]
    public async Task EveryFreeMiner_JoinsTheActiveContract()
    {
        Fleet(Drone("SHIP-3", XB5C, "IN_ORBIT"), Drone("SHIP-4"), Drone("SHIP-5"));

        await RunAsync();

        _open.Select(a => a.ShipSymbol).Should().BeEquivalentTo("SHIP-3", "SHIP-4", "SHIP-5");
        _open.Should().OnlyContain(a => a.ContractId == ContractId && a.CargoSymbol == "COPPER_ORE" && a.OriginWaypoint == XB5C && a.DestWaypoint == H51 && a.RequiredUnits == 100);
        _log.Journal.Where(entry => entry.EventKind == "MiningStarted").Should().HaveCount(2)
            .And.OnlyContain(entry => Equals(entry.Properties["Reason"], "contract"));
    }

    [Fact]
    public async Task AMinerOnATrip_ASurveyor_AndAShipInTransit_DontJoin()
    {
        _goals.GetActiveGoalAsync("SHIP-4", Arg.Any<CancellationToken>())
            .Returns(new MineAndSellGoal { TradeSymbol = "IRON_ORE", SourceWaypointSymbol = XB5C, SellWaypointSymbol = H51 });
        _settings.GetAsync<bool>(AutomationSwitches.PlanEnabledSetting(AutomationPlan.Survey), Arg.Any<CancellationToken>()).Returns(true);
        Fleet(
            Drone("SHIP-3", XB5C, "IN_ORBIT"),
            Drone("SHIP-4"),
            CommandShip(),
            Drone("SHIP-5") with { Status = "IN_TRANSIT", ArrivesAt = DateTimeOffset.UtcNow.AddMinutes(2) });

        await RunAsync();

        _open.Select(a => a.ShipSymbol).Should().Equal("SHIP-3");
    }

    [Fact]
    public async Task WithTheSurveyPlanOff_TheCommandShipMinesForTheContractToo()
    {
        Fleet(Drone("SHIP-3", XB5C, "IN_ORBIT"), CommandShip(waypoint: H51, status: "DOCKED"));

        await RunAsync();

        _open.Select(a => a.ShipSymbol).Should().BeEquivalentTo("SHIP-3", "SHIP-1");
    }

    [Fact]
    public async Task TheUnitsStillNeeded_ReachEveryShipOnTheContract()
    {
        _open.Add(Assignment("SHIP-4", requiredUnits: 100));
        ContractIs(fulfilled: false, unitsFulfilled: 130);
        Fleet(Drone("SHIP-3", XB5C, "IN_ORBIT"), Drone("SHIP-4", XB5C, "IN_ORBIT"));

        await RunAsync();

        _open.Should().HaveCount(2).And.OnlyContain(a => a.RequiredUnits == 15);
    }

    [Fact]
    public async Task OnceTheContractIsFulfilled_EveryShipOnItIsReleased()
    {
        _open.Add(Assignment("SHIP-4"));
        ContractIs(fulfilled: true, unitsFulfilled: 145);
        Fleet(Drone("SHIP-3", XB5C, "IN_ORBIT"), Drone("SHIP-4", H51));

        await RunAsync();

        _open.Should().BeEmpty();
    }

    private static ContractMineralPlanState Plan() => new()
    {
        PlanId = Guid.NewGuid(),
        ContractId = ContractId,
        ShipSymbol = "SHIP-3",
        TradeSymbol = "COPPER_ORE",
        SourceWaypoint = XB5C,
        DestinationWaypoint = H51,
        UnitsRequired = 145,
        UnitsFulfilled = 45,
        Status = ContractMineralPlanStatus.Active,
        CreatedAt = Now,
        UpdatedAt = Now,
    };

    private static ShipAssignmentDto Assignment(string ship, int requiredUnits = 100)
        => new(ship, "Contract", XB5C, H51, "COPPER_ORE", ContractId, 0, Now, null, RequiredUnits: requiredUnits);

    private void ContractIs(bool fulfilled, int unitsFulfilled)
        => _contracts.FindAsync(ContractId, Arg.Any<CancellationToken>()).Returns(new ContractDto(
            Id: ContractId,
            FactionSymbol: "COSMIC",
            Type: "PROCUREMENT",
            IsAccepted: true,
            IsFulfilled: fulfilled,
            Expiration: Now.AddDays(7),
            DeadlineToAccept: Now,
            TermsDeadline: Now.AddDays(7),
            DeliverablesJson: JsonSerializer.Serialize(new List<ContractDeliverableDto> { new("COPPER_ORE", H51, 145, unitsFulfilled) })));

    private void Fleet(params ShipModel[] fleet) => _ships.GetAllAsync(Arg.Any<CancellationToken>()).Returns(fleet);

    private Task RunAsync()
        => new ContractPlanService(
                _plans,
                _contracts,
                _ships,
                _assignments,
                Substitute.For<IShipyardRepository>(),
                Substitute.For<IWaypointRepository>(),
                Substitute.For<ISpaceTradersPort>(),
                Substitute.For<IShipPurchaseService>(),
                Substitute.For<IAgentRepository>(),
                Substitute.For<IMessageBus>(),
                _goals,
                _settings,
                _log.For<ContractPlanService>())
            .EnsureBootstrappedAsync();
}
