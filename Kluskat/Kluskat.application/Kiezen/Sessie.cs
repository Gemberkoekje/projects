namespace Kluskat.application.Kiezen;

/// <summary>
/// The skip window: which klussen were skipped lately, and whether "Geen tijd" capped the size. It
/// wears off <see cref="Levensduur"/> after the last tap, so this morning's skip is back tonight.
/// </summary>
/// <remarks>
/// There is one user, so there is one sessie, stored as a single document. It is deliberately not
/// tied to a browser: a skip on the phone also holds on the laptop.
/// </remarks>
public sealed record Sessie
{
    /// <summary>The identity of the one sessie document.</summary>
    public const string HuidigeId = "huidig";

    /// <summary>How long skips last after the last tap.</summary>
    public static TimeSpan Levensduur { get; } = TimeSpan.FromHours(3);

    /// <summary>The document identity; always <see cref="HuidigeId"/>.</summary>
    public string Id { get; init; } = HuidigeId;

    /// <summary>The last tap on Gedaan, Geen zin or Geen tijd (UTC).</summary>
    public DateTime LaatsteActiviteit { get; init; }

    /// <summary>The klussen skipped in this window, oldest first.</summary>
    public IReadOnlyList<Guid> Overgeslagen { get; init; } = [];

    /// <summary>The largest size still offered; <see cref="Grootte.None"/> means no cap.</summary>
    public Grootte MaxGrootte { get; init; }

    /// <summary>The sessie as it applies now: a fresh one when the window has worn off.</summary>
    /// <param name="nu">The current moment (UTC).</param>
    /// <returns>This sessie, or an empty one.</returns>
    public Sessie Actueel(DateTime nu) => nu - LaatsteActiviteit > Levensduur ? new Sessie() : this;

    /// <summary>Whether a klus was skipped in this window.</summary>
    /// <param name="klusId">The klus.</param>
    /// <returns><see langword="true"/> when it was.</returns>
    public bool IsOvergeslagen(Guid klusId) => Overgeslagen.Contains(klusId);

    /// <summary>Whether a size fits under the "Geen tijd" cap.</summary>
    /// <param name="grootte">The size of a klus.</param>
    /// <returns><see langword="true"/> when there is no cap or the klus is small enough.</returns>
    public bool Past(Grootte grootte) => MaxGrootte == Grootte.None || grootte <= MaxGrootte;

    /// <summary>Records a skip.</summary>
    /// <param name="klus">The klus that was on screen.</param>
    /// <param name="reden">Which button.</param>
    /// <param name="nu">The current moment (UTC).</param>
    /// <returns>The updated sessie.</returns>
    public Sessie NaOverslaan(KlusOverzicht klus, OverslaanReden reden, DateTime nu)
    {
        ArgumentNullException.ThrowIfNull(klus);

        // "Geen tijd" on a klein klus cannot ask for anything smaller, so it settles for another
        // klein one. The cap only ever tightens within a window.
        var max = reden == OverslaanReden.GeenTijd
            ? Kleinste(MaxGrootte, Kleiner(klus.Grootte))
            : MaxGrootte;

        return this with
        {
            LaatsteActiviteit = nu,
            Overgeslagen = [.. Overgeslagen.Where(id => id != klus.Id), klus.Id],
            MaxGrootte = max,
        };
    }

    /// <summary>Records a "Gedaan". The cap stays: no time is still no time.</summary>
    /// <param name="klusId">The klus that was done.</param>
    /// <param name="nu">The current moment (UTC).</param>
    /// <returns>The updated sessie.</returns>
    public Sessie NaGedaan(Guid klusId, DateTime nu)
        => this with
        {
            LaatsteActiviteit = nu,
            Overgeslagen = [.. Overgeslagen.Where(id => id != klusId)],
        };

    private static Grootte Kleiner(Grootte grootte) => grootte <= Grootte.Klein ? Grootte.Klein : grootte - 1;

    private static Grootte Kleinste(Grootte huidig, Grootte nieuw)
        => huidig == Grootte.None || nieuw < huidig ? nieuw : huidig;
}
