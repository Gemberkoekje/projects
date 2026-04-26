namespace SpaceTraders.Domain.ValueObjects;

public sealed record ContractPayment
{
    public required long OnAccepted { get; init; }

    public required long OnFulfilled { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public ContractPayment(long OnAccepted, long OnFulfilled)
    {
        this.OnAccepted = OnAccepted;
        this.OnFulfilled = OnFulfilled;
    }
}

public sealed record DeliverGood
{
    public required TradeSymbol Symbol { get; init; }

    public required WaypointSymbol Destination { get; init; }

    public required int UnitsRequired { get; init; }

    public required int UnitsFulfilled { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public DeliverGood(TradeSymbol Symbol, WaypointSymbol Destination, int UnitsRequired, int UnitsFulfilled)
    {
        this.Symbol = Symbol;
        this.Destination = Destination;
        this.UnitsRequired = UnitsRequired;
        this.UnitsFulfilled = UnitsFulfilled;
    }
}

public sealed record ContractTerms
{
    public required DateTimeOffset Deadline { get; init; }

    public required ContractPayment Payment { get; init; }

    public required IReadOnlyList<DeliverGood> DeliverGoods { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public ContractTerms(DateTimeOffset Deadline, ContractPayment Payment, IReadOnlyList<DeliverGood> DeliverGoods)
    {
        this.Deadline = Deadline;
        this.Payment = Payment;
        this.DeliverGoods = DeliverGoods;
    }
}
