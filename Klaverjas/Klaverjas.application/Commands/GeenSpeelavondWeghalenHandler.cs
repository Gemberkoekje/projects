namespace Klaverjas.application.Commands;

/// <summary>Removes an evening without play from the settings.</summary>
public sealed class GeenSpeelavondWeghalenHandler(IInstellingenStore instellingen, ILogger<GeenSpeelavondWeghalenHandler> logger)
{
    private readonly IInstellingenStore instellingen = instellingen ?? throw new ArgumentNullException(nameof(instellingen));
    private readonly ILogger<GeenSpeelavondWeghalenHandler> logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>Handles the command.</summary>
    /// <param name="command">The date.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>OK; removing a date that is not on the list is not an error.</returns>
    public Task<Result> HandleAsync(GeenSpeelavondWeghalen command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        return WeghalenAsync(command, cancellationToken);
    }

    private async Task<Result> WeghalenAsync(GeenSpeelavondWeghalen command, CancellationToken cancellationToken)
    {
        var oud = await instellingen.LadenAsync(cancellationToken);
        await instellingen.OpslaanAsync(oud with { GeenSpeelavonden = [.. oud.GeenSpeelavonden.Where(g => g.Datum != command.Datum)] }, cancellationToken);

        logger.LogInformation("Toch klaverjassen op {Datum}.", command.Datum);
        return Result.OK;
    }
}
