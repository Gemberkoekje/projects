using Curator.Core.Content;

namespace Curator.Core.Rules;

/// <summary>Outcome resolution at lending (BUILD_BRIEF §5.11). There are no dice in the category.</summary>
public static class OutcomeRules
{
    /// <summary>Compares a book's pages with the patron's goal and temptations.</summary>
    /// <param name="pages">The lent book's pages, removed pages excluded.</param>
    /// <param name="goal">What the patron needs.</param>
    /// <param name="temptations">What they'd do with dangerous spells.</param>
    /// <returns>The category and cause.</returns>
    public static OutcomeEvaluation Evaluate(IEnumerable<Page> pages, VisitGoal goal, IReadOnlyList<Temptation> temptations)
    {
        ArgumentNullException.ThrowIfNull(pages);
        ArgumentNullException.ThrowIfNull(goal);
        ArgumentNullException.ThrowIfNull(temptations);
        var list = pages.ToList();
        var satisfied = list.Any(p => p.Power >= goal.MinPower && p.Tags.Any(goal.Tags.Contains));
        var risky = temptations
            .Where(t => t.Use is TemptationUse.Misuse or TemptationUse.Accident)
            .Where(t => list.Any(p => p.Tags.Contains(t.Tag)))
            .ToList();
        if (risky.Count == 0)
        {
            return new OutcomeEvaluation(satisfied ? OutcomeCategory.Good : OutcomeCategory.Unhelpful, Cause.None);
        }

        var cause = risky.Any(t => t.Use == TemptationUse.Misuse) ? Cause.Misuse : Cause.Accident;
        return new OutcomeEvaluation(satisfied ? OutcomeCategory.Mixed : OutcomeCategory.Harm, cause);
    }

    /// <summary>
    /// Picks the outcome for a lend: the visit's override for the book, else its cause-specific
    /// outcome, else its category outcome, else a generic one.
    /// </summary>
    /// <param name="content">The content (for generic outcomes).</param>
    /// <param name="visit">The visit.</param>
    /// <param name="bookId">The book lent.</param>
    /// <param name="evaluation">The rule-based category and cause.</param>
    /// <param name="pickGeneric">Picks an index among n generic outcomes (a PRNG draw).</param>
    /// <returns>The outcome chosen.</returns>
    public static OutcomeChoice ForLend(ContentSet content, Visit visit, string bookId, OutcomeEvaluation evaluation, Func<int, int> pickGeneric)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(visit);
        ArgumentNullException.ThrowIfNull(evaluation);
        ArgumentNullException.ThrowIfNull(pickGeneric);
        if (visit.Overrides.TryGetValue(bookId, out var overridden))
        {
            var cause = overridden.Category is OutcomeCategory.Harm or OutcomeCategory.Mixed ? evaluation.Cause : Cause.None;
            return new OutcomeChoice(overridden, $"override:{visit.Id}:{bookId}", overridden.Category, cause);
        }

        var specific = OutcomeKeys.ForCategoryAndCause(evaluation.Category, evaluation.Cause);
        if (visit.Outcomes.TryGetValue(specific, out var outcome))
        {
            return new OutcomeChoice(outcome, $"visit:{visit.Id}:{specific}", evaluation.Category, evaluation.Cause);
        }

        var general = OutcomeKeys.ForCategory(evaluation.Category);
        if (visit.Outcomes.TryGetValue(general, out outcome))
        {
            return new OutcomeChoice(outcome, $"visit:{visit.Id}:{general}", evaluation.Category, evaluation.Cause);
        }

        var generic = content.OutcomesGeneric.For(specific);
        var index = pickGeneric(generic.Count);
        return new OutcomeChoice(generic[index], $"generic:{specific}:{index}", evaluation.Category, evaluation.Cause);
    }

    /// <summary>The outcome for a decline or walk-out: the visit's own, else silence.</summary>
    /// <param name="visit">The visit.</param>
    /// <param name="category">Declined or WalkedOut.</param>
    /// <returns>The outcome chosen.</returns>
    public static OutcomeChoice ForNonLend(Visit visit, OutcomeCategory category)
    {
        ArgumentNullException.ThrowIfNull(visit);
        var key = OutcomeKeys.ForCategory(category);
        return visit.Outcomes.TryGetValue(key, out var outcome)
            ? new OutcomeChoice(outcome, $"visit:{visit.Id}:{key}", category, Cause.None)
            : new OutcomeChoice(Outcome.Silent, $"default:{key}", category, Cause.None);
    }

    /// <summary>Fills {name} and {book} in an outcome's text.</summary>
    /// <param name="text">The text.</param>
    /// <param name="patronName">The patron's name.</param>
    /// <param name="bookTitle">The book's title, or empty.</param>
    /// <returns>The filled text.</returns>
    public static string Fill(string text, string patronName, string bookTitle)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.Replace("{name}", patronName, StringComparison.Ordinal)
            .Replace("{book}", bookTitle, StringComparison.Ordinal);
    }
}
