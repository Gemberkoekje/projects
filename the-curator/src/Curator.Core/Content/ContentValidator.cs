using Curator.Core.Text;

namespace Curator.Core.Content;

/// <summary>The content rules of BUILD_BRIEF §8.3 and CONTENT_GUIDE, run on every load and as tests.</summary>
public static class ContentValidator
{
    /// <summary>The fewest books that must hold an outlier of danger 2 or more.</summary>
    public const int MinBooksWithOutliers = 4;

    private const int MaxTrust = 3;

    /// <summary>Checks parsed content.</summary>
    /// <param name="parts">The parsed files.</param>
    /// <returns>Every problem found.</returns>
    public static IReadOnlyList<ContentProblem> Validate(ContentParts parts)
    {
        ArgumentNullException.ThrowIfNull(parts);
        var run = new Run(parts);
        run.All();
        return run.Problems;
    }

    private sealed class Run
    {
        private readonly ContentParts parts;
        private readonly HashSet<string> spineTags;
        private readonly HashSet<string> effectTags;
        private readonly HashSet<string> standardQuestions;
        private readonly HashSet<string> ingredientIds;
        private readonly Dictionary<string, Book> books = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Page> pages = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Patron> patrons = new(StringComparer.Ordinal);
        private readonly HashSet<string> flagsSet = new(StringComparer.Ordinal);
        private readonly List<(string File, string Path, string Flag)> flagsRead = [];

        public Run(ContentParts parts)
        {
            this.parts = parts;
            spineTags = parts.Tags.SpineTags.ToHashSet(StringComparer.Ordinal);
            effectTags = parts.Tags.EffectTags.Select(t => t.Id).ToHashSet(StringComparer.Ordinal);
            standardQuestions = parts.Questions.Standard.Select(q => q.Id).ToHashSet(StringComparer.Ordinal);
            ingredientIds = parts.Ingredients.Ingredients.Select(i => i.Id).ToHashSet(StringComparer.Ordinal);
        }

        public List<ContentProblem> Problems { get; } = [];

        private int WeekDays => parts.Balance.Week.Days;

        public void All()
        {
            Balance();
            Tags();
            Questions();
            Ingredients();
            IndexBooksAndPatrons();
            Schedule();
            foreach (var book in parts.Books)
            {
                BookRules(book);
            }

            Catalogue();
            foreach (var patron in parts.Patrons)
            {
                PatronRules(patron);
            }

            Generic();
            Newspaper();
            Letters();
            Flags();
            Reachability();
        }

        private void Error(string file, string path, string message) =>
            Problems.Add(new ContentProblem(ProblemSeverity.Error, file, path, message));

        private void Warn(string file, string path, string message) =>
            Problems.Add(new ContentProblem(ProblemSeverity.Warning, file, path, message));

        private string FileOf(string id) => parts.FileById.TryGetValue(id, out var file) ? file : id;

        private void Balance()
        {
            const string file = "balance.json";
            var b = parts.Balance;
            foreach (var rarity in Enum.GetValues<Rarity>().Where(r => r != Rarity.None))
            {
                if (!b.Money.FeeByRarity.TryGetValue(rarity, out var fee))
                {
                    Error(file, "$.money.feeByRarity", $"no fee for rarity '{Camel(rarity)}'");
                }
                else if (fee < 0)
                {
                    Error(file, $"$.money.feeByRarity.{Camel(rarity)}", "fee is negative");
                }
            }

            if (b.Money.FeeByRarity.ContainsKey(Rarity.None))
            {
                Error(file, "$.money.feeByRarity", "'none' is not a rarity");
            }

            foreach (var category in Enum.GetValues<OutcomeCategory>().Where(c => c != OutcomeCategory.None))
            {
                if (!b.Outcomes.DefaultDelayDays.TryGetValue(category, out var delay))
                {
                    Error(file, "$.outcomes.defaultDelayDays", $"no delay for category '{Camel(category)}'");
                }
                else if (delay < 0)
                {
                    Error(file, $"$.outcomes.defaultDelayDays.{Camel(category)}", "delay is negative");
                }
            }

            NotNegative(file, "$.mana.basePerDay", b.Mana.BasePerDay);
            NotNegative(file, "$.mana.readThoughtsCost", b.Mana.ReadThoughtsCost);
            NotNegative(file, "$.mana.identifyCost", b.Mana.IdentifyCost);
            NotNegative(file, "$.mana.rolloverCap", b.Mana.RolloverCap);
            NotNegative(file, "$.mana.attentiveBonusCap", b.Mana.AttentiveBonusCap);
            NotNegative(file, "$.mana.goodOutcomeBonus", b.Mana.GoodOutcomeBonus);
            NotNegative(file, "$.mana.totalBonusCap", b.Mana.TotalBonusCap);
            NotNegative(file, "$.money.upkeepPerDay", b.Money.UpkeepPerDay);
            NotNegative(file, "$.patience.questionCost", b.Patience.QuestionCost);
            NotNegative(file, "$.patience.identifyCost", b.Patience.IdentifyCost);
            NotNegative(file, "$.patience.refusedOfferCost", b.Patience.RefusedOfferCost);
            NotNegative(file, "$.patience.readThoughtsCost", b.Patience.ReadThoughtsCost);
            AtLeastOne(file, "$.patience.default", b.Patience.Default);
            AtLeastOne(file, "$.loans.defaultDays", b.Loans.DefaultDays);
            AtLeastOne(file, "$.week.days", b.Week.Days);
            TrustLevel(file, "$.trust.strangerStart", b.Trust.StrangerStart);
            TrustLevel(file, "$.reputation.warmStrangerTrust", b.Reputation.WarmStrangerTrust);
            if (b.Reputation.WaryThreshold >= b.Reputation.WarmThreshold)
            {
                Error(file, "$.reputation", "waryThreshold must be below warmThreshold");
            }
        }

