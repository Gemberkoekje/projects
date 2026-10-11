namespace Curator.Core.Game;

/// <summary>A question asked in a visit and the answer heard.</summary>
/// <param name="QuestionId">The question.</param>
/// <param name="QuestionText">The question as asked.</param>
/// <param name="Answer">The answer.</param>
public sealed record AskedQuestion(string QuestionId, string QuestionText, string Answer);
