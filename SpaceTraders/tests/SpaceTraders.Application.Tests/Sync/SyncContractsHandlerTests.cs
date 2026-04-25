using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Sync;
using SpaceTraders.Infrastructure.Persistence.Repositories;

namespace SpaceTraders.Application.Tests.Sync;

public sealed class SyncContractsHandlerTests
{
    private static ContractModel MakeContract(string id, bool accepted = false) =>
        new(id, "COSMIC", "PROCUREMENT", accepted, false,
            DateTimeOffset.UtcNow.AddDays(7), DateTimeOffset.UtcNow.AddDays(1));

    private static PagedResult<ContractModel> OnePage(params ContractModel[] contracts) =>
        new(contracts, contracts.Length, 1, 20);

    [Fact]
    public async Task Handle_PersistsContracts()
    {
        var port = Substitute.For<ISpaceTradersPort>();
        port.GetMyContractsAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(OnePage(MakeContract("CONTRACT-1"), MakeContract("CONTRACT-2")));

        await using var db = TestDbContextFactory.Create();
        var contracts = new ContractRepository(db);

        await new SyncContractsHandler(port, contracts, NullLogger<SyncContractsHandler>.Instance)
            .Handle(new SyncContractsCommand(), CancellationToken.None);

        db.Contracts.Should().HaveCount(2);
    }

    [Fact]
    public async Task Handle_UpdatesAcceptedFlag()
    {
        var port = Substitute.For<ISpaceTradersPort>();
        port.GetMyContractsAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(OnePage(MakeContract("CONTRACT-1", accepted: true)));

        await using var db = TestDbContextFactory.Create();
        var contracts = new ContractRepository(db);

        await new SyncContractsHandler(port, contracts, NullLogger<SyncContractsHandler>.Instance)
            .Handle(new SyncContractsCommand(), CancellationToken.None);

        var contract = await db.Contracts.FindAsync("CONTRACT-1");
        contract!.IsAccepted.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_DoesNotIssueNonContractGetCalls()
    {
        var port = Substitute.For<ISpaceTradersPort>();
        port.GetMyContractsAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(OnePage(MakeContract("CONTRACT-1")));

        await using var db = TestDbContextFactory.Create();
        var contracts = new ContractRepository(db);

        await new SyncContractsHandler(port, contracts, NullLogger<SyncContractsHandler>.Instance)
            .Handle(new SyncContractsCommand(), CancellationToken.None);

        await port.DidNotReceive().GetMyAgentAsync(Arg.Any<CancellationToken>());
        await port.DidNotReceive().GetMyShipsAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }
}
