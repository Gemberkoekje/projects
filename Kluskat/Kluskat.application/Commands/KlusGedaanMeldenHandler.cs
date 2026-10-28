namespace Kluskat.application.Commands;

/// <summary>Ticks a klus off, logs the completion and counts it as activity in the skip window.</summary>
public sealed class KlusGedaanMeldenHandler(
    IKlusRepository repository,
    IVoltooiingStore voltooiingen,
    ISessieStore sessies,
    ILogger<KlusGedaanMeldenHandler> logger)
{
    private readonly IKlusRepository repository = repository ?? throw new ArgumentNullException(nameof(repository));
    private readonly IVoltooiingStore voltooiingen = voltooiingen ?? throw new ArgumentNullException(nameof(voltooiingen));
    private readonly ISessieStore sessies = sessies ?? throw new ArgumentNullException(nameof(sessies));
    private readonly ILogger<KlusGedaanMeldenHandler> logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>Handles the command.</summary>
    /// <param name="command">The klus that was done.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>OK, or the messages that stopped it.</returns>
    public Task<Result> HandleAsync(KlusGedaanMelden command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        return MeldenAsync(command, cancellationToken);
    }

    private async Task<Result> MeldenAsync(KlusGedaanMelden command, CancellationToken cancellationToken)
    {
        var nu = Clock.UtcNow();

        // Aggregates are immutable, so the instance handed to the mutation is the klus as it was
        // before: its skip history feeds the bravery bonus and the Overwinnaar badge.
        var voor = new Klus();
        var result = await KlusMutatie.UitvoerenAsync(
            repository,
            command.KlusId,
            klus =>
            {
                voor = klus;
                return klus.Gedaan(nu, viaBeheer: false);
            },
            cancellationToken);

        if (!result.IsValid)
        {
            logger.LogWarning("Klus {KlusId} gedaan melden afgewezen: {Reden}", command.KlusId, result.Redenen());
            return result;
        }

        await voltooiingen.ToevoegenAsync(Voltooiing.Van(voor, nu), cancellationToken);

        var sessie = (await sessies.LadenAsync(cancellationToken)).Actueel(nu);
        await sessies.OpslaanAsync(sessie.NaGedaan((Guid)(Uuid)command.KlusId, nu), cancellationToken);

        logger.LogInformation("Klus {KlusId} ({Ruimte}: {Naam}) gedaan.", command.KlusId, result.Value.Ruimte, result.Value.Naam);
        return Result.OK;
    }
}
