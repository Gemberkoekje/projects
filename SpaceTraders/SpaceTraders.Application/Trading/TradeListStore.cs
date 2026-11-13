namespace SpaceTraders.Application.Trading;

/// <summary>
/// The latest <see cref="TradeList"/> (PLAN.md slice 6.63, D148): the background worker writes it (<see cref="TradeListService"/>),
/// the trade context reader hands it to the maps it reads (<see cref="TradeContextReader"/>).
/// </summary>
/// <remarks>A singleton, in memory: the bot runs as one pod, and the worker builds a new list within minutes of a restart.</remarks>
public sealed class TradeListStore
{
    private TradeList _latest = TradeList.None;

    /// <summary>The latest list; <see cref="TradeList.None"/> before the first, and while the home gate isn't built.</summary>
    public TradeList Latest => Volatile.Read(ref _latest);

    /// <summary>Keeps a new list, or <see cref="TradeList.None"/> to have every route worked out again.</summary>
    /// <param name="list">The list.</param>
    public void Set(TradeList list)
    {
        ArgumentNullException.ThrowIfNull(list);

        Volatile.Write(ref _latest, list);
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
