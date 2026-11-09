namespace DatumPrikker.core.Models;

/// <summary>A respondent's choice for one date option.</summary>
public sealed record OptionAnswer
{
    /// <summary>The date option (see <see cref="DateOption.Id"/>).</summary>
    public Guid OptionId { get; init; }

    /// <summary>Ja, Misschien or Nee.</summary>
    public Availability Availability { get; init; }
}
