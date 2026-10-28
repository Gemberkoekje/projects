namespace Kluskat.application.Queries;

/// <summary>The klus screen: which one klus to show.</summary>
public sealed record VolgendeKlusOphalen : IQuery<Keuze>
{
    /// <summary>Whether the user asked to get ahead on a klus that is not due yet.</summary>
    public bool Vooruit { get; init; }
}
