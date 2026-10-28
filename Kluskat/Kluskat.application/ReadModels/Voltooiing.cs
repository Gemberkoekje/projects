namespace Kluskat.application.ReadModels;

/// <summary>
/// One klus ticked off on the klus screen. A completion entered afterwards on the beheer page is not
/// logged here: it moves the klus, but it was not "played".
/// </summary>
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

    /// <summary>How big it was, at the time; phase 2 turns this into XP.</summary>
    public Grootte Grootte { get; init; }

    /// <summary>When it was done (UTC).</summary>
    public DateTime Op { get; init; }

    /// <summary>Logs a completion of a klus.</summary>
    /// <param name="klus">The klus, just ticked off.</param>
    /// <param name="op">When (UTC).</param>
    /// <returns>The document to store.</returns>
    public static Voltooiing Van(Klus klus, DateTime op)
    {
        ArgumentNullException.ThrowIfNull(klus);

        return new Voltooiing
        {
            Id = Guid.NewGuid(),
            KlusId = (Guid)(Uuid)klus.Id,
            Ruimte = klus.Ruimte,
            Naam = klus.Naam,
            Grootte = klus.Grootte,
            Op = op,
        };
    }
}
