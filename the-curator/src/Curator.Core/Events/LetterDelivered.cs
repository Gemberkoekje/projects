using Curator.Core.Game;

namespace Curator.Core.Events;

/// <summary>A letter arrived on the desk.</summary>
/// <param name="Kind">Tutorial note, debt letter or outcome.</param>
/// <param name="From">The sender.</param>
/// <param name="Title">A title, or empty.</param>
/// <param name="Text">The letter.</param>
/// <param name="ResolutionId">The outcome it carries, or empty.</param>
/// <param name="DebtLevel">For debt letters, the level 1-3; otherwise 0.</param>
public sealed record LetterDelivered(LetterKind Kind, string From, string Title, string Text, string ResolutionId, int DebtLevel) : GameEvent;
