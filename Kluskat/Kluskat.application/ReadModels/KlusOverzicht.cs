using System.Text.Json.Serialization;

namespace Kluskat.application.ReadModels;

/// <summary>
/// The read model of one <see cref="Klus"/>, stored as a document next to its stream after every
/// save. The picker reads all of these at once; it never replays a stream.
/// </summary>
public sealed record KlusOverzicht
{
    /// <summary>The placeholder for "no klus", so callers never handle a null.</summary>
    public static KlusOverzicht Geen { get; } = new KlusOverzicht();

    /// <summary>The <see cref="KlusId"/> as a plain Guid: the document identity.</summary>
    public Guid Id { get; init; }

    /// <summary>Where in the house.</summary>
    public string Ruimte { get; init; } = string.Empty;

    /// <summary>What to do.</summary>
    public string Naam { get; init; } = string.Empty;

    /// <summary>How many days the klus stays away after it is done.</summary>
    public int IntervalDagen { get; init; }

    /// <summary>Roughly how long it takes.</summary>
    public Grootte Grootte { get; init; }

    /// <summary>When it was last done (UTC).</summary>
    public DateTime LaatstGedaan { get; init; }

    /// <summary>Whether the latest "Gedaan" can still be taken back.</summary>
    public bool KanTerugdraaien { get; init; }

    /// <summary>Whether the klus left the list for good.</summary>
    public bool Geschrapt { get; init; }

    /// <summary>The typed id.</summary>
    [JsonIgnore]
    public KlusId KlusId => KlusId.Create((Uuid)Id);

    /// <summary>When the klus is due again (UTC).</summary>
    [JsonIgnore]
    public DateTime VolgendeKeer => LaatstGedaan.AddDays(IntervalDagen);

    /// <summary>Whether the klus is due.</summary>
    /// <param name="nu">The current moment (UTC).</param>
    /// <returns><see langword="true"/> from <see cref="VolgendeKeer"/> on.</returns>
    public bool IsAanDeBeurt(DateTime nu) => nu >= VolgendeKeer;

    /// <summary>
    /// How far behind schedule the klus is, relative to its own interval: 1.0 is exactly due, 2.0 a
    /// whole interval late. A weekly klus three days late outranks a yearly klus three days late.
    /// </summary>
    /// <param name="nu">The current moment (UTC).</param>
    /// <returns>The elapsed time since the last completion, in intervals.</returns>
    public double Achterstand(DateTime nu) => (nu - LaatstGedaan).TotalDays / IntervalDagen;

    /// <summary>Projects a klus onto its read model.</summary>
    /// <param name="klus">The klus.</param>
    /// <returns>The document to store.</returns>
    public static KlusOverzicht Van(Klus klus)
    {
        ArgumentNullException.ThrowIfNull(klus);

        return new KlusOverzicht
        {
            Id = (Guid)(Uuid)klus.Id,
            Ruimte = klus.Ruimte,
            Naam = klus.Naam,
            IntervalDagen = klus.IntervalDagen,
            Grootte = klus.Grootte,
            LaatstGedaan = klus.LaatstGedaan,
            KanTerugdraaien = klus.KanTerugdraaien,
            Geschrapt = klus.Geschrapt,
        };
    }
}
