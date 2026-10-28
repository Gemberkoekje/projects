namespace Kluskat.application.Commands;

/// <summary>Stores the chosen accessory, if it is unlocked.</summary>
public sealed class AccessoireKiezenHandler(
    IVoltooiingStore voltooiingen,
    IKlusOverzichtStore overzichten,
    IProfielStore profielen,
    ILogger<AccessoireKiezenHandler> logger)
{
    private readonly IVoltooiingStore voltooiingen = voltooiingen ?? throw new ArgumentNullException(nameof(voltooiingen));
    private readonly IKlusOverzichtStore overzichten = overzichten ?? throw new ArgumentNullException(nameof(overzichten));
    private readonly IProfielStore profielen = profielen ?? throw new ArgumentNullException(nameof(profielen));
    private readonly ILogger<AccessoireKiezenHandler> logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>Handles the command.</summary>
    /// <param name="command">The accessory.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>OK, or a message when it is still locked.</returns>
    public Task<Result> HandleAsync(AccessoireKiezen command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        return KiezenAsync(command, cancellationToken);
    }

    private async Task<Result> KiezenAsync(AccessoireKiezen command, CancellationToken cancellationToken)
    {
        var gegevens = await Spelgegevens.LadenAsync(voltooiingen, overzichten, profielen, cancellationToken);

        if (command.Accessoire != Accessoire.None && !gegevens.Spelstand(Clock.UtcNow()).Accessoires.Contains(command.Accessoire))
        {
            return Result.WithMessages(ValidationMessage.Error("Die heeft de kat nog niet verdiend."));
        }

        await profielen.OpslaanAsync(gegevens.Profiel with { Accessoire = command.Accessoire }, cancellationToken);

        logger.LogInformation("De kat draagt nu {Accessoire}.", command.Accessoire);
        return Result.OK;
    }
}
