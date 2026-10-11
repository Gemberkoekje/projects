namespace Curator.Core.Commands;

/// <summary>Start a new game: loads the first morning.</summary>
/// <param name="Seed">The game seed.</param>
public sealed record NewGame(long Seed) : Command;
