namespace Klaverjas.core.Tafels;

/// <summary>How often a member sat out this season, and when last (replaces the `slapers` sheet).</summary>
public sealed record Slaapgeschiedenis
{
    /// <summary>The number of evenings sat out (a round each).</summary>
    public int Keer { get; init; }

    /// <summary>The last evening sat out; <see cref="DateOnly.MinValue"/> for never.</summary>
    public DateOnly Laatst { get; init; } = DateOnly.MinValue;
}

/// <summary>Sharing out the sitting out fairly (Q10, Dad's note): he picks, the app proposes.</summary>
public static class SlaperKeuze
{
    /// <summary>Counts the season's sitting out per member, from the evenings entered.</summary>
    /// <param name="avonden">Every evening.</param>
    /// <param name="jaar">The season.</param>
    /// <returns>Per member who sat out at least once, how often and when last.</returns>
    public static IReadOnlyDictionary<Guid, Slaapgeschiedenis> Tel(IEnumerable<Avond> avonden, int jaar)
    {
        ArgumentNullException.ThrowIfNull(avonden);

        return avonden
            .Where(a => a.Datum.Year == jaar)
            .SelectMany(a => a.Uitslagen.Where(u => u.Status == Status.Slaper).Select(u => (u.SpelerId, a.Datum)))
            .GroupBy(x => x.SpelerId)
            .ToDictionary(g => g.Key, g => new Slaapgeschiedenis { Keer = g.Count(), Laatst = g.Max(x => x.Datum) });
    }

    /// <summary>
    /// "Eerlijk voorstel": keeps every slaper already picked and fills the empty places with the
    /// members who sat out least, then longest ago, then by name.
    /// </summary>
    /// <param name="gekozen">The slapers picked so far, by number.</param>
    /// <param name="aantal">How many slapers the evening needs.</param>
    /// <param name="kandidaten">The members who get a card.</param>
    /// <param name="geschiedenis">How often and when last each sat out.</param>
    /// <returns>The slapers by number, <paramref name="aantal"/> long; <see cref="Guid.Empty"/> where nobody is left.</returns>
    public static IReadOnlyList<Guid> EerlijkVoorstel(
        IReadOnlyList<Guid> gekozen,
        int aantal,
        IEnumerable<Speler> kandidaten,
        IReadOnlyDictionary<Guid, Slaapgeschiedenis> geschiedenis)
    {
        ArgumentNullException.ThrowIfNull(gekozen);
        ArgumentNullException.ThrowIfNull(kandidaten);
        ArgumentNullException.ThrowIfNull(geschiedenis);

        var plaatsen = Passend(gekozen, aantal);
        var bezet = plaatsen.Where(id => id != Guid.Empty).ToHashSet();
        var leeg = new Slaapgeschiedenis();

        var vrij = new Queue<Guid>(kandidaten
            .Where(s => !bezet.Contains(s.Id))
            .OrderBy(s => (geschiedenis.TryGetValue(s.Id, out var g) ? g : leeg).Keer)
            .ThenBy(s => (geschiedenis.TryGetValue(s.Id, out var g) ? g : leeg).Laatst)
            .ThenBy(s => s.Naam, Standen.StandBerekening.NaamVolgorde)
            .Select(s => s.Id));

        return [.. plaatsen.Select(id => id != Guid.Empty || vrij.Count == 0 ? id : vrij.Dequeue())];
    }

    /// <summary>The picks cut or padded to the number needed; nobody twice.</summary>
    /// <param name="gekozen">The picks.</param>
    /// <param name="aantal">How many slapers are needed.</param>
    /// <returns>Exactly <paramref name="aantal"/> places.</returns>
    public static IReadOnlyList<Guid> Passend(IReadOnlyList<Guid> gekozen, int aantal)
    {
        ArgumentNullException.ThrowIfNull(gekozen);

        var gezien = new HashSet<Guid>();
        var uniek = gekozen.Select(id => id != Guid.Empty && gezien.Add(id) ? id : Guid.Empty);
        return [.. uniek.Concat(Enumerable.Repeat(Guid.Empty, Math.Max(0, aantal))).Take(Math.Max(0, aantal))];
    }
}
