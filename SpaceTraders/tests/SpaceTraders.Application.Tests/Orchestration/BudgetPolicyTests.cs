using FluentAssertions;
using NSubstitute;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Orchestration;
using SpaceTraders.Application.Ports;

namespace SpaceTraders.Application.Tests.Orchestration;

public sealed class BudgetPolicyTests
{
    private static IAgentRepository MakeAgent(long credits)
    {
        var agents = Substitute.For<IAgentRepository>();
        agents.GetAsync(Arg.Any<CancellationToken>()).Returns(
            new AgentModel("AGENT-1", null, null, credits, "COSMIC", 1));
        return agents;
    }

    private static ISettingsRepository MakeSettings(long reserve, int fabMatsThreshold = 2_500, int fabMatsTransactionSize = 60, bool hourlyCapEnabled = false)
    {
        var settings = Substitute.For<ISettingsRepository>();
        settings.GetAsync<long>("FleetExpansion.MinCreditReserve", Arg.Any<CancellationToken>()).Returns(reserve);
        settings.GetAsync<int>("Construction.FabMatsBuyThreshold", Arg.Any<CancellationToken>()).Returns(fabMatsThreshold);
        settings.GetAsync<int>("Construction.FabMatsTransactionSize", Arg.Any<CancellationToken>()).Returns(fabMatsTransactionSize);
        settings.GetAsync<bool>("Construction.HourlyBudgetCapEnabled", Arg.Any<CancellationToken>()).Returns(hourlyCapEnabled);
        return settings;
    }

    [Fact]
    public async Task EvaluateAsync_AllowsSpendBelowSpendable()
    {
        var policy = new BudgetPolicy(MakeAgent(500_000), MakeSettings(100_000));
        var decision = await policy.EvaluateAsync(50_000, CancellationToken.None);

        decision.CanAfford.Should().BeTrue();
        decision.SpendableCredits.Should().Be(400_000);
    }

    [Fact]
    public async Task EvaluateAsync_RejectsSpendThatBreachesReserve()
    {
        var policy = new BudgetPolicy(MakeAgent(150_000), MakeSettings(100_000));
        var decision = await policy.EvaluateAsync(80_000, CancellationToken.None);

        decision.CanAfford.Should().BeFalse();
        decision.SpendableCredits.Should().Be(50_000);
        decision.Reason.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task EvaluateAsync_ZeroProposedCost_AlwaysAffordable()
    {
        var policy = new BudgetPolicy(MakeAgent(1_000), MakeSettings(100_000));
        var decision = await policy.EvaluateAsync(0, CancellationToken.None);

        decision.CanAfford.Should().BeTrue();
        decision.SpendableCredits.Should().Be(0);
    }

    [Fact]
    public async Task EvaluateAsync_ReturnsFabMatsSettings()
    {
        var policy = new BudgetPolicy(MakeAgent(500_000), MakeSettings(100_000, fabMatsThreshold: 2_400, fabMatsTransactionSize: 55, hourlyCapEnabled: false));
        var decision = await policy.EvaluateAsync(0, CancellationToken.None);

        decision.FabMatsBuyThreshold.Should().Be(2_400);
        decision.FabMatsTransactionSize.Should().Be(55);
        decision.HourlyConstructionBudgetCapEnabled.Should().BeFalse();
    }
}
