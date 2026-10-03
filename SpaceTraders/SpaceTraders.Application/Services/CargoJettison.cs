using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Enums;

namespace SpaceTraders.Application.Services;

/// <summary>Throws cargo overboard that nothing will sell or use (D42).</summary>
public interface ICargoJettison
{
    /// <summary>
    /// Jettisons all of one good the ship holds, stores the hold the API answers with, counts it and journals it
    /// (<c>CargoJettisoned</c>). A ship in flight keeps it; a jettison the API refuses is logged at Warning and keeps the
    /// cargo aboard, and the same good of the same ship isn't tried again for <see cref="JettisonRetries.Wait"/>: the
    /// trading plan asks on every tick.
    /// </summary>
    /// <param name="ship">The ship, not in flight.</param>
    /// <param name="cargo">The good and the units aboard.</param>
    /// <param name="reason">Why it goes: <c>no_buyer</c> or <c>not_worth_the_fuel</c>.</param>
    /// <param name="cancellationToken">Stops the work.</param>
    /// <returns>The ship's hold afterwards, or null when nothing was jettisoned.</returns>
    Task<CargoModel?> JettisonAsync(ShipModel ship, CargoItemModel cargo, string reason, CancellationToken cancellationToken);
}

/// <inheritdoc />
public sealed class CargoJettison(
    ISpaceTradersPort port,
    IShipRepository ships,
    IAutomationMetrics metrics,
    JettisonRetries retries,
    ILogger<CargoJettison> logger) : ICargoJettison
{
    /// <inheritdoc />
    public Task<CargoModel?> JettisonAsync(ShipModel ship, CargoItemModel cargo, string reason, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ship);
        ArgumentNullException.ThrowIfNull(cargo);

        return ship.LocalStatus == ShipLocalStatus.InTransit
            || cargo.Units <= 0
            || !retries.MayTry(ship.Symbol, cargo.Symbol, TimeProvider.System.GetUtcNow())
                ? Task.FromResult<CargoModel?>(null)
                : JettisonAllAsync(ship, cargo, reason, cancellationToken);
    }

    private async Task<CargoModel?> JettisonAllAsync(ShipModel ship, CargoItemModel cargo, string reason, CancellationToken cancellationToken)
    {
        CargoModel left;
        try
        {
            left = (await port.JettisonCargoAsync(ship.Symbol, cargo.Symbol, cargo.Units, cancellationToken)).Cargo;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            retries.Failed(ship.Symbol, cargo.Symbol, TimeProvider.System.GetUtcNow());
            logger.LogWarning(
                ex,
                "Couldn't jettison the {Units} {TradeSymbol} of ship {ShipSymbol}; it stays aboard, and is tried again in {Minutes} minutes.",
                cargo.Units,
                cargo.Symbol,
                ship.Symbol,
                JettisonRetries.Wait.TotalMinutes);
            return null;
        }

        await ships.UpdateCargoAsync(ship.Symbol, left, cancellationToken);
        metrics.Jettisoned(ship.Symbol, cargo.Symbol, cargo.Units);
        logger.LogInformation(
            "{EventKind:l}: ship {ShipSymbol} jettisons {Units} {TradeSymbol} at {WaypointSymbol} ({Reason}): nothing will sell or use it.",
            JournalEvents.CargoJettisoned,
            ship.Symbol,
            cargo.Units,
            cargo.Symbol,
            ship.WaypointSymbol ?? string.Empty,
            reason);
        return left;
    }
}

/// <summary>
/// The jettisons the API refused, by ship and good, in memory (D42): the trading plan asks again on every tick while the
/// ship stays free, and a refusal that repeats would cost a call each time. A restart forgets them.
/// </summary>
/// <remarks>A singleton; thread-safe.</remarks>
public sealed class JettisonRetries
{
    /// <summary>How long a refused jettison waits before it is tried again.</summary>
    public static readonly TimeSpan Wait = TimeSpan.FromMinutes(10);

    private readonly Lock _gate = new();
    private readonly Dictionary<(string Ship, string Good), DateTimeOffset> _refusedAt = [];

    /// <summary>Whether a jettison of the good may be tried now: never refused, or refused at least <see cref="Wait"/> ago.</summary>
    /// <param name="shipSymbol">The ship.</param>
    /// <param name="tradeSymbol">The good.</param>
    /// <param name="now">The time.</param>
    /// <returns>True when it may be tried.</returns>
    public bool MayTry(string shipSymbol, string tradeSymbol, DateTimeOffset now)
    {
        lock (_gate)
        {
            return !_refusedAt.TryGetValue(Key(shipSymbol, tradeSymbol), out var at) || now - at >= Wait;
        }
    }

    /// <summary>Records a refused jettison.</summary>
    /// <param name="shipSymbol">The ship.</param>
    /// <param name="tradeSymbol">The good.</param>
    /// <param name="now">When the API refused it.</param>
    public void Failed(string shipSymbol, string tradeSymbol, DateTimeOffset now)
    {
        lock (_gate)
        {
            _refusedAt[Key(shipSymbol, tradeSymbol)] = now;
        }
    }

    private static (string Ship, string Good) Key(string shipSymbol, string tradeSymbol)
        => (shipSymbol.ToUpperInvariant(), tradeSymbol.ToUpperInvariant());
}
