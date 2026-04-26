using SpaceTraders.Domain.Common;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Events;
using SpaceTraders.Domain.ValueObjects;

namespace SpaceTraders.Domain.Aggregates;

[Mutable]
public sealed class Contract : AggregateRoot
{
    public string Id { get; private set; }
    public string FactionSymbol { get; private set; }
    public ContractType Type { get; private set; }
    public ContractTerms Terms { get; private set; }
    public bool IsAccepted { get; private set; }
    public bool IsFulfilled { get; private set; }
    public DateTimeOffset Expiration { get; private set; }

    private Contract(
        string id,
        string factionSymbol,
        ContractType type,
        ContractTerms terms,
        bool isAccepted,
        bool isFulfilled,
        DateTimeOffset expiration)
    {
        Id = id;
        FactionSymbol = factionSymbol;
        Type = type;
        Terms = terms;
        IsAccepted = isAccepted;
        IsFulfilled = isFulfilled;
        Expiration = expiration;
    }

    public static Contract Reconstitute(
        string id,
        string factionSymbol,
        ContractType type,
        ContractTerms terms,
        bool isAccepted,
        bool isFulfilled,
        DateTimeOffset expiration)
        => new(id, factionSymbol, type, terms, isAccepted, isFulfilled, expiration);

    public void Accept()
    {
        if (IsAccepted) return;
        IsAccepted = true;
        RaiseDomainEvent(new ContractAcceptedEvent(Id));
    }

    public void Fulfill(long payment)
    {
        if (IsFulfilled) return;
        IsFulfilled = true;
        RaiseDomainEvent(new ContractFulfilledEvent(Id, payment));
    }

    public void CheckDeadline(DateTimeOffset now)
    {
        var remaining = Terms.Deadline - now;
        if (remaining <= TimeSpan.FromHours(24) && remaining > TimeSpan.Zero)
            RaiseDomainEvent(new ContractDeadlineApproachingEvent(Id, remaining));
    }
}
