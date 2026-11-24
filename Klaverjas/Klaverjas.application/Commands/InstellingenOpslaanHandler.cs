namespace Klaverjas.application.Commands;

/// <summary>
/// Stores the settings. The standings are computed, so every week follows at once. Dad's new password
/// is stored only as a hash, and never the same as the players' (D7).
/// </summary>
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
            Spelerswachtwoord = (command.Spelerswachtwoord ?? string.Empty).Trim(),
            AfzenderNaam = string.IsNullOrWhiteSpace(command.AfzenderNaam) ? oud.AfzenderNaam : command.AfzenderNaam.Trim(),
            Antwoordadres = (command.Antwoordadres ?? string.Empty).Trim(),
            Onderwerp = (command.Onderwerp ?? string.Empty).Trim(),
        };

        var fouten = Instellingen.Controleer(nieuw).Messages.Concat(WachtwoordFouten(command, oud, nieuw)).ToList();
        if (fouten.Count > 0)
        {
            return Result.WithMessages(fouten);
        }

        var nieuwWachtwoord = command.NieuwWachtwoord ?? string.Empty;
        if (nieuwWachtwoord.Length > 0)
        {
            nieuw = nieuw with { BeheerWachtwoord = Wachtwoord.Maak(nieuwWachtwoord) };
        }

        await instellingen.OpslaanAsync(nieuw, cancellationToken);

        logger.LogInformation("Instellingen opgeslagen.");
        if (nieuwWachtwoord.Length > 0)
        {
            logger.LogInformation("Wachtwoord voor het beheer gewijzigd.");
        }

        if (nieuw.Spelerswachtwoord != oud.Spelerswachtwoord)
        {
            logger.LogInformation("Wachtwoord voor de spelers gewijzigd.");
        }

        return Result.OK;
    }

    private static IEnumerable<IValidationMessage> WachtwoordFouten(InstellingenOpslaan command, Instellingen oud, Instellingen nieuw)
    {
        var wachtwoord = command.NieuwWachtwoord ?? string.Empty;
        var herhaald = command.NieuwWachtwoordHerhaald ?? string.Empty;

        if (wachtwoord.Length == 0 && herhaald.Length > 0)
        {
            yield return ValidationMessage.Error("Typ uw nieuwe wachtwoord in beide vakjes.");
        }
        else if (wachtwoord.Length is > 0 and < Wachtwoord.MinimumLengteBeheer)
        {
            yield return ValidationMessage.Error($"Uw nieuwe wachtwoord moet minstens {Wachtwoord.MinimumLengteBeheer} tekens hebben.");
        }
        else if (wachtwoord != herhaald)
        {
            yield return ValidationMessage.Error("De twee nieuwe wachtwoorden zijn niet gelijk. Typ ze nog een keer.");
        }
        else if (wachtwoord.Length > 0 && Wachtwoord.Spelersvorm(wachtwoord) == Wachtwoord.Spelersvorm(nieuw.Spelerswachtwoord))
        {
            yield return ValidationMessage.Error("Kies voor uzelf een ander wachtwoord dan dat van de spelers.");
        }

        // A players' password that opens /beheer would hand every player the admin.
        if (wachtwoord.Length == 0
            && nieuw.SpelerspaginaOpen
            && nieuw.Spelerswachtwoord != oud.Spelerswachtwoord
            && Wachtwoord.Klopt(nieuw.Spelerswachtwoord, oud.BeheerWachtwoord))
        {
            yield return ValidationMessage.Error("Het wachtwoord voor de spelers mag niet hetzelfde zijn als uw eigen wachtwoord.");
        }
    }
}
