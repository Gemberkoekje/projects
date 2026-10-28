namespace Kluskat.test.TestHelpers;

internal static class Fixtures
{
    /// <summary>Wednesday 28 October 2026, 10:00 in Amsterdam (CET, UTC+1).</summary>
    public static readonly DateTime Nu = new DateTime(2026, 10, 28, 09, 00, 00, DateTimeKind.Utc);

    public static Klus.Data Data(string naam = "Stofzuigen", int dagen = 7, Grootte grootte = Grootte.Klein, string ruimte = "Keuken")
        => new Klus.Data { Ruimte = ruimte, Naam = naam, IntervalDagen = dagen, Grootte = grootte };

    /// <summary>A read model with a chosen last-done moment, for picker specs.</summary>
    public static KlusOverzicht Overzicht(string naam, int dagen, double dagenGeleden, Grootte grootte = Grootte.Klein)
        => new KlusOverzicht
        {
            Id = Guid.NewGuid(),
            Ruimte = "Keuken",
            Naam = naam,
            IntervalDagen = dagen,
            Grootte = grootte,
            LaatstGedaan = Nu.AddDays(-dagenGeleden),
        };

    /// <summary>A completion at a moment, for game-rule specs.</summary>
    public static Voltooiing Voltooid(
        DateTime op,
        Grootte grootte = Grootte.Klein,
        string ruimte = "Keuken",
        bool dapper = false,
        int keerOvergeslagen = 0,
        int intervalDagen = 7)
        => new Voltooiing
        {
            Id = Guid.NewGuid(),
            KlusId = Guid.NewGuid(),
            Ruimte = ruimte,
            Naam = "Iets",
            Grootte = grootte,
            Op = op,
            Dapper = dapper,
            KeerOvergeslagen = keerOvergeslagen,
            IntervalDagen = intervalDagen,
        };

    /// <summary>A number of completions in the week of a Monday, at noon on different days.</summary>
    public static IEnumerable<Voltooiing> Week(DateOnly maandag, int aantal)
        => Enumerable.Range(0, aantal).Select(i => Voltooid(Kalender.Middag(maandag.AddDays(i % 7))));

    public static Klus NieuweKlus(Klus.Data data, DateTime laatstGedaan)
    {
        var result = core.Models.Klus.Toevoegen(data, laatstGedaan);
        result.IsValid.Should().BeTrue();
        return result.Value;
    }
}
