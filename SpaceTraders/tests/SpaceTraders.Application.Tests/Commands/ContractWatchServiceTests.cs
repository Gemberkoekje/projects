using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SpaceTraders.Application.Automation;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Domain.Events;
using Wolverine;

namespace SpaceTraders.Application.Tests.Commands;

public sealed class ContractWatchServiceTests
{
    private static ServiceProvider BuildProvider(IContractRepository contracts, IMessageBus bus)
    {
        var services = new ServiceCollection();
        services.AddSingleton(contracts);
        services.AddSingleton(bus);
        return services.BuildServiceProvider();
    }

    private static ContractDto MakeAcceptedContract(string id, DateTimeOffset deadline) =>
        new(id, "COSMIC", "PROCUREMENT", IsAccepted: true, IsFulfilled: false, Expiration: deadline, DeadlineToAccept: null);

    [Fact]
    public async Task CheckContracts_PublishesWarning_WhenDeadlineWithin24Hours()
    {
        var contract = MakeAcceptedContract("CONTRACT-1", DateTimeOffset.UtcNow.AddHours(12));

        var contractRepo = Substitute.For<IContractRepository>();
        contractRepo.GetActiveAsync(Arg.Any<CancellationToken>()).Returns([contract]);

        var bus = Substitute.For<IMessageBus>();
        var provider = BuildProvider(contractRepo, bus);

        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
        var service = new ContractWatchService(scopeFactory, NullLogger<ContractWatchService>.Instance);

        await service.InvokeCheckContractsForTestAsync(CancellationToken.None);

        await bus.Received(1).PublishAsync(
            Arg.Is<ContractDeadlineApproachingEvent>(e => e.ContractId == "CONTRACT-1"),
            Arg.Any<DeliveryOptions>());
    }

    [Fact]
    public async Task CheckContracts_DoesNotPublish_WhenDeadlineFar()
    {
        var contract = MakeAcceptedContract("CONTRACT-1", DateTimeOffset.UtcNow.AddDays(7));

        var contractRepo = Substitute.For<IContractRepository>();
        contractRepo.GetActiveAsync(Arg.Any<CancellationToken>()).Returns([contract]);

        var bus = Substitute.For<IMessageBus>();
        var provider = BuildProvider(contractRepo, bus);

        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
        var service = new ContractWatchService(scopeFactory, NullLogger<ContractWatchService>.Instance);

        await service.InvokeCheckContractsForTestAsync(CancellationToken.None);

        await bus.DidNotReceive().PublishAsync(Arg.Any<ContractDeadlineApproachingEvent>(), Arg.Any<DeliveryOptions>());
    }

    [Fact]
    public async Task CheckContracts_DoesNotRepeatWarning_OnSecondCall()
    {
        var contract = MakeAcceptedContract("CONTRACT-1", DateTimeOffset.UtcNow.AddHours(12));

        var contractRepo = Substitute.For<IContractRepository>();
        contractRepo.GetActiveAsync(Arg.Any<CancellationToken>()).Returns([contract]);

        var bus = Substitute.For<IMessageBus>();
        var provider = BuildProvider(contractRepo, bus);

        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
        var service = new ContractWatchService(scopeFactory, NullLogger<ContractWatchService>.Instance);

        await service.InvokeCheckContractsForTestAsync(CancellationToken.None);
        await service.InvokeCheckContractsForTestAsync(CancellationToken.None);

        await bus.Received(1).PublishAsync(Arg.Any<ContractDeadlineApproachingEvent>(), Arg.Any<DeliveryOptions>());
    }

    [Fact]
    public async Task CheckContracts_SkipsUnacceptedContracts()
    {
        var contract = new ContractDto(
            "CONTRACT-1", "COSMIC", "PROCUREMENT",
            IsAccepted: false, IsFulfilled: false,
            Expiration: DateTimeOffset.UtcNow.AddHours(12),
            DeadlineToAccept: null);

        var contractRepo = Substitute.For<IContractRepository>();
        contractRepo.GetActiveAsync(Arg.Any<CancellationToken>()).Returns([contract]);

        var bus = Substitute.For<IMessageBus>();
        var provider = BuildProvider(contractRepo, bus);

        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
        var service = new ContractWatchService(scopeFactory, NullLogger<ContractWatchService>.Instance);

        await service.InvokeCheckContractsForTestAsync(CancellationToken.None);

        await bus.DidNotReceive().PublishAsync(Arg.Any<ContractDeadlineApproachingEvent>(), Arg.Any<DeliveryOptions>());
    }
}
