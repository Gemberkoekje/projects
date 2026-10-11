namespace Curator.Core.Content;

/// <summary>Fallback lines for decisions a visit doesn't script, and other shared lines.</summary>
public sealed record GenericLines
{
    public required string Lent { get; init; }

    public required string Alternative { get; init; }

    public required string OfferRefused { get; init; }

    public required string Declined { get; init; }

    public required string WalkedOut { get; init; }

    /// <summary>The tell shown at patience 1 or less.</summary>
    public required string Impatient { get; init; }

    /// <summary>Said when the requested book is out on loan.</summary>
    public string BookOut { get; init; } = "";

    /// <summary>Shown when Read Thoughts finds nothing (a warded mind, or no fragment written).</summary>
    public string NoThoughts { get; init; } = "";
}
