namespace Klaverjas.application.Commands;

/// <summary>Stores a changed player. A corrected name shows everywhere at once, old weeks too (R9).</summary>
public sealed class SpelerAanpassenHandler(ISpelerStore spelers, ILogger<SpelerAanpassenHandler> logger)
{
    private readonly ISpelerStore spelers = spelers ?? throw new ArgumentNullException(nameof(spelers));
    private readonly ILogger<SpelerAanpassenHandler> logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>Handles the command.</summary>
    /// <param name="command">The changes.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>OK, or what is wrong.</returns>
    public Task<Result> HandleAsync(SpelerAanpassen command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        return AanpassenAsync(command, cancellationToken);
    }

    private async Task<Result> AanpassenAsync(SpelerAanpassen command, CancellationToken cancellationToken)
    {
        var geladen = await spelers.LadenAsync(command.Id, cancellationToken);
        if (!geladen.IsValid)
        {
            logger.LogWarning("Speler {SpelerId} niet gevonden.", command.Id);
            return Result.WithMessages(geladen.Messages);
        }

        var oud = geladen.Value;

        // Stopping takes a member out of the stand by default (R18). Keeping a stopped member in is
        // a separate, later choice, so a "Gestopt" that arrives with the stand unchanged means "out".
        var stopt = oud.DoetMee && !command.DoetMee && command.InStand == oud.InStand;

        var nieuw = oud with
        {
            Naam = (command.Naam ?? string.Empty).Trim(),
            Soort = command.Soort,
            Email = (command.Email ?? string.Empty).Trim(),
            Telefoon = (command.Telefoon ?? string.Empty).Trim(),
            Adres = (command.Adres ?? string.Empty).Trim(),
            Verjaardag = (command.Verjaardag ?? string.Empty).Trim(),
            DoetMee = command.DoetMee,
            InStand = !stopt && command.InStand,
        };

        var controle = Speler.Controleer(nieuw);
        if (!controle.IsValid)
        {
            return controle;
        }

        var anderen = await spelers.AllesAsync(cancellationToken);
        if (anderen.Any(s => s.Id != nieuw.Id && string.Equals(s.Naam, nieuw.Naam, StringComparison.OrdinalIgnoreCase)))
        {
            return Result.WithMessages(ValidationMessage.Error($"Er staat al een andere speler met de naam {nieuw.Naam} in de lijst."));
        }

        await spelers.OpslaanAsync(nieuw, cancellationToken);

        logger.LogInformation("Speler {SpelerId} aangepast; doet mee: {DoetMee}, in de stand: {InStand}.", nieuw.Id, nieuw.DoetMee, nieuw.InStand);
        return Result.OK;
    }
}
