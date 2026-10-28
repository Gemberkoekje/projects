namespace Kluskat.application.Queries;

/// <summary>The win screen after a "Gedaan".</summary>
public sealed record OverwinningOphalen : IQuery<Overwinning>
{
    /// <summary>The klus that was just done.</summary>
    public KlusId KlusId { get; init; }
}
