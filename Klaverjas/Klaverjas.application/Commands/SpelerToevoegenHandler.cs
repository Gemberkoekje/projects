namespace Klaverjas.application.Commands;

/// <summary>Stores a new player. A new member joins today: earlier evenings count as absent (R17).</summary>
public sealed class SpelerToevoegenHandler(ISpelerStore spelers, IAvondStore avonden, ILogger<SpelerToevoegenHandler> logger)
{
    private readonly ISpelerStore spelers = spelers ?? throw new ArgumentNullException(nameof(spelers));
    private readonly IAvondStore avonden = avonden ?? throw new ArgumentNullException(nameof(avonden));
    private readonly ILogger<SpelerToevoegenHandler> logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>Handles the command.</summary>
    /// <param name="command">The new player.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The player and the evenings that count as absent, or what is wrong.</returns>
    public Task<Result<SpelerToegevoegd>> HandleAsync(SpelerToevoegen command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        return ToevoegenAsync(command, cancellationToken);
    }

    private async Task<Result<SpelerToegevoegd>> ToevoegenAsync(SpelerToevoegen command, CancellationToken cancellationToken)
    {
        var vandaag = Kalender.Vandaag();
        var speler = new Speler
        {
            Id = Guid.NewGuid(),
            Naam = (command.Naam ?? string.Empty).Trim(),
            Soort = command.Soort,
            Email = (command.Email ?? string.Empty).Trim(),
            LidVanaf = command.Soort == Soort.Lid ? vandaag : DateOnly.MinValue,
        };

        var controle = Speler.Controleer(speler);
        if (!controle.IsValid)
        {
            return Result.WithMessages<SpelerToegevoegd>(controle.Messages);
        }

        var bestaand = await spelers.AllesAsync(cancellationToken);
        if (bestaand.Any(s => string.Equals(s.Naam, speler.Naam, StringComparison.OrdinalIgnoreCase)))
        {
            return Result.WithMessages<SpelerToegevoegd>(ValidationMessage.Error($"Er staat al een speler met de naam {speler.Naam} in de lijst."));
        }

        await spelers.OpslaanAsync(speler, cancellationToken);

        var gemist = speler.IsLid
            ? (await avonden.AllesAsync(cancellationToken)).Count(a => a.Datum.Year == vandaag.Year && a.Datum < vandaag)
            : 0;

        logger.LogInformation("Speler {SpelerId} toegevoegd als {Soort}.", speler.Id, speler.Soort);
        return Result.For(new SpelerToegevoegd { Speler = speler, GemisteAvonden = gemist });
    }
}
