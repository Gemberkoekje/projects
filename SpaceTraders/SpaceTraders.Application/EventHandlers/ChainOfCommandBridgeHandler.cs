using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Events.Dispatching;

namespace SpaceTraders.Application.EventHandlers;

/// <summary>
/// Wolverine message handler that routed incoming chain-of-command events to the
/// <see cref="IChainOfCommandDispatcher"/>.
/// Phase 7d: All overloads removed (ShipInTransitEvent was the last). Phase 7f will delete this class
/// and the chain infrastructure entirely.
/// </summary>
#pragma warning disable CA1812 // Avoid uninstantiated internal classes — retained for Phase 7f deletion
public sealed class ChainOfCommandBridgeHandler
{
}
#pragma warning restore CA1812
