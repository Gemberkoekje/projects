namespace SpaceTraders.Domain.ValueObjects;

public sealed record ContractPayment(long OnAccepted, long OnFulfilled);

public sealed record DeliverGood(
    TradeSymbol Symbol,
    WaypointSymbol Destination,
    int UnitsRequired,
    int UnitsFulfilled);

public sealed record ContractTerms(
    DateTimeOffset Deadline,
    ContractPayment Payment,
    IReadOnlyList<DeliverGood> DeliverGoods);