        private void Tags()
        {
            const string file = "tags.json";
            Duplicates(file, "$.spineTags", parts.Tags.SpineTags, "spine tag");
            Duplicates(file, "$.effectTags", parts.Tags.EffectTags.Select(t => t.Id), "effect tag");
            for (var i = 0; i < parts.Tags.EffectTags.Count; i++)
            {
                if (parts.Tags.EffectTags[i].About.Trim().Length == 0)
                {
                    Warn(file, $"$.effectTags[{i}].about", "effect tag has no 'about'");
                }
            }
        }

        private void Questions()
        {
            const string file = "questions.json";
            var q = parts.Questions;
            Duplicates(file, "$.standard", q.Standard.Select(s => s.Id), "standard question");
            if (!q.Deflections.TryGetValue(0, out var lowest) || lowest.Count == 0)
            {
                Error(file, "$.deflections", "needs at least one deflection at trust 0");
            }

            foreach (var (trust, lines) in q.Deflections)
            {
                TrustLevel(file, $"$.deflections.{trust}", trust);
                if (lines.Any(l => l.Trim().Length == 0))
                {
                    Error(file, $"$.deflections.{trust}", "empty deflection");
                }
            }

            var lines2 = q.GenericLines;
            foreach (var (name, line) in new[]
                     {
                         ("lent", lines2.Lent), ("alternative", lines2.Alternative), ("offerRefused", lines2.OfferRefused),
                         ("declined", lines2.Declined), ("walkedOut", lines2.WalkedOut), ("impatient", lines2.Impatient),
                     })
            {
                if (line.Trim().Length == 0)
                {
                    Error(file, $"$.genericLines.{name}", "generic line is empty");
                }
            }

            if (lines2.BookOut.Trim().Length == 0)
            {
                Warn(file, "$.genericLines.bookOut", "no line for a requested book that's out");
            }

            if (lines2.NoThoughts.Trim().Length == 0)
            {
                Warn(file, "$.genericLines.noThoughts", "no line for Read Thoughts finding nothing");
            }
        }

        private void Ingredients()
        {
            const string file = "ingredients.json";
            Duplicates(file, "$.ingredients", parts.Ingredients.Ingredients.Select(i => i.Id), "ingredient");
            for (var i = 0; i < parts.Ingredients.Ingredients.Count; i++)
            {
                if (parts.Ingredients.Ingredients[i].Name.Trim().Length == 0)
                {
                    Error(file, $"$.ingredients[{i}].name", "ingredient has no name");
                }
            }
        }

        private void IndexBooksAndPatrons()
        {
            foreach (var book in parts.Books)
            {
                if (!books.TryAdd(book.Id, book))
                {
                    Error(FileOf(book.Id), "$.id", $"duplicate book id '{book.Id}'");
                }

                for (var i = 0; i < book.Pages.Count; i++)
                {
                    if (!pages.TryAdd(book.Pages[i].Id, book.Pages[i]))
                    {
                        Error(FileOf(book.Id), $"$.pages[{i}].id", $"duplicate page id '{book.Pages[i].Id}'");
                    }
                }
            }

            foreach (var patron in parts.Patrons)
            {
                if (!patrons.TryAdd(patron.Id, patron))
                {
                    Error(FileOf(patron.Id), "$.id", $"duplicate patron id '{patron.Id}'");
                }

                if (patron.Warded)
                {
                    flagsSet.Add($"{patron.Id}:noticed");
                }
            }

            var visitIds = new HashSet<string>(StringComparer.Ordinal);
            var questionIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var patron in parts.Patrons)
            {
                for (var v = 0; v < patron.Visits.Count; v++)
                {
                    var visit = patron.Visits[v];
                    if (!visitIds.Add(visit.Id))
                    {
                        Error(FileOf(patron.Id), $"$.visits[{v}].id", $"duplicate visit id '{visit.Id}'");
                    }

                    for (var q = 0; q < visit.Questions.Count; q++)
                    {
                        if (!questionIds.Add(visit.Questions[q].Id) || standardQuestions.Contains(visit.Questions[q].Id))
                        {
                            Error(FileOf(patron.Id), $"$.visits[{v}].questions[{q}].id", $"duplicate question id '{visit.Questions[q].Id}'");
                        }
                    }
                }
            }
        }

