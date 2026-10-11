using Curator.Core.Content;

namespace Curator.Core.Events;

/// <summary>The visit's decision was made; the patron says their exit line.</summary>
/// <param name="VisitId">The visit.</param>
/// <param name="Decision">How it ended; None when a debug cheat ended it.</param>
/// <param name="ExitLine">Their last line.</param>
public sealed record VisitEnded(string VisitId, DecisionKind Decision, string ExitLine) : GameEvent;
