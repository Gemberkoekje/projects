namespace Klaverjas.application.Queries;

/// <summary>Every player: the members by name, then the substitutes by name.</summary>
public sealed record SpelersOphalen : IQuery<IReadOnlyList<Speler>>;