        private void Schedule()
        {
            const string file = "schedule.json";
            var s = parts.Schedule;
            if (s.Calendar.Months.Count == 0)
            {
                Error(file, "$.calendar.months", "no months");
            }

            Duplicates(file, "$.calendar.months", s.Calendar.Months, "month");
            AtLeastOne(file, "$.calendar.daysPerMonth", s.Calendar.DaysPerMonth);
            if (!s.Calendar.Months.Contains(s.Calendar.Day1.Month))
            {
                Error(file, "$.calendar.day1.month", $"'{s.Calendar.Day1.Month}' is not one of the months");
            }

            if (s.Calendar.Day1.Day < 1 || s.Calendar.Day1.Day > s.Calendar.DaysPerMonth)
            {
                Error(file, "$.calendar.day1.day", "day is outside the month");
            }

            var seen = new HashSet<int>();
            for (var i = 0; i < s.Days.Count; i++)
            {
                var day = s.Days[i];
                if (day.Day < 1 || day.Day > WeekDays)
                {
                    Error(file, $"$.days[{i}].day", $"day {day.Day} is outside 1-{WeekDays}");
                }

                if (!seen.Add(day.Day))
                {
                    Error(file, $"$.days[{i}].day", $"day {day.Day} is listed twice");
                }

                if (day.StartMana is < 0)
                {
                    Error(file, $"$.days[{i}].startMana", "startMana is negative");
                }

                for (var j = 0; j < day.Slots.Count; j++)
                {
                    var slot = day.Slots[j];
                    if (slot == ContentSet.FillerSlot)
                    {
                        continue;
                    }

                    if (!patrons.TryGetValue(slot, out var patron))
                    {
                        Error(file, $"$.days[{i}].slots[{j}]", $"no patron '{slot}'");
                    }
                    else if (patron.Role == PatronRole.Filler)
                    {
                        Warn(file, $"$.days[{i}].slots[{j}]", $"one-off visitor '{slot}' is scheduled by id; use \"filler\"");
                    }
                }
            }

            for (var day = 1; day <= WeekDays; day++)
            {
                if (!seen.Contains(day))
                {
                    Error(file, "$.days", $"no schedule for day {day}");
                }
            }
        }

        private void BookRules(Book book)
        {
            var file = FileOf(book.Id);
            FileNameMatches(file, book.Id, "books");
            NonEmpty(file, "$.title", book.Title, "title");
            if (!spineTags.Contains(book.SpineTag))
            {
                Error(file, "$.spineTag", $"'{book.SpineTag}' is not a spine tag");
            }

            if (book.Rarity == Rarity.None)
            {
                Error(file, "$.rarity", "rarity is required");
            }

            if (book.Fee is < 0)
            {
                Error(file, "$.fee", "fee is negative");
            }

            if (book.IdentifyResistance is < 0 or >= 1)
            {
                Error(file, "$.identifyResistance", "must be at least 0 and below 1");
            }

            if (book.Pages.Count is < 3 or > 10)
            {
                Error(file, "$.pages", $"has {book.Pages.Count} pages; books have 3-10");
            }

            if (!book.Pages.Any(p => p.Danger == 0))
            {
                Error(file, "$.pages", "needs at least one page of danger 0");
            }

            for (var i = 0; i < book.Pages.Count; i++)
            {
                PageRules(file, $"$.pages[{i}]", book, book.Pages[i], i);
            }
        }

        private void PageRules(string file, string path, Book book, Page page, int index)
        {
            if (page.Id != $"{book.Id}-{index + 1}")
            {
                Warn(file, $"{path}.id", $"page ids are '<bookId>-<n>'; expected '{book.Id}-{index + 1}'");
            }

            Markup(file, $"{path}.spellName", page.SpellName);
            Markup(file, $"{path}.text", page.Text);
            NonEmpty(file, $"{path}.text", page.Text, "text");
            if (page.Tags.Count == 0)
            {
                Error(file, $"{path}.tags", "page has no effect tags");
            }

            for (var t = 0; t < page.Tags.Count; t++)
            {
                if (!effectTags.Contains(page.Tags[t]))
                {
                    Error(file, $"{path}.tags[{t}]", $"'{page.Tags[t]}' is not an effect tag");
                }
            }

            if (page.Tags.Contains("unknown") && !page.Unidentifiable)
            {
                Warn(file, $"{path}.tags", "'unknown' is for story pages that can't be identified");
            }

            if (page.Power is < 0 or > 3)
            {
                Error(file, $"{path}.power", "power is 0-3");
            }

            if (page.Danger is < 0 or > 3)
            {
                Error(file, $"{path}.danger", "danger is 0-3");
            }

            for (var g = 0; g < page.Ingredients.Count; g++)
            {
                if (!ingredientIds.Contains(page.Ingredients[g]))
                {
                    Error(file, $"{path}.ingredients[{g}]", $"no ingredient '{page.Ingredients[g]}'");
                }
            }

            if (!page.Unidentifiable && page.Ingredients.Count is < 2 or > 5)
            {
                Warn(file, $"{path}.ingredients", $"has {page.Ingredients.Count} ingredients; spells have 2-5");
            }

            Duplicates(file, $"{path}.ingredients", page.Ingredients, "ingredient");
        }

        private void Catalogue()
        {
            var withOutliers = parts.Books.Count(b => b.Pages.Any(p => p.Danger >= 2));
            if (withOutliers < MinBooksWithOutliers)
            {
                Error("books/", "$", $"only {withOutliers} book(s) hold an outlier of danger 2 or more; at least {MinBooksWithOutliers} must");
            }
        }

