using System.Globalization;

namespace Klaverjas.core.Models;

/// <summary>
/// Dad's preparation of one evening (D12): who gets a card, how many blank cards, the expected number
/// of players, and the slapers he picked. One document per date.
/// </summary>
public sealed record Voorbereiding
{
    /// <summary>The most blank cards that make sense: one A4 of them.</summary>
    public const int MaxBlanco = 12;

    /// <summary>The document identity: the date as <c>yyyy-MM-dd</c>.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>The evening.</summary>
    public DateOnly Datum { get; init; }

    /// <summary>The players who get a card with their name: members, and the substitutes Dad ticked.</summary>
    public IReadOnlyList<Guid> Kaartjes { get; init; } = [];

    /// <summary>
    /// The members Dad unticked. A member in neither list (new, or a member since this was saved) gets a
    /// card; a substitute only when ticked (Dad, 2026-11-26).
    /// </summary>
    public IReadOnlyList<Guid> ZonderKaartje { get; init; } = [];

    /// <summary>Blank cards for substitutes.</summary>
    public int Blanco { get; init; } = 2;

    /// <summary>The number of players Dad expects; it decides how many slapers there are.</summary>
    public int Verwacht { get; init; }

    /// <summary>Whether Dad set <see cref="Verwacht"/> himself; until then it follows the ticked cards.</summary>
    public bool VerwachtZelfGekozen { get; init; }

    /// <summary>The slapers by number: the first is "Slaper 1" (seat 1), and so on. <see cref="Guid.Empty"/> where none is picked yet.</summary>
    public IReadOnlyList<Guid> Slapers { get; init; } = [];

    /// <summary>The document identity of an evening's preparation.</summary>
    /// <param name="datum">The evening.</param>
    /// <returns>The date as <c>yyyy-MM-dd</c>.</returns>
    public static string IdVan(DateOnly datum) => datum.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>Whether a player gets a card with their name: as ticked or unticked, else members do and substitutes don't.</summary>
    /// <param name="speler">The player.</param>
    /// <returns><see langword="true"/> for a card.</returns>
    public bool HeeftKaartje(Speler speler)
    {
        ArgumentNullException.ThrowIfNull(speler);

        return Kaartjes.Contains(speler.Id) || (speler.IsLid && !ZonderKaartje.Contains(speler.Id));
    }

    /// <summary>A player's slaper number.</summary>
    /// <param name="spelerId">The player.</param>
    /// <returns>1 and up for a slaper (which is also their seat number), 0 otherwise.</returns>
    public int SlaperNummer(Guid spelerId)
        => spelerId == Guid.Empty ? 0 : Slapers.ToList().IndexOf(spelerId) + 1;
}
