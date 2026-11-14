namespace SpaceTraders.Application.Trading;

/// <summary>
/// The latest <see cref="TradeList"/> (PLAN.md slice 6.63, D148): the background worker writes it (<see cref="TradeListService"/>),
/// the trade context reader hands it to the maps it reads (<see cref="TradeContextReader"/>).
/// </summary>
/// <remarks>
/// A singleton, in memory: the bot runs as one pod, and the worker builds a new list within minutes of a restart. Until its
/// first round the plans that rank routes wait (<see cref="IsReady"/>, B91).
/// </remarks>
public sealed class TradeListStore
{
    private TradeList _latest = TradeList.None;
    private int _ready;

    /// <summary>The latest list; <see cref="TradeList.None"/> before the first, and while the home gate isn't built.</summary>
    public TradeList Latest => Volatile.Read(ref _latest);

    /// <summary>
    /// Whether the worker has finished a round since the start (slice 6.66, B91): kept a list, or none while the home gate isn't
    /// built. Until then the role board and the trading plan wait (<see cref="Automation.GameLoopService"/>): with no list they
    /// work out every route, which after a restart on 2026-11-14 (3,100 markets, 482 ships) took more memory than the pod had.
    /// </summary>
    public bool IsReady => Volatile.Read(ref _ready) == 1;

    /// <summary>Keeps a new list, or <see cref="TradeList.None"/> to have every route worked out again.</summary>
    /// <param name="list">The list.</param>
    public void Set(TradeList list)
    {
        ArgumentNullException.ThrowIfNull(list);

        Volatile.Write(ref _latest, list);
        Volatile.Write(ref _ready, 1);
    }

    /// <summary>
    /// The latest list while it is recent enough to decide by; otherwise <see cref="TradeList.None"/>, and every route is worked
    /// out as before the list.
    /// </summary>
    /// <param name="now">The time to judge by.</param>
    /// <param name="maxAge">How old its markets may be read.</param>
    /// <returns>The list, or none.</returns>
    public TradeList FreshAt(DateTimeOffset now, TimeSpan maxAge)
    {
        var latest = Latest;
        return latest.IsNone || now - latest.BuiltAt > maxAge ? TradeList.None : latest;
    }
}
