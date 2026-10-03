using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Goals;

namespace SpaceTraders.Application.Services;

/// <summary>
/// Books what each trip made after fuel when it ends (D46), so what trading, mining, siphoning, spare time and contracts
/// earn shows without comparing the credits before and after.
/// </summary>
public interface ITripBook
{
    /// <summary>
    /// Books a trip that ended: what its sales brought in (<see cref="TripGoal.Earned"/>), less what its cargo cost
    /// (<see cref="TripGoal.Spent"/>) and the fuel its ship bought since the trip started, as the ledger has it. Counts the
    /// trip and what it made or lost, by activity, and journals it (<c>TripEnded</c>).
    /// </summary>
    /// <param name="shipSymbol">The ship.</param>
    /// <param name="trip">The trip as it ended, with everything it sold and bought.</param>
    /// <param name="reason">
    /// Why it ended: <see cref="TripBook.Sold"/>, <see cref="TripBook.Interrupted"/>, <see cref="TripBook.Runaway"/>,
    /// <see cref="TripBook.Rejected"/>, <see cref="TripBook.NothingAboard"/>, <see cref="TripBook.NoBuyer"/>, or why a trade
    /// was dropped (<c>not_bought_here</c>, <c>not_lucrative</c>, <c>not_possible</c>).
    /// </param>
    /// <param name="cancellationToken">Stops the work.</param>
    /// <returns>A task that completes once the trip is booked.</returns>
    Task BookAsync(string shipSymbol, TripGoal trip, string reason, CancellationToken cancellationToken);

    /// <summary>
    /// Books a contract round trip at its delivery (<see cref="TripBook.Delivered"/>): the fuel its ship bought since it was
    /// assigned, as a loss. The contract's deposit and payout count as its profit when they come.
    /// </summary>
    /// <param name="shipSymbol">The ship.</param>
    /// <param name="assignedAt">When the ship was assigned the round trip.</param>
    /// <param name="cancellationToken">Stops the work.</param>
    /// <returns>A task that completes once the round trip is booked.</returns>
    Task BookContractTripAsync(string shipSymbol, DateTimeOffset assignedAt, CancellationToken cancellationToken);
}

/// <inheritdoc />
/// <remarks>
/// A trip's sales and purchases are its own figures, kept with its goal: the ledger books them from events a moment
/// later, so the last sale may not be there yet when the trip ends. Fuel is bought when a ship leaves a market, minutes
/// before its trip ends, so that comes from the ledger.
/// </remarks>
public sealed class TripBook(ILedgerRepository ledger, IAutomationMetrics metrics, ILogger<TripBook> logger) : ITripBook
{
    /// <summary>A trade trip (<see cref="TradeBetweenMarketsGoal"/>).</summary>
    public const string Trade = "trade";

    /// <summary>A mining trip (<see cref="MineAndSellGoal"/>).</summary>
    public const string Mining = "mining";

    /// <summary>A siphon trip (<see cref="SiphonAndSellGoal"/>).</summary>
    public const string Siphoning = "siphoning";

    /// <summary>A spare-time trip (<see cref="GatherAndSellGoal"/>).</summary>
    public const string SpareTime = "spare_time";

    /// <summary>Contract work: a round trip, and the contract's deposit and payout.</summary>
    public const string Contract = "contract";

    /// <summary>The trip sold its cargo, or what of it pays for its fuel.</summary>
    public const string Sold = "sold";

    /// <summary>A contract round trip delivered.</summary>
    public const string Delivered = "delivered";

    /// <summary>A survey or a trade took the ship off its spare-time trip (D34, D37).</summary>
    public const string Interrupted = "interrupted";

    /// <summary>The circuit breaker blocked the trip's goal.</summary>
    public const string Runaway = "runaway";

    /// <summary>The extraction or siphon command rejected the trip's gathering.</summary>
    public const string Rejected = "rejected";

    /// <summary>None of what the trip sells was aboard.</summary>
    public const string NothingAboard = "nothing_aboard";

    /// <summary>No market buys what the trip's source yields any more.</summary>
    public const string NoBuyer = "no_buyer";

    /// <summary>The trip's sell market doesn't buy its cargo any more.</summary>
    public const string NotBoughtHere = "not_bought_here";

    /// <inheritdoc />
    public Task BookAsync(string shipSymbol, TripGoal trip, string reason, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(trip);
        return BookTripAsync(shipSymbol, ActivityOf(trip), trip.StartedAt, trip.Earned, trip.Spent, reason, cancellationToken);
    }

    /// <inheritdoc />
    public Task BookContractTripAsync(string shipSymbol, DateTimeOffset assignedAt, CancellationToken cancellationToken)
        => BookTripAsync(shipSymbol, Contract, assignedAt, earned: 0, spent: 0, Delivered, cancellationToken);

    private static string ActivityOf(TripGoal trip) => trip switch
    {
        TradeBetweenMarketsGoal => Trade,
        MineAndSellGoal => Mining,
        SiphonAndSellGoal => Siphoning,
        GatherAndSellGoal => SpareTime,
        _ => throw new ArgumentOutOfRangeException(nameof(trip), trip.Kind, "A trip the book has no activity for."),
    };

    private async Task BookTripAsync(
        string shipSymbol,
        string activity,
        DateTimeOffset startedAt,
        long earned,
        long spent,
        string reason,
        CancellationToken cancellationToken)
    {
        var now = TimeProvider.System.GetUtcNow();
        var fuelPurchases = await ledger.GetRangeAsync(
            from: startedAt,
            to: now,
            shipSymbol: shipSymbol,
            category: LedgerCategory.FuelPurchase,
            cancellationToken: cancellationToken);

        // A purchase is a negative amount in the ledger.
        var fuel = -fuelPurchases.Sum(entry => entry.Amount);
        var profit = earned - spent - fuel;

        metrics.TripEnded(activity);
        metrics.TripProfit(activity, profit);
        logger.LogInformation(
            "{EventKind:l}: ship {ShipSymbol} made {Profit} credits on its {Activity} trip in {Minutes} minutes: sold for {Earned}, bought for {Spent}, fuel {FuelCost} ({Reason}).",
            JournalEvents.TripEnded,
            shipSymbol,
            profit,
            activity,
            (int)(now - startedAt).TotalMinutes,
            earned,
            spent,
            fuel,
            reason);
    }
}
