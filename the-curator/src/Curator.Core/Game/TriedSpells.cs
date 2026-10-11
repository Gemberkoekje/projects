namespace Curator.Core.Game;

/// <summary>The spells a patron tried from one book.</summary>
/// <param name="BookId">The book.</param>
/// <param name="PageIds">The pages tried.</param>
public sealed record TriedSpells(string BookId, IReadOnlyList<string> PageIds)
{
    /// <summary>Nothing tried yet.</summary>
    public static readonly TriedSpells None = new("", []);
}
