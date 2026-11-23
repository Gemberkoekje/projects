namespace Klaverjas.application.Commands;

/// <summary>Stores the settings. The standings are computed, so every week follows at once.</summary>
public sealed class InstellingenOpslaanHandler(IInstellingenStore instellingen, ILogger<InstellingenOpslaanHandler> logger)
{
    private readonly IInstellingenStore instellingen = instellingen ?? throw new ArgumentNullException(nameof(instellingen));
    private readonly ILogger<InstellingenOpslaanHandler> logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>Handles the command.</summary>
    /// <param name="command">The new settings.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>OK, or what is wrong.</returns>
    public Task<Result> HandleAsync(InstellingenOpslaan command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        return OpslaanAsync(command, cancellationToken);
    }

    private async Task<Result> OpslaanAsync(InstellingenOpslaan command, CancellationToken cancellationToken)
    {
        var oud = await instellingen.LadenAsync(cancellationToken);
        var nieuw = oud with
        {
            Club = (command.Club ?? string.Empty).Trim(),
            PuntenAfwezig = command.PuntenAfwezig,
            Speeldag = command.Speeldag,
            EersteAvond = command.EersteAvond,
            Kleuren = command.Kleuren ?? new Kleuren(),
        };

        var controle = Instellingen.Controleer(nieuw);
        if (!controle.IsValid)
        {
            return controle;
        }

        await instellingen.OpslaanAsync(nieuw, cancellationToken);

        logger.LogInformation("Instellingen opgeslagen.");
        return Result.OK;
    }
}
