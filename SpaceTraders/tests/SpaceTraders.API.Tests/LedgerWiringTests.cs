using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Events;
using SpaceTraders.Domain.ValueObjects;
using Wolverine;

namespace SpaceTraders.API.Tests;

/// <summary>
/// B7: sales, purchases, contract payments and ship purchases reach the ledger and the metrics. This
/// sends each event through the host's own Wolverine, handlers and middleware included.
/// </summary>
public sealed class LedgerWiringTests
{
    [Fact]
    public async Task EachMoneyEvent_IsALedgerRow_AndMovesItsCounter()
    {
        var metrics = Substitute.For<IAutomationMetrics>();
        await using var parent = new SpaceTradersApiFactory();
        await using var factory = parent.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IAutomationMetrics>();
            services.AddSingleton(metrics);
        }));
        using var scope = factory.Services.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

        await bus.InvokeAsync(new ShipCargoSoldEvent("AGENT-1", new TradeSymbol("IRON_ORE"), 10, 450, 175_450));
        await bus.InvokeAsync(new CargoPurchasedEvent("AGENT-1", new TradeSymbol("FOOD"), 40, 8_000, 167_450, "X1-AB-BUY"));
        await bus.InvokeAsync(new ContractAcceptedEvent("C-1", 1_136));
        await bus.InvokeAsync(new ContractFulfilledEvent("C-1", 6_620));
        await bus.InvokeAsync(new NewShipPurchasedEvent("AGENT-2", ShipType.ShipMiningDrone, 12_000));

        var ledger = parent.LedgerRepository;
        await ledger.Received(1).AppendAsync("AGENT-1", LedgerCategory.TradeSell, 450, Arg.Any<string?>(), Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
        await ledger.Received(1).AppendAsync("AGENT-1", LedgerCategory.TradeBuy, -8_000, Arg.Any<string?>(), Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
        await ledger.Received(1).AppendAsync("AGENT", LedgerCategory.ContractDeposit, 1_136, Arg.Any<string?>(), Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
        await ledger.Received(1).AppendAsync("AGENT", LedgerCategory.ContractPayout, 6_620, Arg.Any<string?>(), Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
        await ledger.Received(1).AppendAsync("AGENT-2", LedgerCategory.ShipPurchase, -12_000, Arg.Any<string?>(), Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());

        metrics.Received(1).CreditsEarned("TradeSell", 450);
        metrics.Received(1).CreditsSpent("TradeBuy", 8_000);
        metrics.Received(1).CreditsEarned("ContractDeposit", 1_136);
        metrics.Received(1).CreditsEarned("ContractPayout", 6_620);
        metrics.Received(1).CreditsSpent("ShipPurchase", 12_000);
        metrics.Received(1).MessageHandled("ShipCargoSoldEvent");
    }
}
