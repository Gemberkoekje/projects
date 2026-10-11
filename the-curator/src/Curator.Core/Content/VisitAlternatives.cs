namespace Curator.Core.Content;

/// <summary>A visit's own rule for accepting an offered book; absent fields use the balance.</summary>
/// <remarks>The fields are nullable because "absent" (use the balance) differs from any value.</remarks>
public sealed record VisitAlternatives
{
    /// <summary>No override: both fields come from the balance.</summary>
    public static readonly VisitAlternatives Default = new();

    public bool? AcceptSameTopic { get; init; }

    public int? MinTrustForAny { get; init; }
}