        private void PatronRules(Patron patron)
        {
            var file = FileOf(patron.Id);
            FileNameMatches(file, patron.Id, "patrons");
            NonEmpty(file, "$.name", patron.Name, "name");
            NonEmpty(file, "$.description", patron.Description, "description");
            if (patron.Role == PatronRole.None)
            {
                Error(file, "$.role", "role is required");
            }

            if (patron.StartTrust is { } startTrust)
            {
                TrustLevel(file, "$.startTrust", startTrust);
            }

            if (patron.Role == PatronRole.Filler)
            {
                if (patron.FillerOrder < 1)
                {
                    Error(file, "$.fillerOrder", "one-off visitors need a fillerOrder of 1 or more");
                }
                else if (parts.Patrons.Any(p => p != patron && p.Role == PatronRole.Filler && p.FillerOrder == patron.FillerOrder))
                {
                    Error(file, "$.fillerOrder", $"fillerOrder {patron.FillerOrder} is used twice");
                }

                if (patron.Visits.Count != 1 || patron.Visits[0].Step != 1)
                {
                    Error(file, "$.visits", "one-off visitors have exactly one visit, step 1");
                }
            }
            else if (patron.FillerOrder != 0)
            {
                Warn(file, "$.fillerOrder", "fillerOrder only matters for one-off visitors");
            }

            if (patron.Role == PatronRole.Story)
            {
                var steps = patron.Visits.Select(v => v.Step).Distinct().Count();
                if (steps is < 2 or > 3)
                {
                    Warn(file, "$.visits", $"story patrons have 2-3 steps; this one has {steps}");
                }
            }

            for (var i = 0; i < patron.PreGameLoans.Count; i++)
            {
                var loan = patron.PreGameLoans[i];
                var path = $"$.preGameLoans[{i}]";
                BookExists(file, $"{path}.bookId", loan.BookId);
                if (loan.BorrowedDay > 0 || loan.ReturnedDay > 0)
                {
                    Error(file, path, "pre-game loans happen on day 0 or earlier");
                }

                if (loan.ReturnedDay < loan.BorrowedDay)
                {
                    Error(file, path, "returned before it was borrowed");
                }
            }

            var maxStep = patron.Visits.Count == 0 ? 0 : patron.Visits.Max(v => v.Step);
            for (var step = 1; step <= maxStep; step++)
            {
                if (patron.Visits.All(v => v.Step != step))
                {
                    Warn(file, "$.visits", $"no visit for step {step}");
                }
            }

            for (var v = 0; v < patron.Visits.Count; v++)
            {
                VisitRules(file, $"$.visits[{v}]", patron, patron.Visits[v]);
            }
        }

        private void VisitRules(string file, string path, Patron patron, Visit visit)
        {
            if (!visit.Id.StartsWith($"{patron.Id}-{visit.Step}", StringComparison.Ordinal))
            {
                Warn(file, $"{path}.id", $"visit ids are '<patronId>-<step>' with an optional suffix; got '{visit.Id}'");
            }

            if (visit.Step < 1)
            {
                Error(file, $"{path}.step", "step is 1 or more");
            }

            if (visit.EarliestDay < 1 || visit.EarliestDay > WeekDays)
            {
                Error(file, $"{path}.earliestDay", $"earliestDay is 1-{WeekDays}");
            }

            NotNegative(file, $"{path}.minDaysAfterPrevious", visit.MinDaysAfterPrevious);
            if (visit.Patience is { } patience)
            {
                AtLeastOne(file, $"{path}.patience", patience);
            }

            if (visit.LoanDays is { } loanDays)
            {
                AtLeastOne(file, $"{path}.loanDays", loanDays);
            }

            ConditionRules(file, $"{path}.when", visit.When);

            if (visit.Greeting.Count == 0)
            {
                Error(file, $"{path}.greeting", "no greeting");
            }

            for (var g = 0; g < visit.Greeting.Count; g++)
            {
                NonEmpty(file, $"{path}.greeting[{g}].text", visit.Greeting[g].Text, "greeting");
                ConditionRules(file, $"{path}.greeting[{g}].when", visit.Greeting[g].When);
            }

            if (visit.Greeting.Count > 0 && !visit.Greeting[^1].When.IsAlways)
            {
                Error(file, $"{path}.greeting", "the last greeting variant must have no 'when'");
            }

            RequestRules(file, $"{path}.request", visit);
            GoalRules(file, path, visit);
            ReadThoughtsRules(file, $"{path}.readThoughts", visit);
            AnswersRules(file, $"{path}.answers", visit);
            for (var q = 0; q < visit.Questions.Count; q++)
            {
                QuestionRules(file, $"{path}.questions[{q}]", visit.Questions[q]);
            }

            DecisionRules(file, $"{path}.decisions.lent", visit.Decisions.Lent);
            DecisionRules(file, $"{path}.decisions.alternative", visit.Decisions.Alternative);
            DecisionRules(file, $"{path}.decisions.offerRefused", visit.Decisions.OfferRefused);
            DecisionRules(file, $"{path}.decisions.declined", visit.Decisions.Declined);
            DecisionRules(file, $"{path}.decisions.walkedOut", visit.Decisions.WalkedOut);

            foreach (var (key, outcome) in visit.Outcomes)
            {
                if (!OutcomeKeys.All.Contains(key))
                {
                    Error(file, $"{path}.outcomes.{key}", $"'{key}' is not an outcome key");
                }

                OutcomeRules(file, $"{path}.outcomes.{key}", outcome, visit.Request.BookId, isOverride: false);
            }

            foreach (var (bookId, outcome) in visit.Overrides)
            {
                BookExists(file, $"{path}.overrides.{bookId}", bookId);
                OutcomeRules(file, $"{path}.overrides.{bookId}", outcome, bookId, isOverride: true);
            }

            if (patron.Role == PatronRole.Story)
            {
                foreach (var key in new[] { OutcomeKeys.Good, OutcomeKeys.Unhelpful })
                {
                    if (!visit.Outcomes.ContainsKey(key))
                    {
                        Error(file, $"{path}.outcomes", $"story visits define a '{key}' outcome");
                    }
                }

                if (!visit.Outcomes.ContainsKey(OutcomeKeys.Harm) && !visit.Outcomes.ContainsKey(OutcomeKeys.HarmMisuse) &&
                    !visit.Outcomes.ContainsKey(OutcomeKeys.HarmAccident))
                {
                    Error(file, $"{path}.outcomes", "story visits define a 'harm' outcome (or harmMisuse/harmAccident)");
                }
            }

            if (visit.Forced && !visit.Request.NamesBook)
            {
                Error(file, $"{path}.forced", "a forced visit must ask for a particular book");
            }

            if (visit.Alternatives.MinTrustForAny is < 0 or > MaxTrust + 1)
            {
                Error(file, $"{path}.alternatives.minTrustForAny", "is 0-4 (4 means never)");
            }
        }

