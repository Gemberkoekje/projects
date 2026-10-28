namespace Kluskat.application.Queries;

/// <summary>The beheer page: every active klus.</summary>
public sealed record KlussenOphalen : IQuery<IReadOnlyList<KlusOverzicht>>;
