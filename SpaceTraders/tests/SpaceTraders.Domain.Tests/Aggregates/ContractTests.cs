using FluentAssertions;
using SpaceTraders.Domain.Aggregates;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Events;
using SpaceTraders.Domain.ValueObjects;

namespace SpaceTraders.Domain.Tests.Aggregates;

public sealed class ContractTests
{
    private static Contract CreateContract(bool isAccepted = false, bool isFulfilled = false, DateTimeOffset? deadline = null) =>
        Contract.Reconstitute(
            id: "CONTRACT-1",
            factionSymbol: "COSMIC",
            type: ContractType.Procurement,
            terms: new ContractTerms(
                Deadline: deadline ?? DateTimeOffset.UtcNow.AddDays(7),
                Payment: new ContractPayment(10_000, 50_000),
                DeliverGoods: []),
            isAccepted: isAccepted,
            isFulfilled: isFulfilled,
            expiration: DateTimeOffset.UtcNow.AddDays(30));

    [Fact]
    public void Accept_NotYetAccepted_RaisesContractAcceptedEvent()
    {
        var contract = CreateContract(isAccepted: false);

        contract.Accept();

        contract.DomainEvents.Should().ContainSingle(e => e is ContractAcceptedEvent);
        contract.IsAccepted.Should().BeTrue();
    }

    [Fact]
    public void Accept_AlreadyAccepted_DoesNotRaiseEventAgain()
    {
        var contract = CreateContract(isAccepted: true);

        contract.Accept();

        contract.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Fulfill_NotYetFulfilled_RaisesContractFulfilledEvent()
    {
        var contract = CreateContract();

        contract.Fulfill(50_000);

        contract.DomainEvents.Should().ContainSingle(e => e is ContractFulfilledEvent);
        var evt = (ContractFulfilledEvent)contract.DomainEvents[0];
        evt.ContractId.Should().Be("CONTRACT-1");
        evt.Payment.Should().Be(50_000);
    }

    [Fact]
    public void Fulfill_AlreadyFulfilled_DoesNotRaiseEventAgain()
    {
        var contract = CreateContract(isFulfilled: true);

        contract.Fulfill(50_000);

        contract.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void CheckDeadline_Within24Hours_RaisesDeadlineApproachingEvent()
    {
        var deadline = DateTimeOffset.UtcNow.AddHours(12);
        var contract = CreateContract(deadline: deadline);

        contract.CheckDeadline(DateTimeOffset.UtcNow);

        contract.DomainEvents.Should().ContainSingle(e => e is ContractDeadlineApproachingEvent);
    }

    [Fact]
    public void CheckDeadline_MoreThan24HoursAway_DoesNotRaiseEvent()
    {
        var contract = CreateContract(deadline: DateTimeOffset.UtcNow.AddDays(3));

        contract.CheckDeadline(DateTimeOffset.UtcNow);

        contract.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void CheckDeadline_AlreadyPassed_DoesNotRaiseEvent()
    {
        var contract = CreateContract(deadline: DateTimeOffset.UtcNow.AddHours(-1));

        contract.CheckDeadline(DateTimeOffset.UtcNow);

        contract.DomainEvents.Should().BeEmpty();
    }
}
