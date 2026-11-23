namespace Klaverjas.test.TestHelpers;

/// <summary>Invented players and November 2026 evenings. Never real club data: the repository is public (D9).</summary>
internal static class Fixtures
{
    /// <summary>Monday 2 November 2026 (week 45).</summary>
    public static readonly DateOnly Wk45 = new DateOnly(2026, 11, 02);

    /// <summary>Monday 9 November 2026 (week 46).</summary>
    public static readonly DateOnly Wk46 = new DateOnly(2026, 11, 09);

    /// <summary>Monday 16 November 2026 (week 47).</summary>
    public static readonly DateOnly Wk47 = new DateOnly(2026, 11, 16);

    public static readonly Instellingen Standaard = new Instellingen();

    public static Speler Lid(string naam, DateOnly lidVanaf = default)
        => new Speler { Id = Guid.NewGuid(), Naam = naam, Soort = Soort.Lid, LidVanaf = lidVanaf };

    public static Speler Invaller(string naam)
        => new Speler { Id = Guid.NewGuid(), Naam = naam, Soort = Soort.Invaller };

    public static Uitslag Speelt(Speler speler, int punten, int pitten = 0)
        => new Uitslag { SpelerId = speler.Id, Status = Status.Speelt, Punten = punten, Pitten = pitten };

    public static Uitslag Slaper(Speler speler, int punten, int ronde, int pitten = 0)
        => new Uitslag { SpelerId = speler.Id, Status = Status.Slaper, Punten = punten, Pitten = pitten, Ronde = ronde };

    public static Uitslag Afwezig(Speler speler)
        => new Uitslag { SpelerId = speler.Id, Status = Status.Afwezig };

    public static Avond Avond(DateOnly datum, params Uitslag[] uitslagen) => core.Models.Avond.Van(datum, uitslagen);

    public static Stand Bereken(IEnumerable<Speler> spelers, params Avond[] avonden)
        => StandBerekening.Bereken(spelers, avonden, Standaard, 2026);

    public static StandRegel RegelVan(this Stand stand, Speler speler)
        => stand.Punten.Concat(stand.Invallers).Single(r => r.Speler.Id == speler.Id);
}
