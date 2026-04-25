using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SpaceTraders.Application.Sync;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Clients;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Common;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Contracts;

namespace SpaceTraders.Application.Tests.Sync;

public sealed class SyncContractsHandlerTests
{
    private static Contract MakeContract(string id, bool accepted = false) =>
        new()
        {
            Id = id,
            FactionSymbol = "COSMIC",
            Type = "PROCUREMENT",
            Accepted = accepted,
            Fulfilled = false,
            Expiration = DateTimeOffset.UtcNow.AddDays(7),
            DeadlineToAccept = DateTimeOffset.UtcNow.AddDays(1)
        };

    private static PagedApiResponse<Contract> OnePage(params Contract[] contracts) =>
        new() { Data = contracts, Meta = new Meta { Total = contracts.Length, Page = 1, Limit = 20 } };

    [Fact]
    public async Task Handle_PersistsContracts()
    {
        var apiClient = Substitute.For<ISpaceTradersApiClient>();
        apiClient.GetMyContractsAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
                 .Returns(OnePage(MakeContract("CONTRACT-1"), MakeContract("CONTRACT-2")));

        await using var db = TestDbContextFactory.Create();
        var handler = new SyncContractsHandler(apiClient, db, NullLogger<SyncContractsHandler>.Instance);

        await handler.Handle(new SyncContractsCommand(), CancellationToken.None);

        db.Contracts.Should().HaveCount(2);
    }

    [Fact]
    public async Task Handle_UpdatesAcceptedFlag()
    {
        var apiClient = Substitute.For<ISpaceTradersApiClient>();
        apiClient.GetMyContractsAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
                 .Returns(OnePage(MakeContract("CONTRACT-1", accepted: true)));

        await using var db = TestDbContextFactory.Create();
        var handler = new SyncContractsHandler(apiClient, db, NullLogger<SyncContractsHandler>.Instance);

        await handler.Handle(new SyncContractsCommand(), CancellationToken.None);

        var contract = await db.Contracts.FindAsync("CONTRACT-1");
        contract!.IsAccepted.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_DoesNotIssueNonContractGetCalls()
    {
        var apiClient = Substitute.For<ISpaceTradersApiClient>();
        apiClient.GetMyContractsAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
                 .Returns(OnePage(MakeContract("CONTRACT-1")));

        await using var db = TestDbContextFactory.Create();
        var handler = new SyncContractsHandler(apiClient, db, NullLogger<SyncContractsHandler>.Instance);

        await handler.Handle(new SyncContractsCommand(), CancellationToken.None);

        await apiClient.DidNotReceive().GetMyAgentAsync(Arg.Any<CancellationToken>());
        await apiClient.DidNotReceive().GetMyShipsAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }
}
