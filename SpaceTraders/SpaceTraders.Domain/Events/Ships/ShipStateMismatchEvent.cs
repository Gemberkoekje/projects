using SpaceTraders.Domain.Events;

namespace SpaceTraders.Domain.Events.Ships;

public sealed record ShipStateMismatchEvent : ChainOfCommandEvent
{
    public string ShipSymbol { get; init; }

    public string CommandName { get; init; }

    public string RequiredState { get; init; }

    public string ActualState { get; init; }

    public string Reason { get; init; }

    public ShipStateMismatchEvent(
        string shipSymbol,
        string commandName,
        string requiredState,
        string actualState,
        string reason,
        Guid correlationId,
        Guid causationId,
        DateTimeOffset occurredAt)
        : base(correlationId, causationId, occurredAt)
    {
        ShipSymbol = shipSymbol;
        CommandName = commandName;
        RequiredState = requiredState;
        ActualState = actualState;
        Reason = reason;
    }
}
