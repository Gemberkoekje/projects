using Curator.Core.Content;
using Curator.Core.Game;

namespace Curator.Core.Rules;

/// <summary>Which questions are on offer and how they're answered (BUILD_BRIEF §5.9).</summary>
public static class QuestionRules
{
    /// <summary>The questions on offer in the visit at the counter: the standard ones, then unlocked ones.</summary>
    /// <param name="content">The content.</param>
    /// <param name="state">The state; must have a visit.</param>
    /// <returns>The questions.</returns>
    public static IReadOnlyList<AvailableQuestion> Available(ContentSet content, GameState state)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(state);
        if (!state.HasVisit)
        {
            return [];
        }

        var visit = content.Visit(state.Visit.VisitId).Visit;
        var asked = state.Visit.Asked.Select(a => a.QuestionId).ToHashSet(StringComparer.Ordinal);
        var questions = content.Questions.Standard
            .Select(q => new AvailableQuestion(q.Id, visit.Request.NamesBook ? q.Text : q.TextTopic, false, asked.Contains(q.Id)))
            .ToList();
        questions.AddRange(visit.Questions
            .Where(q => IsUnlocked(content, state, q, state.Visit.PatronId))
            .Select(q => new AvailableQuestion(q.Id, q.Text, true, asked.Contains(q.Id))));
        return questions;
    }

    /// <summary>Whether a specific question is unlocked for a patron right now.</summary>
    /// <param name="content">The content.</param>
    /// <param name="state">The state.</param>
    /// <param name="question">The question.</param>
    /// <param name="patronId">The patron at the counter.</param>
    /// <returns>True when unlocked.</returns>
    public static bool IsUnlocked(ContentSet content, GameState state, SpecificQuestion question, string patronId)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(question);
        if (question.UnlockedBy == "readThoughts")
        {
            return state.Visit.Unlocks.Contains(question.Id);
        }

        var colon = question.UnlockedBy.IndexOf(':', StringComparison.Ordinal);
        var target = question.UnlockedBy[(colon + 1)..];
        return question.UnlockedBy[..colon] switch
        {
            "identifiedPage" => state.Book(content.Page(target).Book.Id).Identified.Contains(target),
            "cardBook" => ConditionRules.BorrowedBooks(content, state, patronId).Contains(target),
            "flag" => state.Flags.Contains(target),
            _ => false,
        };
    }

    /// <summary>
    /// The answer at a trust level: the visit's entry at that level, else the nearest lower one,
    /// else a deflection for that level.
    /// </summary>
    /// <param name="content">The content.</param>
    /// <param name="answers">The question's answers by trust (empty when the visit has none).</param>
    /// <param name="trust">The patron's trust.</param>
    /// <param name="pickDeflection">Picks an index among n deflections (a PRNG draw).</param>
    /// <returns>The answer.</returns>
    public static string Answer(ContentSet content, IReadOnlyDictionary<int, string> answers, int trust, Func<int, int> pickDeflection)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(pickDeflection);
        if (TrustRules.TryAtOrBelow(answers, trust, out var answer))
        {
            return answer;
        }

        return TrustRules.TryAtOrBelow(content.Questions.Deflections, trust, out var deflections) && deflections.Count > 0
            ? deflections[pickDeflection(deflections.Count)]
            : "";
    }
}
