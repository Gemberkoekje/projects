using System.Globalization;

namespace Klaverjas.application.Commands;

/// <summary>
/// Checks and stores an evening (D1). Unusual numbers are only flagged on the screen, never blocked
/// here; what is refused is what cannot be a score: an empty total, letters, or impossible values.
/// </summary>
public sealed class UitslagOpslaanHandler(
    IAvondStore avonden,
    ISpelerStore spelers,
    IInstellingenStore instellingen,
    ILogger<UitslagOpslaanHandler> logger)
{
    private readonly IAvondStore avonden = avonden ?? throw new ArgumentNullException(nameof(avonden));
    private readonly ISpelerStore spelers = spelers ?? throw new ArgumentNullException(nameof(spelers));
    private readonly IInstellingenStore instellingen = instellingen ?? throw new ArgumentNullException(nameof(instellingen));
    private readonly ILogger<UitslagOpslaanHandler> logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>Handles the command.</summary>
    /// <param name="command">The evening as typed.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>OK, or what still needs doing, naming the players.</returns>
    public Task<Result> HandleAsync(UitslagOpslaan command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        return OpslaanAsync(command, cancellationToken);
    }

    private async Task<Result> OpslaanAsync(UitslagOpslaan command, CancellationToken cancellationToken)
    {
        var instelling = await instellingen.LadenAsync(cancellationToken);
        if (instelling.IsGeenSpeelavond(command.Datum))
        {
            return Result.WithMessages(ValidationMessage.Error("Op die avond wordt niet geklaverjast (zie Instellingen)."));
        }

        var perId = (await spelers.AllesAsync(cancellationToken)).ToDictionary(s => s.Id);
        var gecontroleerd = Controleer(command, perId);

        if (!gecontroleerd.IsValid)
        {
            return Result.WithMessages(gecontroleerd.Messages);
        }

        await avonden.OpslaanAsync(Avond.Van(command.Datum, gecontroleerd.Value), cancellationToken);

        logger.LogInformation("Uitslag van {Datum} opgeslagen met {Aantal} spelers.", command.Datum, gecontroleerd.Value.Count);
        return Result.OK;
    }

    /// <summary>Turns the typed rows into results, or lists every player whose row is not right yet.</summary>
    /// <param name="command">The evening as typed.</param>
    /// <param name="perId">Every player by identity.</param>
    /// <returns>The results, or the messages.</returns>
    internal static Result<List<Uitslag>> Controleer(UitslagOpslaan command, IReadOnlyDictionary<Guid, Speler> perId)
    {
        var uitslagen = new List<Uitslag>();
        var zonderPunten = new List<string>();
        var geenGetal = new List<string>();
        var fouten = new List<IValidationMessage>();

        foreach (var regel in command.Regels)
        {
            var (uitslag, probleem, tekst) = Beoordeel(regel, perId);

            switch (probleem)
            {
                case Probleem.None: uitslagen.Add(uitslag); break;
                case Probleem.ZonderPunten: zonderPunten.Add(tekst); break;
                case Probleem.GeenGetal: geenGetal.Add(tekst); break;
                default: fouten.Add(ValidationMessage.Error(tekst)); break;
            }
        }

        if (zonderPunten.Count > 0)
        {
            fouten.Add(ValidationMessage.Error($"Nog geen punten bij: {string.Join(", ", zonderPunten)}. Vul ze in, of zet de status op Afwezig als iemand er niet was."));
        }

        if (geenGetal.Count > 0)
        {
            fouten.Add(ValidationMessage.Error($"Alleen cijfers, graag, bij: {string.Join(", ", geenGetal)}."));
        }

        if (uitslagen.GroupBy(u => u.SpelerId).Any(g => g.Count() > 1))
        {
            fouten.Add(ValidationMessage.Error("Er staat een speler twee keer op het scherm. Open de avond opnieuw."));
        }

        if (fouten.Count == 0 && uitslagen.Count == 0)
        {
            fouten.Add(ValidationMessage.Error("Er staat niemand op deze avond."));
        }

        return fouten.Count == 0 ? Result.For(uitslagen) : Result.WithMessages<List<Uitslag>>(fouten);
    }

    /// <summary>Checks one row.</summary>
    /// <param name="regel">The row as typed.</param>
    /// <param name="perId">Every player by identity.</param>
    /// <returns>The result, or what is wrong with it: a player's name, or a whole message for <see cref="Probleem.Fout"/>.</returns>
    private static (Uitslag Uitslag, Probleem Probleem, string Tekst) Beoordeel(UitslagInvoer regel, IReadOnlyDictionary<Guid, Speler> perId)
    {
        var leeg = new Uitslag();

        if (!perId.TryGetValue(regel.SpelerId, out var speler))
        {
            return (leeg, Probleem.Fout, "Er staat een speler op het scherm die niet meer bestaat. Open de avond opnieuw.");
        }

        if (regel.Status == Status.Afwezig)
        {
            return speler.IsLid
                ? (new Uitslag { SpelerId = speler.Id, Status = Status.Afwezig }, Probleem.None, string.Empty)
                : (leeg, Probleem.Fout, $"{speler.Naam} is invaller en kan niet afwezig zijn. Haal de invaller weg.");
        }

        var punten = (regel.Punten ?? string.Empty).Trim();
        var pitten = (regel.Pitten ?? string.Empty).Trim();

        if (punten.Length == 0)
        {
            return (leeg, Probleem.ZonderPunten, speler.Naam);
        }

        if (!Getal(punten, Uitslag.MaxPunten, out var p) || !Getal(pitten.Length == 0 ? "0" : pitten, Uitslag.MaxPitten, out var pt))
        {
            return (leeg, Probleem.GeenGetal, speler.Naam);
        }

        var slaper = regel.Status == Status.Slaper;
        var uitslag = new Uitslag
        {
            SpelerId = speler.Id,
            Status = slaper ? Status.Slaper : Status.Speelt,
            Punten = p,
            Pitten = pt,
            Ronde = slaper ? Math.Clamp(regel.Ronde, 1, 3) : 0,
        };

        return (uitslag, Probleem.None, string.Empty);
    }

    private static bool Getal(string tekst, int max, out int waarde)
        => int.TryParse(tekst, NumberStyles.None, CultureInfo.InvariantCulture, out waarde) && waarde <= max;

    /// <summary>What is wrong with a row.</summary>
    private enum Probleem
    {
        /// <summary>Nothing: the row is a result.</summary>
        None = 0,

        /// <summary>The card total is empty.</summary>
        ZonderPunten = 1,

        /// <summary>A number is not a number, or impossibly large.</summary>
        GeenGetal = 2,

        /// <summary>Something else, with its own message.</summary>
        Fout = 3,
    }
}
