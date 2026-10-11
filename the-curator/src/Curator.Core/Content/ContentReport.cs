using System.Globalization;
using System.Text;
using Curator.Core.Rules;
using Curator.Core.Text;

namespace Curator.Core.Content;

/// <summary>
/// Writes out/content-report.md for the designer: every patron's visits, branches, unlocks and
/// outcomes, and what each relevant book would lead to (BUILD_BRIEF §9 M2).
/// </summary>
public static class ContentReport
{
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    /// <summary>Renders the report.</summary>
    /// <param name="content">The content.</param>
    /// <returns>Markdown.</returns>
    public static string Render(ContentSet content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var md = new StringBuilder();
        md.AppendLine("# Content report").AppendLine();
        md.AppendLine(Culture, $"Generated from `game/content` (version `{content.Version}`) by the content tests. Every file is `\"placeholder\": true` unless listed under **Approved**.").AppendLine();
        Files(md, content);
        Schedule(md, content);
        Books(md, content);
        md.AppendLine("## Patrons").AppendLine();
        var order = content.Patrons
            .OrderBy(p => p.Role switch { PatronRole.Tutorial => 0, PatronRole.Story => 1, _ => 2 })
            .ThenBy(p => p.FillerOrder)
            .ThenBy(p => p.Id, StringComparer.Ordinal);
        foreach (var patron in order)
        {
            Patron(md, content, patron);
        }

        return md.ToString();
    }

    private static void Files(StringBuilder md, ContentSet content)
    {
        var approved = content.Books.Where(b => !b.Placeholder).Select(b => $"books/{b.Id}.json")
            .Concat(content.Patrons.Where(p => !p.Placeholder).Select(p => $"patrons/{p.Id}.json"))
            .ToList();
        md.AppendLine("**Approved** (`\"placeholder\": false`): " + (approved.Count == 0 ? "none yet." : string.Join(", ", approved))).AppendLine();
    }

    private static void Schedule(StringBuilder md, ContentSet content)
    {
        md.AppendLine("## Schedule").AppendLine();
        md.AppendLine("| Day | Date | Slots |");
        md.AppendLine("|---|---|---|");
        foreach (var day in content.Schedule.Days.OrderBy(d => d.Day))
        {
            var mana = day.StartMana is { } fixedMana ? string.Create(Culture, $" (mana fixed at {fixedMana})") : "";
            md.AppendLine(Culture, $"| {day.Day} | {CalendarRules.DateText(content.Schedule.Calendar, day.Day)}{mana} | {string.Join(", ", day.Slots)} |");
        }

        md.AppendLine().AppendLine(Culture, $"One-off visitors in order: {string.Join(", ", content.Fillers.Select(f => f.Name))}.").AppendLine();
    }

    private static void Books(StringBuilder md, ContentSet content)
    {
        md.AppendLine("## Books").AppendLine();
        md.AppendLine("| Book | Spine | Rarity | Fee | Pages | Dangerous pages (danger ≥ 2) |");
        md.AppendLine("|---|---|---|---|---|---|");
        foreach (var book in content.Books.OrderBy(b => b.Title, StringComparer.Ordinal))
        {
            var outliers = book.Pages.Select((p, i) => (Page: p, Number: i + 1)).Where(p => p.Page.Danger >= 2)
                .Select(p => string.Create(Culture, $"p{p.Number} {Plain(p.Page.SpellName)} ({string.Join("/", p.Page.Tags)}, danger {p.Page.Danger})"));
            md.AppendLine(Culture, $"| {book.Title} (`{book.Id}`) | {book.SpineTag} | {Camel(book.Rarity)} | {content.FeeFor(book)} | {book.Pages.Count} | {string.Join("; ", outliers)} |");
        }

        md.AppendLine();
    }

