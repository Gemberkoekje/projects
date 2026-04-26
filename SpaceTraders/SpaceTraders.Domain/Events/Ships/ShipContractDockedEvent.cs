namespace SpaceTraders.Domain.Events.Ships;

public sealed record ShipContractDockedEvent : ShipDockedEvent
{
    public string ContractId { get; init; }

    public ShipContractDockedEvent(
        string shipSymbol,
        string systemSymbol,
        string waypointSymbol,
        string contractId,
        Guid correlationId,
        Guid causationId,
        DateTimeOffset occurredAt)
        : base(shipSymbol, systemSymbol, waypointSymbol, correlationId, causationId, occurredAt)
    {
        ContractId = contractId;
    }
}