        private void RequestRules(string file, string path, Visit visit)
        {
            if (!spineTags.Contains(visit.Request.Topic))
            {
                Error(file, $"{path}.topic", $"'{visit.Request.Topic}' is not a spine tag");
            }

            if (visit.Request.NamesBook)
            {
                BookExists(file, $"{path}.bookId", visit.Request.BookId);
            }

            NonEmpty(file, $"{path}.text", visit.Request.Text, "request text");
        }

        private void GoalRules(string file, string path, Visit visit)
        {
            if (visit.Goal.Tags.Count == 0)
            {
                Error(file, $"{path}.goal.tags", "goal has no tags");
            }

            for (var t = 0; t < visit.Goal.Tags.Count; t++)
            {
                GoalTag(file, $"{path}.goal.tags[{t}]", visit.Goal.Tags[t]);
            }

            if (visit.Goal.MinPower is < 0 or > 3)
            {
                Error(file, $"{path}.goal.minPower", "minPower is 0-3");
            }

            for (var t = 0; t < visit.Temptations.Count; t++)
            {
                GoalTag(file, $"{path}.temptations[{t}].tag", visit.Temptations[t].Tag);
                if (visit.Temptations[t].Use == TemptationUse.None)
                {
                    Error(file, $"{path}.temptations[{t}].use", "use is misuse, accident or benign");
                }
            }
        }

        private void GoalTag(string file, string path, string tag)
        {
            if (tag == "unknown")
            {
                Error(file, path, "'unknown' never appears in goals or temptations");
            }
            else if (!effectTags.Contains(tag))
            {
                Error(file, path, $"'{tag}' is not an effect tag");
            }
        }

        private void ReadThoughtsRules(string file, string path, Visit visit)
        {
            var spec = visit.ReadThoughts;
            if (spec.Default.Trim().Length == 0)
            {
                Warn(file, $"{path}.default", "no fragment: Read Thoughts will find nothing");
            }

            foreach (var (trust, fragment) in spec.ByTrust)
            {
                TrustLevel(file, $"{path}.byTrust.{trust}", trust);
                NonEmpty(file, $"{path}.byTrust.{trust}", fragment, "fragment");
            }

            var questionIds = visit.Questions.Select(q => q.Id).ToHashSet(StringComparer.Ordinal);
            for (var u = 0; u < spec.Unlocks.Count; u++)
            {
                if (!questionIds.Contains(spec.Unlocks[u]))
                {
                    Error(file, $"{path}.unlocks[{u}]", $"no question '{spec.Unlocks[u]}' in this visit");
                }
            }

            foreach (var question in visit.Questions.Where(q => q.UnlockedBy == "readThoughts"))
            {
                if (!spec.Unlocks.Contains(question.Id))
                {
                    Error(file, $"{path}.unlocks", $"question '{question.Id}' is unlocked by readThoughts but isn't listed here");
                }
            }

            foreach (var id in spec.Unlocks)
            {
                if (visit.Questions.FirstOrDefault(q => q.Id == id) is { } question && question.UnlockedBy != "readThoughts")
                {
                    Error(file, $"{path}.unlocks", $"question '{id}' is listed here but unlocked by '{question.UnlockedBy}'");
                }
            }
        }

        private void AnswersRules(string file, string path, Visit visit)
        {
            foreach (var (questionId, byTrust) in visit.Answers)
            {
                if (!standardQuestions.Contains(questionId))
                {
                    Error(file, $"{path}.{questionId}", $"'{questionId}' is not a standard question");
                }

                foreach (var (trust, answer) in byTrust)
                {
                    TrustLevel(file, $"{path}.{questionId}.{trust}", trust);
                    NonEmpty(file, $"{path}.{questionId}.{trust}", answer, "answer");
                }
            }
        }

