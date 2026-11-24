using Klaverjas.core.Standen;

namespace Klaverjas.core.Models;

/// <summary>
/// The standings on the players' page: what Dad put online with "Zet de stand online", with his
/// note (D2, D5). One document, replaced on every publish. This is the one place the standings are
/// stored (D8): a frozen copy, so a correction reaches the players only when Dad puts it online.
/// </summary>
public sealed record OnlineStand
{
    /// <summary>The identity of the one document.</summary>
    public const string HuidigeId = "huidig";

    /// <summary>The longest note.</summary>
    public const int MaxBericht = 1000;

    /// <summary>The document identity; always <see cref="HuidigeId"/>.</summary>
    public string Id { get; init; } = HuidigeId;

    /// <summary>The standings as shown.</summary>
    public StandWeergave Stand { get; init; } = new StandWeergave();

    /// <summary>Dad's note above the standings; may be empty.</summary>
    public string Bericht { get; init; } = string.Empty;

    /// <summary>When it went online (UTC); <see cref="DateTime.MinValue"/> when nothing is online yet.</summary>
    public DateTime OnlineSinds { get; init; } = DateTime.MinValue;

    /// <summary>A fingerprint of <see cref="Stand"/>, to tell whether the standings changed since.</summary>
    public string Vingerafdruk { get; init; } = string.Empty;

    /// <summary>Whether nothing has been put online yet.</summary>
    public bool IsLeeg => OnlineSinds == DateTime.MinValue;
}
