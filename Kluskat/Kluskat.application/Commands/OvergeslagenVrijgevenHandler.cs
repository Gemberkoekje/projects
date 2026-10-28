namespace Kluskat.application.Commands;

/// <summary>Clears the skips and the "Geen tijd" cap.</summary>
public sealed class OvergeslagenVrijgevenHandler(ISessieStore sessies, ILogger<OvergeslagenVrijgevenHandler> logger)
{
    private readonly ISessieStore sessies = sessies ?? throw new ArgumentNullException(nameof(sessies));
    private readonly ILogger<OvergeslagenVrijgevenHandler> logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>Handles the command.</summary>
    /// <param name="command">The command.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>OK.</returns>
    public Task<Result> HandleAsync(OvergeslagenVrijgeven command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        return VrijgevenAsync(cancellationToken);
    }

    private async Task<Result> VrijgevenAsync(CancellationToken cancellationToken)
    {
        await sessies.OpslaanAsync(new Sessie { LaatsteActiviteit = Clock.UtcNow() }, cancellationToken);

        logger.LogInformation("Overgeslagen klussen vrijgegeven.");
        return Result.OK;
    }
}
