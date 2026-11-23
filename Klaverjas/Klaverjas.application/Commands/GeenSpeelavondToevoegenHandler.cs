namespace Klaverjas.application.Commands;

/// <summary>Adds an evening without play to the settings.</summary>
public sealed class GeenSpeelavondToevoegenHandler(
    IInstellingenStore instellingen,
    IAvondStore avonden,
    ILogger<GeenSpeelavondToevoegenHandler> logger)
{
    private readonly IInstellingenStore instellingen = instellingen ?? throw new ArgumentNullException(nameof(instellingen));
    private readonly IAvondStore avonden = avonden ?? throw new ArgumentNullException(nameof(avonden));
    private readonly ILogger<GeenSpeelavondToevoegenHandler> logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>Handles the command.</summary>
    /// <param name="command">The date and the reason.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>OK, or why not.</returns>
    public Task<Result> HandleAsync(GeenSpeelavondToevoegen command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        return ToevoegenAsync(command, cancellationToken);
    }

    private async Task<Result> ToevoegenAsync(GeenSpeelavondToevoegen command, CancellationToken cancellationToken)
    {
        var oud = await instellingen.LadenAsync(cancellationToken);

        if (command.Datum.DayOfWeek != oud.Speeldag)
        {
            return Result.WithMessages(ValidationMessage.Error("Die datum valt niet op de speelavond."));
        }

        if (oud.IsGeenSpeelavond(command.Datum))
        {
            return Result.WithMessages(ValidationMessage.Error("Die avond staat er al bij."));
        }

        if ((await avonden.LadenAsync(command.Datum, cancellationToken)).IsValid)
        {
            return Result.WithMessages(ValidationMessage.Error("Op die avond is al gespeeld: de uitslag staat erin."));
        }

        var reden = (command.Reden ?? string.Empty).Trim();
        var nieuw = oud with
        {
            GeenSpeelavonden = [.. oud.GeenSpeelavonden
                .Append(new GeenSpeelavond { Datum = command.Datum, Reden = reden.Length == 0 ? "Geen klaverjassen" : reden })
                .OrderBy(g => g.Datum)],
        };

        await instellingen.OpslaanAsync(nieuw, cancellationToken);

        logger.LogInformation("Geen klaverjassen op {Datum}.", command.Datum);
        return Result.OK;
    }
}
