namespace Curator.Core.Commands;

/// <summary>Ask the patron a standard or unlocked question.</summary>
/// <param name="QuestionId">The question.</param>
public sealed record AskQuestion(string QuestionId) : Command;