    private static void Patron(StringBuilder md, ContentSet content, Patron patron)
    {
        md.AppendLine(Culture, $"### {patron.Name} (`{patron.Id}`)").AppendLine();
        var facts = new List<string> { Camel(patron.Role), $"starts at trust {patron.StartTrust ?? content.Balance.Trust.StrangerStart}" };
        if (patron.Warded)
        {
            facts.Add("warded");
        }

        if (patron.Role == PatronRole.Filler)
        {
            facts.Add($"filler #{patron.FillerOrder}");
        }

        if (patron.PreGameLoans.Count > 0)
        {
            facts.Add($"{patron.PreGameLoans.Count} pre-game loans");
        }

        md.AppendLine(Culture, $"*{string.Join(" · ", facts)}.* {patron.Description}").AppendLine();
        foreach (var visit in patron.Visits)
        {
            Visit(md, content, visit);
        }
    }

    private static void Visit(StringBuilder md, ContentSet content, Visit visit)
    {
        var header = new List<string> { $"step {visit.Step}", $"from day {visit.EarliestDay}" };
        if (visit.MinDaysAfterPrevious > 0)
        {
            header.Add($"{visit.MinDaysAfterPrevious}+ days after the last visit");
        }

        if (!visit.When.IsAlways)
        {
            header.Add($"when {Describe(visit.When)}");
        }

        if (visit.Forced)
        {
            header.Add("**forced**");
        }

        if (visit.Patience is { } patience)
        {
            header.Add($"patience {patience}");
        }

        md.AppendLine(Culture, $"#### `{visit.Id}` — {string.Join(", ", header)}").AppendLine();
        foreach (var greeting in visit.Greeting)
        {
            var when = greeting.When.IsAlways ? "" : $" *(when {Describe(greeting.When)})*";
            md.AppendLine(Culture, $"- Greets: \"{greeting.Text}\"{when}");
        }

        var asked = visit.Request.NamesBook ? $"*{content.Book(visit.Request.BookId).Title}*, topic {visit.Request.Topic}" : $"anything on {visit.Request.Topic}";
        md.AppendLine(Culture, $"- Asks: \"{visit.Request.Text}\" ({asked})");
        var temptations = visit.Temptations.Count == 0 ? "none" : string.Join(", ", visit.Temptations.Select(t => $"{t.Tag} ({Camel(t.Use)})"));
        md.AppendLine(Culture, $"- Needs: {string.Join("/", visit.Goal.Tags)} at power {visit.Goal.MinPower}+. Temptations: {temptations}.");
        var fragments = new List<string> { $"\"{visit.ReadThoughts.Default}\"" };
        fragments.AddRange(visit.ReadThoughts.ByTrust.OrderBy(f => f.Key).Select(f => string.Create(Culture, $"at trust {f.Key}+: \"{f.Value}\"")));
        md.AppendLine(Culture, $"- Read Thoughts: {string.Join("; ", fragments)}");
        foreach (var question in visit.Questions)
        {
            var levels = string.Join(", ", question.Answers.Keys.Order());
            md.AppendLine(Culture, $"- Question `{question.Id}` (unlocked by {question.UnlockedBy}): \"{question.Text}\" — answers at trust {levels}");
        }

        foreach (var (key, outcome) in visit.Outcomes.OrderBy(o => OutcomeKeys.All.ToList().IndexOf(o.Key)))
        {
            md.AppendLine(Culture, $"- Outcome **{key}**: {Describe(content, outcome)}");
        }

        foreach (var (bookId, outcome) in visit.Overrides)
        {
            md.AppendLine(Culture, $"- Override for *{content.Book(bookId).Title}* (counts as {Camel(outcome.Category)}): {Describe(content, outcome)}");
        }

        LendMatrix(md, content, visit);
        md.AppendLine();
    }

