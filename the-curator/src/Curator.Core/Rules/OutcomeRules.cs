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
        var satisfied = list.Any(p => Meets(p, goal));
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

    /// <summary>Whether a page meets a goal: one of its tags, with enough power.</summary>
    /// <param name="page">The page.</param>
    /// <param name="goal">The goal.</param>
    /// <returns>True when it meets it.</returns>
    public static bool Meets(Page page, VisitGoal goal)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(goal);
        return page.Power >= goal.MinPower && page.Tags.Any(goal.Tags.Contains);
    }

    /// <summary>
    /// The spells a patron tries from a book (BUILD_BRIEF §5.11): one drawn among the pages that meet
    /// their goal, and, if the book holds a spell they'd misuse or botch, one drawn among those (of the
    /// evaluation's cause) unless the first already is one. A book with neither gets one page drawn
    /// from all of them. The category stays the evaluation's: this says which spells, not how it went.
    /// </summary>
    /// <param name="pages">The book's pages, removed pages excluded.</param>
    /// <param name="goal">What the patron needs.</param>
    /// <param name="temptations">What they'd do with dangerous spells.</param>
    /// <param name="cause">The evaluation's cause.</param>
    /// <param name="pickNeed">Picks an index among n pages for the need (a PRNG draw).</param>
    /// <param name="pickTemptation">Picks an index among n tempting pages (a different PRNG draw).</param>
    /// <returns>The page ids, the need's first.</returns>
    public static IReadOnlyList<string> SpellsTried(IEnumerable<Page> pages, VisitGoal goal, IReadOnlyList<Temptation> temptations, Cause cause, Func<int, int> pickNeed, Func<int, int> pickTemptation)
    {
        ArgumentNullException.ThrowIfNull(pages);
        ArgumentNullException.ThrowIfNull(goal);
        ArgumentNullException.ThrowIfNull(temptations);
        ArgumentNullException.ThrowIfNull(pickNeed);
        ArgumentNullException.ThrowIfNull(pickTemptation);
        var list = pages.ToList();
        var use = cause == Cause.Misuse ? TemptationUse.Misuse : TemptationUse.Accident;
        var tempting = temptations.Where(t => t.Use == use).Select(t => t.Tag).ToHashSet(StringComparer.Ordinal);
        var risky = cause == Cause.None ? [] : list.Where(p => p.Tags.Any(tempting.Contains)).ToList();
        var need = list.Where(p => Meets(p, goal)).ToList();
        var tried = new List<string>();
        if (need.Count > 0)
        {
            tried.Add(need[pickNeed(need.Count)].Id);
        }

        if (risky.Count > 0 && !risky.Any(p => tried.Contains(p.Id)))
        {
            tried.Add(risky[pickTemptation(risky.Count)].Id);
        }

        if (tried.Count == 0 && list.Count > 0)
        {
            tried.Add(list[pickNeed(list.Count)].Id);
        }

        return tried;
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