        private void QuestionRules(string file, string path, SpecificQuestion question)
        {
            NonEmpty(file, $"{path}.text", question.Text, "question text");
            UnlockRules(file, $"{path}.unlockedBy", question.UnlockedBy);
            if (question.Answers.Count == 0)
            {
                Error(file, $"{path}.answers", "question has no answers");
            }

            foreach (var (trust, answer) in question.Answers)
            {
                TrustLevel(file, $"{path}.answers.{trust}", trust);
                NonEmpty(file, $"{path}.answers.{trust}", answer, "answer");
            }

            TrustDelta(file, $"{path}.trust", question.Trust);
            SetsFlags(file, $"{path}.setFlags", question.SetFlags);
            if (question.PatienceCost is < 0)
            {
                Error(file, $"{path}.patienceCost", "patienceCost is negative");
            }
        }

        private void UnlockRules(string file, string path, string unlockedBy)
        {
            if (unlockedBy == "readThoughts")
            {
                return;
            }

            var colon = unlockedBy.IndexOf(':', StringComparison.Ordinal);
            var kind = colon < 0 ? unlockedBy : unlockedBy[..colon];
            var target = colon < 0 ? "" : unlockedBy[(colon + 1)..];
            switch (kind)
            {
                case "identifiedPage":
                    if (!pages.ContainsKey(target))
                    {
                        Error(file, path, $"no page '{target}'");
                    }

                    break;
                case "cardBook":
                    BookExists(file, path, target);
                    break;
                case "flag":
                    ReadsFlag(file, path, target);
                    break;
                default:
                    Error(file, path, $"'{unlockedBy}' is not readThoughts, identifiedPage:<id>, cardBook:<id> or flag:<flag>");
                    break;
            }
        }

        private void DecisionRules(string file, string path, DecisionLine decision)
        {
            if (decision.Trust is { } trust)
            {
                TrustDelta(file, $"{path}.trust", trust);
            }

            SetsFlags(file, $"{path}.setFlags", decision.SetFlags);
        }

        private void OutcomeRules(string file, string path, Outcome outcome, string bookId, bool isOverride)
        {
            if (outcome.DelayDays is < 0)
            {
                Error(file, $"{path}.delayDays", "delayDays is negative");
            }

            if (outcome.ReturnInDays is < 1)
            {
                Error(file, $"{path}.returnInDays", "returnInDays is 1 or more");
            }

            TrustDelta(file, $"{path}.trust", outcome.Trust);
            SetsFlags(file, $"{path}.setFlags", outcome.SetFlags);
            if (outcome.Channel != OutcomeChannel.None && outcome.Text.Trim().Length == 0)
            {
                Error(file, $"{path}.text", "an outcome that surfaces needs text");
            }

            if (outcome.Channel == OutcomeChannel.Newspaper)
            {
                if (outcome.Headline.Trim().Length == 0)
                {
                    Warn(file, $"{path}.headline", "newspaper outcomes need a headline");
                }
                else if (WordCount(outcome.Headline) > 8)
                {
                    Warn(file, $"{path}.headline", "headlines are at most 8 words");
                }
            }

            if (outcome.Channel == OutcomeChannel.Letter && outcome.From.Trim().Length == 0)
            {
                Warn(file, $"{path}.from", "letter outcomes need a sender");
            }

            if (outcome.OnReturn.RemovePage.Length > 0)
            {
                if (!pages.ContainsKey(outcome.OnReturn.RemovePage))
                {
                    Error(file, $"{path}.onReturn.removePage", $"no page '{outcome.OnReturn.RemovePage}'");
                }
                else if (bookId.Length == 0)
                {
                    Error(file, $"{path}.onReturn.removePage", "removePage needs a known book: use it in an override, or in a visit that asks for a particular book");
                }
                else if (books.TryGetValue(bookId, out var book) && book.Pages.All(p => p.Id != outcome.OnReturn.RemovePage))
                {
                    Error(file, $"{path}.onReturn.removePage", $"page '{outcome.OnReturn.RemovePage}' isn't in '{bookId}'");
                }
            }

            if (isOverride)
            {
                if (outcome.Category is not (OutcomeCategory.Good or OutcomeCategory.Unhelpful or OutcomeCategory.Harm or OutcomeCategory.Mixed))
                {
                    Error(file, $"{path}.category", "an override needs a category: good, unhelpful, harm or mixed");
                }
            }
            else if (outcome.Category != OutcomeCategory.None)
            {
                Warn(file, $"{path}.category", "only overrides use 'category'; it's ignored here");
            }
        }

        private void ConditionRules(string file, string path, Condition when)
        {
            if (when.MinTrust is { } min)
            {
                TrustLevel(file, $"{path}.minTrust", min);
            }

            if (when.MaxTrust is { } max)
            {
                TrustLevel(file, $"{path}.maxTrust", max);
            }

            if (when.MinTrust is { } low && when.MaxTrust is { } high && low > high)
            {
                Error(file, path, "minTrust is above maxTrust: the condition can never hold");
            }

            foreach (var flag in when.FlagsAll.Concat(when.FlagsNone))
            {
                ReadsFlag(file, path, flag);
            }

            if (when.PreviousDecisionIn.Contains(DecisionKind.None))
            {
                Error(file, $"{path}.previousDecisionIn", "decisions are lent, alternative, declined or walkedOut");
            }

            if (when.PreviousOutcomeIn.Any(c => c is not (OutcomeCategory.Good or OutcomeCategory.Unhelpful or OutcomeCategory.Harm or OutcomeCategory.Mixed)))
            {
                Error(file, $"{path}.previousOutcomeIn", "outcomes are good, unhelpful, harm or mixed");
            }

            if (when.HasBorrowed.Length > 0)
            {
                BookExists(file, $"{path}.hasBorrowed", when.HasBorrowed);
            }
        }

