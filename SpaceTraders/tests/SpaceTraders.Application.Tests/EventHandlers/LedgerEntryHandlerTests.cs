using FluentAssertions;
using NSubstitute;
using SpaceTraders.Application.Automation;
using SpaceTraders.Application.EventHandlers;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Events;
using SpaceTraders.Domain.ValueObjects;
using Wolverine;

namespace SpaceTraders.Application.Tests.EventHandlers;

public sealed class LedgerEntryHandlerTests
{
    private readonly ILedgerRepository _ledger = Substitute.For<ILedgerRepository>();
    private readonly IAutomationMetrics _metrics = Substitute.For<IAutomationMetrics>();
    private readonly LedgerEntryHandler _handler;

    public LedgerEntryHandlerTests()
    {
        _handler = new LedgerEntryHandler(_ledger, _metrics, Substitute.For<IDashboardNotifier>());
    }

    [Fact]
    public async Task ARefuel_IsALedgerRow_AndCreditsSpentOnFuel()
    {
        await _handler.Handle(new ShipRefueledEvent("AGENT-1", 720, 174_280, "X1-AB-2"), CancellationToken.None);

        await _ledger.Received(1).AppendAsync(
            "AGENT-1",
            LedgerCategory.FuelPurchase,
            -720,
            waypointSymbol: "X1-AB-2",
            cancellationToken: Arg.Any<CancellationToken>());
        _metrics.Received(1).CreditsSpent("FuelPurchase", 720);
        _metrics.DidNotReceive().CreditsEarned(Arg.Any<string>(), Arg.Any<long>());
    }

    [Fact]
    public async Task ASale_IsALedgerRow_AndCreditsEarned()
    {
        await _handler.Handle(new ShipCargoSoldEvent("AGENT-1", new TradeSymbol("IRON_ORE"), 10, 450, 175_450), CancellationToken.None);

        await _ledger.Received(1).AppendAsync("AGENT-1", LedgerCategory.TradeSell, 450, goodSymbol: "IRON_ORE", units: 10, cancellationToken: Arg.Any<CancellationToken>());
        _metrics.Received(1).CreditsEarned("TradeSell", 450);
    }

    [Fact]
    public async Task ACargoPurchase_IsALedgerRow_AndCreditsSpent()
    {
        await _handler.Handle(new CargoPurchasedEvent("AGENT-1", new TradeSymbol("FOOD"), 40, 8_000, 180_000, "X1-AB-BUY"), CancellationToken.None);

        await _ledger.Received(1).AppendAsync("AGENT-1", LedgerCategory.TradeBuy, -8_000, goodSymbol: "FOOD", unitPrice: 200, units: 40, waypointSymbol: "X1-AB-BUY", cancellationToken: Arg.Any<CancellationToken>());
        _metrics.Received(1).CreditsSpent("TradeBuy", 8_000);
    }

    [Fact]
    public async Task AContractAcceptance_IsALedgerRow_AndCreditsEarned()
    {
        await _handler.Handle(new ContractAcceptedEvent("C-1", 1_136), CancellationToken.None);

        await _ledger.Received(1).AppendAsync("AGENT", LedgerCategory.ContractDeposit, 1_136, sourceEventId: "C-1", cancellationToken: Arg.Any<CancellationToken>());
        _metrics.Received(1).CreditsEarned("ContractDeposit", 1_136);
    }

    [Fact]
    public async Task AContractAcceptanceWithoutPayment_IsNoLedgerRow()
    {
        await _handler.Handle(new ContractAcceptedEvent("C-1"), CancellationToken.None);

        _ledger.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task AContractFulfilment_IsALedgerRow_AndCreditsEarned()
    {
        await _handler.Handle(new ContractFulfilledEvent("C-1", 6_620), CancellationToken.None);

        await _ledger.Received(1).AppendAsync("AGENT", LedgerCategory.ContractPayout, 6_620, sourceEventId: "C-1", cancellationToken: Arg.Any<CancellationToken>());
        _metrics.Received(1).CreditsEarned("ContractPayout", 6_620);
    }

    [Fact]
    public async Task AShipPurchase_IsALedgerRow_AndCreditsSpent()
    {
        await _handler.Handle(new NewShipPurchasedEvent("AGENT-2", ShipType.ShipMiningDrone, 12_000), CancellationToken.None);

        await _ledger.Received(1).AppendAsync("AGENT-2", LedgerCategory.ShipPurchase, -12_000, goodSymbol: "ShipMiningDrone", cancellationToken: Arg.Any<CancellationToken>());
        _metrics.Received(1).CreditsSpent("ShipPurchase", 12_000);
    }
}

public sealed class MessageMetricsMiddlewareTests
{
    [Fact]
    public void AHandledMessage_IsCountedByItsType()
    {
        var metrics = Substitute.For<IAutomationMetrics>();

        MessageMetricsMiddleware.After(new Envelope(new ShipRefueledEvent("AGENT-1", 720, 174_280, "X1-AB-2")), metrics);

        metrics.Received(1).MessageHandled("ShipRefueledEvent");
    }
}

public sealed class ProbeDeploymentCreditsChangedHandlerTests
{
    private readonly IProbeDeploymentPlanService _probes = Substitute.For<IProbeDeploymentPlanService>();
    private readonly ISettingsRepository _settings = Substitute.For<ISettingsRepository>();

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task ACreditChange_LeavesTheProbePlanAlone_WhileItOrAutomationIsOff(bool automationOn, bool planOn)
    {
        // Credit changes are published now (B7), and waking the probe plan can buy probes: a plan that
        // is switched off buys nothing (D9).
        _settings.GetAsync<bool>("Automation.Enabled", Arg.Any<CancellationToken>()).Returns(automationOn);
        _settings.GetAsync<bool>("Automation.Plan.ProbeDeployment.Enabled", Arg.Any<CancellationToken>()).Returns(planOn);

        await new ProbeDeploymentCreditsChangedHandler(_probes, _settings).Handle(new AgentCreditsChangedEvent(100_000, 250_000), CancellationToken.None);

        await _probes.DidNotReceive().OnCreditsChangedAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ACreditChange_WakesTheProbePlan_WhenItIsOn()
    {
        _settings.GetAsync<bool>(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);

        await new ProbeDeploymentCreditsChangedHandler(_probes, _settings).Handle(new AgentCreditsChangedEvent(100_000, 250_000), CancellationToken.None);

        await _probes.Received(1).OnCreditsChangedAsync(Arg.Any<CancellationToken>());
    }
}
