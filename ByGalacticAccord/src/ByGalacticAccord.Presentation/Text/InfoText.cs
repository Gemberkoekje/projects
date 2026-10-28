using System.Collections.Generic;
using System.Linq;
using ByGalacticAccord.Engine.Domain;
using ByGalacticAccord.Engine.Economy;
using ByGalacticAccord.Engine.Simulation;

namespace ByGalacticAccord.Presentation.Text;

/// <summary>
/// How old a piece of information is (design §7.2, §11.1: "information age is opacity"): live, from today, a few days old,
/// older. The client draws each a little fainter than the last; CSS classes <c>age-*</c>.
/// </summary>
public enum Age
{
    Live,
    Today,
    Days,
    Old,
}

/// <summary>Words and ages for what the player knows (design §7, §11.5): how a reading was learnt and when, and a track record.</summary>
public static class InfoText
{
    /// <summary>How old something learnt at <paramref name="tick"/> is at <paramref name="now"/>.</summary>
    public static Age AgeOf(long tick, long now, bool live = false)
    {
        if (live)
        {
            return Age.Live;
        }

        long hours = now - tick;
        return hours < GameTime.HoursPerDay ? Age.Today : hours < 3 * GameTime.HoursPerDay ? Age.Days : Age.Old;
    }

    public static Age AgeOf(PriceReading reading, long now) => AgeOf(reading.AskTick, now, reading.IsLive);

    /// <summary>How the player knows an ask: "live", "daily bulletin, Day 4 · 06:00", "as of Day 2 · 14:00", "from the news, …".</summary>
    public static string ReadingAge(PriceReading reading) => reading.Source == ReadingSource.Live
        ? Templates.Get("Age.Live")
        : Templates.Fill("Age." + reading.Source, ("when", Format.Date(reading.AskTick)));

    /// <summary>
    /// An actor's public record (design §7.3), as anyone can look it up: how its Accord contracts ended, as the side that performs
    /// and as the side that pays. Every count is the engine's own.
    /// </summary>
    public static string PublicRecord(TrackRecord record) =>
        record.Total == 0 ? Templates.Get("Record.Public.None") : Templates.Fill("Record.Public", ("parts", Parts(record)));

    /// <summary>What another actor did on contracts with the player, frontier ones too (design §7.3: each party's own experience).</summary>
    public static string Dealings(TrackRecord record, string name) =>
        record.Total == 0
            ? Templates.Fill("Record.Yours.None", ("name", name))
            : Templates.Fill("Record.Yours", ("name", name), ("parts", Parts(record)));

    private static string Parts(TrackRecord r)
    {
        var parts = new List<string>();
        int delivered = r.OnTime + r.Late;
        if (delivered > 0)
        {
            parts.Add(Templates.Fill("Record.Delivered", ("onTime", Count(r.OnTime)), ("delivered", Count(delivered))));
        }

        Add(parts, "Record.Short", r.Short);
        Add(parts, "Record.Failed", r.Failed);
        Add(parts, "Record.CancelledForDefault", r.CancelledForDefault);
        Add(parts, "Record.WalkedAway", r.WalkedAway);
        Add(parts, "Record.Paid", r.Paid);
        Add(parts, "Record.Defaulted", r.Defaulted);
        Add(parts, "Record.PayerWalkedAway", r.PayerWalkedAway);
        return string.Join(Templates.Get("Record.Separator"), parts);
    }

    private static void Add(List<string> parts, string key, int count)
    {
        if (count > 0)
        {
            parts.Add(Templates.Fill(key, ("n", Count(count))));
        }
    }

    private static string Count(int n) => n.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The lowest and highest asks among a reading history, and when it starts; null if it is empty.</summary>
    public static (Credits Low, Credits High, long Since)? Range(IReadOnlyList<(long Tick, Credits Ask)> history) =>
        history.Count == 0 ? null : (history.Min(h => h.Ask), history.Max(h => h.Ask), history[0].Tick);
}