        private void Generic()
        {
            const string file = "outcomes_generic.json";
            var generic = parts.OutcomesGeneric;
            foreach (var key in new[] { OutcomeKeys.Good, OutcomeKeys.Unhelpful, OutcomeKeys.HarmMisuse, OutcomeKeys.HarmAccident, OutcomeKeys.MixedMisuse, OutcomeKeys.MixedAccident })
            {
                var outcomes = generic.For(key);
                if (outcomes.Count == 0)
                {
                    Error(file, $"$.{key}", "needs at least one outcome");
                }
                else if (outcomes.All(o => o.Channel != OutcomeChannel.None))
                {
                    Warn(file, $"$.{key}", "has no silent outcome");
                }

                for (var i = 0; i < outcomes.Count; i++)
                {
                    OutcomeRules(file, $"$.{key}[{i}]", outcomes[i], "", isOverride: false);
                    if (outcomes[i].Channel == OutcomeChannel.Return)
                    {
                        Warn(file, $"$.{key}[{i}].channel", "generic outcomes on 'return' arrive as letters for one-off visitors");
                    }
                }
            }
        }

        private void Newspaper()
        {
            const string file = "newspaper.json";
            var paper = parts.Newspaper;
            NonEmpty(file, "$.masthead", paper.Masthead, "masthead");
            foreach (var day in paper.Flavour.Keys.Where(d => d < 1 || d > WeekDays))
            {
                Error(file, $"$.flavour.{day}", $"day {day} is outside 1-{WeekDays}");
            }

            for (var day = 1; day <= WeekDays; day++)
            {
                if (!paper.Flavour.TryGetValue(day, out var items) || items.Count == 0)
                {
                    Warn(file, "$.flavour", $"no flavour items for day {day}");
                }
            }

            foreach (var (band, lines) in new[] { ("wary", paper.ToneLines.Wary), ("neutral", paper.ToneLines.Neutral), ("warm", paper.ToneLines.Warm) })
            {
                if (lines.Count == 0 || lines.Any(l => l.Trim().Length == 0))
                {
                    Error(file, $"$.toneLines.{band}", "needs at least one non-empty line");
                }
            }
        }

        private void Letters()
        {
            const string file = "letters.json";
            var letters = parts.Letters;
            for (var i = 0; i < letters.Tutorial.Count; i++)
            {
                var letter = letters.Tutorial[i];
                if (letter.Day < 1 || letter.Day > WeekDays)
                {
                    Error(file, $"$.tutorial[{i}].day", $"day is 1-{WeekDays}");
                }

                NonEmpty(file, $"$.tutorial[{i}].text", letter.Text, "letter text");
                NonEmpty(file, $"$.tutorial[{i}].from", letter.From, "sender");
            }

            for (var level = 1; level <= 3; level++)
            {
                if (!letters.Debt.TryGetValue(level, out var letter))
                {
                    Error(file, "$.debt", $"no debt letter for level {level}");
                    continue;
                }

                NonEmpty(file, $"$.debt.{level}.text", letter.Text, "letter text");
                NonEmpty(file, $"$.debt.{level}.from", letter.From, "sender");
            }
        }

        private void Flags()
        {
            foreach (var (file, path, flag) in flagsRead)
            {
                if (!flagsSet.Contains(flag))
                {
                    Warn(file, path, $"flag '{flag}' is never set");
                }
            }
        }

        private void Reachability()
        {
            foreach (var patron in parts.Patrons.Where(p => p.Role is PatronRole.Story or PatronRole.Tutorial))
            {
                var file = FileOf(patron.Id);
                var slotDays = parts.Schedule.Days.OrderBy(d => d.Day)
                    .SelectMany(d => d.Slots.Where(s => s == patron.Id).Select(_ => d.Day))
                    .ToList();
                if (slotDays.Count == 0)
                {
                    Error(file, "$", $"'{patron.Id}' has no slot in the schedule");
                    continue;
                }

                for (var v = 0; v < patron.Visits.Count; v++)
                {
                    var visit = patron.Visits[v];
                    var path = $"$.visits[{v}]";
                    if (visit.EarliestDay > slotDays[^1])
                    {
                        Error(file, $"{path}.earliestDay", $"day {visit.EarliestDay} is after {patron.Id}'s last slot (day {slotDays[^1]})");
                        continue;
                    }

                    var usableSlots = slotDays.Skip(visit.Step - 1).Count(day => day >= visit.EarliestDay);
                    if (usableSlots == 0)
                    {
                        Warn(file, path, $"step {visit.Step} has no slot left on or after day {visit.EarliestDay}");
                    }

                    if (visit.Step > 1 && visit.When.PreviousDecisionIn.Count > 0)
                    {
                        var previous = patron.Visits.Where(p => p.Step == visit.Step - 1).ToList();
                        if (!visit.When.PreviousDecisionIn.Any(d => previous.Any(p => CanEndWith(p, d))))
                        {
                            Warn(file, $"{path}.when.previousDecisionIn", "no visit at the previous step can end with these decisions");
                        }
                    }

                    if (visit.Step > 1 && visit.MinDaysAfterPrevious > 0 && !SpacingFits(slotDays, visit))
                    {
                        Warn(file, $"{path}.minDaysAfterPrevious", $"no two of {patron.Id}'s slots are {visit.MinDaysAfterPrevious} days apart in time for step {visit.Step}");
                    }

                    if (visit.When.MinTrust is { } minTrust && minTrust > HighestTrustBefore(patron, visit.Step))
                    {
                        Warn(file, $"{path}.when.minTrust", $"trust {minTrust} can't be reached by step {visit.Step}");
                    }
                }
            }
        }