    // What each relevant book would lead to: the book asked for, books on the same topic, and any
    // book that would cause harm in this patron's hands if they trusted the curator enough to take it.
    private static void LendMatrix(StringBuilder md, ContentSet content, Visit visit)
    {
        var minTrustForAny = visit.Alternatives.MinTrustForAny ?? content.Balance.Alternatives.MinTrustForAny;
        var sameTopic = visit.Alternatives.AcceptSameTopic ?? content.Balance.Alternatives.AcceptSameTopic;
        var rows = new List<string>();
        foreach (var book in content.Books.OrderBy(b => b.Title, StringComparer.Ordinal))
        {
            var evaluation = OutcomeRules.Evaluate(book.Pages, visit.Goal, visit.Temptations);
            var choice = OutcomeRules.ForLend(content, visit, book.Id, evaluation, _ => 0);
            string taken;
            if (book.Id == visit.Request.BookId)
            {
                taken = "asked for";
            }
            else if (visit.Forced)
            {
                continue;
            }
            else if (sameTopic && book.SpineTag == visit.Request.Topic)
            {
                taken = "same topic";
            }
            else if (choice.Category is OutcomeCategory.Harm or OutcomeCategory.Mixed && minTrustForAny <= TrustRules.Highest)
            {
                taken = $"only at trust {minTrustForAny}+";
            }
            else
            {
                continue;
            }

            var cause = choice.Cause == Cause.None ? "" : $" ({Camel(choice.Cause)})";
            rows.Add($"| {book.Title} | {taken} | {Camel(choice.Category)}{cause} | {Camel(choice.Outcome.Channel)} | `{choice.Key}` |");
        }

        if (rows.Count == 0)
        {
            return;
        }

        md.AppendLine().AppendLine("| Lend | Taken | Outcome | Heard by | From |");
        md.AppendLine("|---|---|---|---|---|");
        foreach (var row in rows)
        {
            md.AppendLine(row);
        }
    }

    private static string Describe(ContentSet content, Outcome outcome)
    {
        var parts = new List<string> { Camel(outcome.Channel) == "none" ? "silent" : Camel(outcome.Channel) };
        if (outcome.DelayDays is { } delay)
        {
            parts.Add(string.Create(Culture, $"after {delay} day(s)"));
        }

        if (outcome.Headline.Length > 0)
        {
            parts.Add($"\"{outcome.Headline}\"");
        }

        if (outcome.Text.Length > 0)
        {
            parts.Add($"— {outcome.Text}");
        }

        if (outcome.Trust != 0)
        {
            parts.Add(string.Create(Culture, $"(trust {outcome.Trust:+0;-0})"));
        }

        if (outcome.Reputation is { } reputation)
        {
            parts.Add(string.Create(Culture, $"(reputation {reputation:+0;-0;0})"));
        }

        if (!outcome.BookReturns)
        {
            parts.Add("(the book never comes back)");
        }

        if (outcome.OnReturn.RemovePage.Length > 0)
        {
            parts.Add($"(returns without {Plain(content.Page(outcome.OnReturn.RemovePage).Page.SpellName)})");
        }

        if (outcome.SetFlags.Count > 0)
        {
            parts.Add($"sets {string.Join(", ", outcome.SetFlags.Select(f => $"`{f}`"))}");
        }

        return string.Join(" ", parts);
    }

    private static string Describe(Condition when)
    {
        var parts = new List<string>();
        if (when.MinTrust is { } min)
        {
            parts.Add(string.Create(Culture, $"trust ≥ {min}"));
        }

        if (when.MaxTrust is { } max)
        {
            parts.Add(string.Create(Culture, $"trust ≤ {max}"));
        }

        if (when.PreviousDecisionIn.Count > 0)
        {
            parts.Add($"last visit ended {string.Join(" or ", when.PreviousDecisionIn.Select(Camel))}");
        }

        if (when.PreviousOutcomeIn.Count > 0)
        {
            parts.Add($"last loan was {string.Join(" or ", when.PreviousOutcomeIn.Select(Camel))}");
        }

        if (when.FlagsAll.Count > 0)
        {
            parts.Add($"flags {string.Join(", ", when.FlagsAll.Select(f => $"`{f}`"))}");
        }

        if (when.FlagsNone.Count > 0)
        {
            parts.Add($"not {string.Join(", ", when.FlagsNone.Select(f => $"`{f}`"))}");
        }

        if (when.HasBorrowed.Length > 0)
        {
            parts.Add($"has borrowed `{when.HasBorrowed}`");
        }

        return string.Join(" and ", parts);
    }

    private static string Plain(string markup) => string.Concat(PageMarkup.Parse(markup).Select(s => s.Text));

    private static string Camel<T>(T value)
        where T : struct, Enum
    {
        var name = value.ToString();
        return char.ToLowerInvariant(name[0]) + name[1..];
    }
}
