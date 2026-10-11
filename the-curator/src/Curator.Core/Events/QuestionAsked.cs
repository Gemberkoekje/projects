namespace Curator.Core.Events;

/// <summary>The curator asked a question and heard an answer.</summary>
/// <param name="QuestionId">The question.</param>
/// <param name="QuestionText">The question as asked.</param>
/// <param name="Answer">The answer.</param>
/// <param name="Specific">Whether it was an unlocked, specific question.</param>
public sealed record QuestionAsked(string QuestionId, string QuestionText, string Answer, bool Specific) : GameEvent;
