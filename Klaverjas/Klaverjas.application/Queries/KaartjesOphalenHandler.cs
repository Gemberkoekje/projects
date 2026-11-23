using Klaverjas.core.Tafels;

namespace Klaverjas.application.Queries;

/// <summary>
/// The cards as Dad's own card sheet has them: the first name (the full name when two active members
/// share it, Q16), the date, and for a slaper the number top right plus 1600 and "Slaper n" in the
/// round sat out. Then the blank cards.
/// </summary>
public sealed class KaartjesOphalenHandler(VoorbereidingOphalenHandler voorbereiding, IInstellingenStore instellingen)
{
    private readonly VoorbereidingOphalenHandler voorbereiding = voorbereiding ?? throw new ArgumentNullException(nameof(voorbereiding));
    private readonly IInstellingenStore instellingen = instellingen ?? throw new ArgumentNullException(nameof(instellingen));

    /// <summary>Handles the query.</summary>
    /// <param name="query">The evening.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The cards.</returns>
    public Task<Result<Kaartjes>> HandleAsync(KaartjesOphalen query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        return OphalenAsync(query.Datum, cancellationToken);
    }

    /// <summary>The name on a card: the first name, unless another active member has the same one.</summary>
    /// <param name="speler">The member.</param>
    /// <param name="actief">Every active member.</param>
    /// <returns>The first name, or the full name.</returns>
    internal static string NaamOpKaartje(Speler speler, IReadOnlyCollection<Speler> actief)
    {
        var voornaam = Voornaam(speler.Naam);
        return actief.Any(o => o.Id != speler.Id && string.Equals(Voornaam(o.Naam), voornaam, StringComparison.OrdinalIgnoreCase)) ? speler.Naam : voornaam;
    }

    private static string Voornaam(string naam) => naam.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;

    private async Task<Result<Kaartjes>> OphalenAsync(DateOnly datum, CancellationToken cancellationToken)
    {
        var formulier = (await voorbereiding.HandleAsync(new VoorbereidingOphalen { Datum = datum }, cancellationToken)).Value;
        var v = formulier.Voorbereiding;
        var actief = formulier.Leden.Select(l => l.Speler).ToList();
        var metKaartje = actief.Where(s => v.Kaartjes.Contains(s.Id)).ToList();

        var lijst = metKaartje
            .Select(s =>
            {
                var nummer = v.SlaperNummer(s.Id);
                return new Kaartje { Naam = NaamOpKaartje(s, actief), SlaperNummer = nummer, RondeStil = nummer == 0 ? 0 : formulier.Schema.RondeStil(nummer) };
            })
            .Concat(Enumerable.Range(0, v.Blanco).Select(_ => new Kaartje()))
            .ToList();

        return Result.For(new Kaartjes
        {
            Datum = datum,
            Club = (await instellingen.LadenAsync(cancellationToken)).Club,
            Lijst = lijst,
            OpenSlapers = v.Slapers.Count(id => id == Guid.Empty),
        });
    }
}
