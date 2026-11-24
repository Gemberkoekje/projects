using System.Security.Cryptography;
using System.Text.Json;

namespace Klaverjas.application.Online;

/// <summary>The standings as they would go online now (D2), and how to tell them apart from what is online.</summary>
internal static class HuidigeStand
{
    /// <summary>The standings of the latest entered evening's season, as the players see them.</summary>
    /// <param name="spelers">Every player.</param>
    /// <param name="avonden">Every entered evening.</param>
    /// <param name="instellingen">The settings.</param>
    /// <returns>The standings as shown.</returns>
    public static StandWeergave Bereken(IReadOnlyList<Speler> spelers, IReadOnlyList<Avond> avonden, Instellingen instellingen)
    {
        var jaar = avonden.Count == 0 ? Kalender.Vandaag().Year : avonden.Max(a => a.Datum).Year;
        return StandWeergave.Van(StandBerekening.Bereken(spelers, avonden, instellingen, jaar), instellingen);
    }

    /// <summary>A fingerprint of what the players see: equal fingerprints, equal pages.</summary>
    /// <param name="weergave">The standings as shown.</param>
    /// <returns>A hex string.</returns>
    public static string Vingerafdruk(StandWeergave weergave)
        => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(weergave)));

    /// <summary>Whether the standings changed after they were put online.</summary>
    /// <param name="online">What is online.</param>
    /// <param name="weergave">The standings now.</param>
    /// <returns><see langword="true"/> when something is online and it differs from now.</returns>
    public static bool IsGewijzigd(OnlineStand online, StandWeergave weergave)
        => !online.IsLeeg && online.Vingerafdruk != Vingerafdruk(weergave);
}
