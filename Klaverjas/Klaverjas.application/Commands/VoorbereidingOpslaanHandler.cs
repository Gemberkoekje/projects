using Klaverjas.core.Tafels;

namespace Klaverjas.application.Commands;

/// <summary>
/// Stores an evening's preparation. The slapers are kept to members who get a card, cut or padded to
/// the number the expected players need, and filled fairly when Dad asks for a proposal.
/// </summary>
public sealed class VoorbereidingOpslaanHandler(
    IVoorbereidingStore voorbereidingen,
    ISpelerStore spelers,
    IAvondStore avonden,
    ILogger<VoorbereidingOpslaanHandler> logger)
{
    private readonly IVoorbereidingStore voorbereidingen = voorbereidingen ?? throw new ArgumentNullException(nameof(voorbereidingen));
    private readonly ISpelerStore spelers = spelers ?? throw new ArgumentNullException(nameof(spelers));
    private readonly IAvondStore avonden = avonden ?? throw new ArgumentNullException(nameof(avonden));
    private readonly ILogger<VoorbereidingOpslaanHandler> logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>Handles the command.</summary>
    /// <param name="command">The screen as posted.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>OK, or why not.</returns>
    public Task<Result> HandleAsync(VoorbereidingOpslaan command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        return OpslaanAsync(command, cancellationToken);
    }

    private async Task<Result> OpslaanAsync(VoorbereidingOpslaan command, CancellationToken cancellationToken)
    {
        if (!Tafelschemas.KanMet(command.Verwacht))
        {
            return Result.WithMessages(ValidationMessage.Error(
                $"Voor {command.Verwacht} spelers is er geen tafelindeling. Kies een aantal van {Tafelschemas.Minimum} tot en met {Tafelschemas.Maximum} (behalve 7)."));
        }

        var actief = (await spelers.AllesAsync(cancellationToken)).Where(s => s.IsLid && s.DoetMee).ToDictionary(s => s.Id);
        var kaartjes = command.Kaartjes.Distinct().Where(actief.ContainsKey).ToList();

        // The expected number follows the ticked cards until Dad sets it himself (as in the mock).
        var vorige = await voorbereidingen.LadenAsync(command.Datum, cancellationToken);
        var vorigVerwacht = vorige.IsValid ? vorige.Value.Verwacht : Tafelschemas.Dichtstbij(actief.Count);
        var zelfGekozen = command.Verwacht != vorigVerwacht || (vorige.IsValid && vorige.Value.VerwachtZelfGekozen);
        var verwacht = zelfGekozen ? command.Verwacht : Tafelschemas.Dichtstbij(kaartjes.Count);
        var schema = Tafelschemas.Voor(verwacht);

        // A slaper must be someone with a card.
        var gekozen = SlaperKeuze.Passend([.. command.Slapers.Select(id => kaartjes.Contains(id) ? id : Guid.Empty)], schema.Slapers);

        if (command.Voorstellen)
        {
            var geschiedenis = SlaperKeuze.Tel(await avonden.AllesAsync(cancellationToken), command.Datum.Year);
            gekozen = SlaperKeuze.EerlijkVoorstel(gekozen, schema.Slapers, kaartjes.Select(id => actief[id]), geschiedenis);
        }

        await voorbereidingen.OpslaanAsync(
            new Voorbereiding
            {
                Id = Voorbereiding.IdVan(command.Datum),
                Datum = command.Datum,
                Kaartjes = kaartjes,
                Blanco = Math.Clamp(command.Blanco, 0, Voorbereiding.MaxBlanco),
                Verwacht = verwacht,
                VerwachtZelfGekozen = zelfGekozen,
                Slapers = gekozen,
            },
            cancellationToken);

        logger.LogInformation("Voorbereiding van {Datum}: {Kaartjes} kaartjes, {Verwacht} spelers verwacht.", command.Datum, kaartjes.Count, verwacht);
        return Result.OK;
    }
}
