namespace Kluskat.application.Queries;

/// <summary>One klus, for its edit form.</summary>
public sealed record KlusOphalen : IQuery<KlusOverzicht>
{
    /// <summary>The klus.</summary>
    public KlusId KlusId { get; init; }
}
