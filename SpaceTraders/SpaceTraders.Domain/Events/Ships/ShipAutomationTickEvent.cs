namespace SpaceTraders.Domain.Events.Ships;

/// <summary>
/// Phase 5: explicit, factual automation event that asks the ship-automation pipeline to
/// evaluate a single ship and (if appropriate) issue exactly one command.
/// </summary>
/// <remarks>
/// <para>
/// Unlike the legacy chain-of-command events (e.g. <see cref="ShipInOrbitEvent"/>,
/// <see cref="ShipDockedEvent"/>), this event is intentionally <b>not</b> derived from
/// <see cref="ChainOfCommandEvent"/>. Phase 5 of the ship automation architecture plan
/// removes inheritance-based routing: state-change events such as <see cref="ShipArrivedEvent"/>
/// or <see cref="ShipUndockedEvent"/> should no longer be implicitly dispatched as a base
/// type to drive automation. Instead, callers publish this explicit automation tick which is
/// routed to a single ship automation handler.
/// </para>
/// <para>
/// The event carries only what is required to identify the ship and trace the cause; the
/// authoritative ship state is loaded by the handler via <c>IShipRepository</c>.
/// </para>
/// </remarks>
public sealed record ShipAutomationTickEvent
{
    public required string ShipSymbol { get; init; }

    /// <summary>Optional reason describing why this tick was queued (state change, periodic re-evaluation, etc.).</summary>
    public string Reason { get; init; } = string.Empty;

    public Guid EventId { get; init; }

    public DateTimeOffset OccurredAt { get; init; }

    public Guid CorrelationId { get; init; }

    public Guid CausationId { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public ShipAutomationTickEvent(
        string shipSymbol,
        string reason,
        DateTimeOffset occurredAt,
        Guid correlationId,
        Guid causationId)
    {
        ShipSymbol = shipSymbol;
        Reason = reason;
        EventId = Guid.NewGuid();
        OccurredAt = occurredAt;
        CorrelationId = correlationId == Guid.Empty ? EventId : correlationId;
        CausationId = causationId;
    }
}
