namespace Kluskat.application.Spel;

/// <summary>The inputs every game question needs, loaded in one go.</summary>
/// <param name="Log">Every completion.</param>
/// <param name="Ruimtes">The rooms of the active klussen.</param>
/// <param name="Profiel">The stored choices.</param>
internal sealed record Spelgegevens(IReadOnlyList<Voltooiing> Log, IReadOnlyCollection<string> Ruimtes, Profiel Profiel)
{
    /// <summary>Loads the log, the rooms and the profile.</summary>
    internal static async Task<Spelgegevens> LadenAsync(
        IVoltooiingStore voltooiingen,
        IKlusOverzichtStore overzichten,
        IProfielStore profielen,
        CancellationToken cancellationToken)
    {
        var log = await voltooiingen.AlleAsync(cancellationToken);
        var klussen = await overzichten.ActieveAsync(cancellationToken);
        var profiel = await profielen.LadenAsync(cancellationToken);

        return new Spelgegevens(log, [.. klussen.Select(k => k.Ruimte).Distinct(StringComparer.OrdinalIgnoreCase)], profiel);
    }

    /// <summary>The score now.</summary>
    internal Spelstand Spelstand(DateTime nu) => Spelregels.Bereken(Log, Ruimtes, nu);

    /// <summary>What the cat wears, given a score: the stored choice, if it is still unlocked.</summary>
    internal Accessoire Draagt(Spelstand spelstand)
        => spelstand.Accessoires.Contains(Profiel.Accessoire) ? Profiel.Accessoire : Accessoire.None;
}