        // Whether some slot for the previous step and a later one for this step are far enough apart.
        private static bool SpacingFits(IReadOnlyList<int> slotDays, Visit visit)
        {
            for (var previous = visit.Step - 2; previous < slotDays.Count; previous++)
            {
                for (var next = previous + 1; next < slotDays.Count; next++)
                {
                    if (slotDays[next] >= visit.EarliestDay && slotDays[next] - slotDays[previous] >= visit.MinDaysAfterPrevious)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        // An upper bound on a patron's trust as a step begins: their start, plus every trust gain
        // the earlier steps could give (an accepted alternative, questions, outcomes).
        private int HighestTrustBefore(Patron patron, int step)
        {
            var trust = patron.StartTrust ?? parts.Balance.Trust.StrangerStart;
            foreach (var earlier in patron.Visits.Where(v => v.Step < step).GroupBy(v => v.Step))
            {
                trust += earlier.Max(v =>
                    Math.Max(0, Math.Max(v.Decisions.Alternative.Trust ?? parts.Balance.Trust.AlternativeAccepted, v.Decisions.Lent.Trust ?? 0)) +
                    v.Questions.Sum(q => Math.Max(0, q.Trust)) +
                    v.Outcomes.Values.Concat(v.Overrides.Values).Select(o => Math.Max(0, o.Trust)).DefaultIfEmpty(0).Max());
            }

            return Math.Min(trust, MaxTrust);
        }

        private static bool CanEndWith(Visit visit, DecisionKind decision) => decision switch
        {
            DecisionKind.Lent => true,
            DecisionKind.Alternative => visit.Request.NamesBook && !visit.Forced,
            DecisionKind.Declined => !visit.Forced,
            DecisionKind.WalkedOut => true,
            _ => false,
        };

        private void SetsFlags(string file, string path, IReadOnlyList<string> flags)
        {
            for (var i = 0; i < flags.Count; i++)
            {
                if (FlagShape(file, $"{path}[{i}]", flags[i]))
                {
                    flagsSet.Add(flags[i]);
                }
            }
        }

        private void ReadsFlag(string file, string path, string flag)
        {
            if (FlagShape(file, path, flag))
            {
                flagsRead.Add((file, path, flag));
            }
        }

        private bool FlagShape(string file, string path, string flag)
        {
            var colon = flag.IndexOf(':', StringComparison.Ordinal);
            if (colon <= 0 || colon == flag.Length - 1)
            {
                Error(file, path, $"flag '{flag}' isn't '<patronId>:<flag>'");
                return false;
            }

            if (!patrons.ContainsKey(flag[..colon]))
            {
                Error(file, path, $"flag '{flag}' names no patron");
                return false;
            }

            return true;
        }

        private void BookExists(string file, string path, string bookId)
        {
            if (!books.ContainsKey(bookId))
            {
                Error(file, path, $"no book '{bookId}'");
            }
        }

        private void FileNameMatches(string file, string id, string folder)
        {
            if (file != $"{folder}/{id}.json")
            {
                Error(file, "$.id", $"id '{id}' doesn't match the file name");
            }
        }

        private void Markup(string file, string path, string markup)
        {
            foreach (var problem in PageMarkup.Validate(markup))
            {
                Error(file, path, $"markup: {problem}");
            }
        }

        private void NonEmpty(string file, string path, string value, string what)
        {
            if (value.Trim().Length == 0)
            {
                Error(file, path, $"{what} is empty");
            }
        }

        private void NotNegative(string file, string path, int value)
        {
            if (value < 0)
            {
                Error(file, path, "is negative");
            }
        }

        private void AtLeastOne(string file, string path, int value)
        {
            if (value < 1)
            {
                Error(file, path, "must be 1 or more");
            }
        }

        private void TrustLevel(string file, string path, int value)
        {
            if (value is < 0 or > MaxTrust)
            {
                Error(file, path, $"trust level {value} is outside 0-{MaxTrust}");
            }
        }

        private void TrustDelta(string file, string path, int value)
        {
            if (value is < -MaxTrust or > MaxTrust)
            {
                Error(file, path, $"trust change {value} is outside -{MaxTrust} to {MaxTrust}");
            }
        }

        private void Duplicates(string file, string path, IEnumerable<string> values, string what)
        {
            foreach (var duplicate in values.GroupBy(v => v, StringComparer.Ordinal).Where(g => g.Count() > 1))
            {
                Error(file, path, $"duplicate {what} '{duplicate.Key}'");
            }
        }

        private static int WordCount(string text) =>
            text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;

        private static string Camel<T>(T value)
            where T : struct, Enum
        {
            var name = value.ToString();
            return char.ToLowerInvariant(name[0]) + name[1..];
        }
    }
}
