namespace Kluskat.application.ReadModels;

/// <summary>
/// One klus ticked off on the klus screen. A completion entered afterwards on the beheer page is not
/// logged here: it moves the klus, but it was not "played".
/// </summary>
/// <remarks>
/// A snapshot of the facts the game rules need, taken at the moment of completion, so XP, levels,
/// the streak and every achievement are a pure function of this log (see <c>Spelregels</c>).
/// </remarks>
public sealed record Voltooiing
{
    /// <summary>The document identity.</summary>
    public Guid Id { get; init; }

    /// <summary>The klus, as a plain Guid.</summary>
    public Guid KlusId { get; init; }

    /// <summary>Where in the house, at the time.</summary>
    public string Ruimte { get; init; } = string.Empty;

    /// <summary>What was done, at the time.</summary>
    public string Naam { get; init; } = string.Empty;

    /// <summary>How big it was, at the time; the game rules turn this into XP.</summary>
    public Grootte Grootte { get; init; }

    /// <summary>When it was done (UTC).</summary>
    public DateTime Op { get; init; }

    /// <summary>How many days the klus stays away, at the time; the "Jaarklus" badge reads it.</summary>
    public int IntervalDagen { get; init; }

    /// <summary>Whether the klus had been skipped in the <see cref="DapperVenster"/> before: the bravery bonus.</summary>
    public bool Dapper { get; init; }

    /// <summary>How often the klus had been skipped since it was last done.</summary>
    public int KeerOvergeslagen { get; init; }

    /// <summary>How recent a skip must be for finishing the klus to count as brave.</summary>
    public static TimeSpan DapperVenster { get; } = TimeSpan.FromDays(3);

    /// <summary>Logs a completion of a klus.</summary>
    /// <param name="voor">The klus as it was just <em>before</em> it was ticked off, so its skip history is intact.</param>
    /// <param name="op">When (UTC).</param>
    /// <returns>The document to store.</returns>
    public static Voltooiing Van(Klus voor, DateTime op)
    {
        ArgumentNullException.ThrowIfNull(voor);

        return new Voltooiing
        {
            Id = Guid.NewGuid(),
            KlusId = (Guid)(Uuid)voor.Id,
            Ruimte = voor.Ruimte,
            Naam = voor.Naam,
            Grootte = voor.Grootte,
            Op = op,
            IntervalDagen = voor.IntervalDagen,
            Dapper = voor.KeerOvergeslagen > 0 && op - voor.LaatstOvergeslagen <= DapperVenster,
            KeerOvergeslagen = voor.KeerOvergeslagen,
        };
    }
}
