using CharacterCreator.core.Plans;
using CharacterCreator.core.Skills;

namespace CharacterCreator.core.Rules;

/// <summary>
/// The rules engine (DESIGN.md §3 and §6): it checks a plan against a rule book and returns facts.
/// It refuses nothing: a plan may break any rule, and the findings say which (§2, decision 7).
/// </summary>
/// <remarks>
/// Stateless and thread-safe. The same code runs in the browser (the build screen) and on the server
/// (before a plan is stored or pushed into VOID).
/// </remarks>
public static class PlanEvaluator
{
    /// <summary>Checks a plan.</summary>
    /// <param name="book">The rule book.</param>
    /// <param name="plan">The plan.</param>
    /// <returns>The XP, the findings and the energy.</returns>
    /// <exception cref="ArgumentException">The plan has no mode.</exception>
    public static PlanEvaluation Evaluate(RuleBook book, Plan plan)
    {
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(plan);

        var findings = new List<Finding>();
        foreach (var skill in plan.Held.Skills.Where(skill => !book.TryGet(skill, out _)))
        {
            findings.Add(new UnknownSkill { Skill = skill, Held = true });
        }

        var (xp, remaining) = plan.Mode switch
        {
            PlanMode.NewCharacter => NewCharacter(book, plan, findings),
            PlanMode.Existing => Existing(book, plan, findings),
            PlanMode.Free => Free(book, plan, findings),
            _ => throw new ArgumentException("A plan needs a mode.", nameof(plan)),
        };

        return new PlanEvaluation
        {
            Plan = plan,
            Xp = xp,
            Spent = SkillRules.Spent(plan.All, book),
            Remaining = remaining,
            Findings = [.. findings.OrderByDescending(finding => finding.Severity)],
            Energy = Energy(book, plan.All),
        };
    }

    /// <summary>What one more purchase of <paramref name="skill"/> would bring about, for the picker.</summary>
    /// <param name="book">The rule book.</param>
    /// <param name="plan">The plan as it is.</param>
    /// <param name="skill">The skill.</param>
    /// <returns>Whether it is offered, and every reason one more purchase would break a rule or need a teacher.</returns>
    public static SkillOption Option(RuleBook book, Plan plan, SkillKey skill)
    {
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(plan);

        return Option(book, plan, Evaluate(book, plan), skill);
    }

    /// <summary>
    /// <see cref="Option(RuleBook, Plan, SkillKey)"/> for many skills at once, as the picker shows them:
    /// the plan as it is is evaluated once, not once per skill.
    /// </summary>
    /// <param name="book">The rule book.</param>
    /// <param name="plan">The plan as it is.</param>
    /// <param name="skills">The skills.</param>
    /// <returns>Each skill's option.</returns>
    public static IReadOnlyDictionary<SkillKey, SkillOption> Options(RuleBook book, Plan plan, IEnumerable<SkillKey> skills)
    {
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(skills);

        var before = Evaluate(book, plan);
        return skills.Distinct().ToDictionary(skill => skill, skill => Option(book, plan, before, skill));
    }

    private static SkillOption Option(RuleBook book, Plan plan, PlanEvaluation before, SkillKey skill)
    {
        if (!book.TryGet(skill, out var known))
        {
            return new SkillOption { Skill = skill, Offered = false, Reasons = [new UnknownSkill { Skill = skill, Held = false }] };
        }

        if (known.Deprecated)
        {
            return new SkillOption { Skill = skill, Offered = false, Reasons = [new DeprecatedSkill { Skill = skill }] };
        }

        var after = Evaluate(book, plan.WithPurchase(skill));
        var reasons = after.For(skill).ToList();

        var newTeaching = plan.Mode == PlanMode.Existing && known.IsExtra && plan.All[skill] == 0;
        if (!newTeaching && known.Cost > before.Remaining)
        {
            reasons.Add(new NotEnoughXp { Skill = skill, Needed = known.Cost, Available = Math.Max(before.Remaining, 0m) });
        }

        var teachingsAfter = after.Findings.OfType<MoreThanOneTeaching>().FirstOrDefault();
        if (teachingsAfter is not null && !before.Findings.OfType<MoreThanOneTeaching>().Any())
        {
            reasons.Add(teachingsAfter);
        }

        return new SkillOption { Skill = skill, Offered = true, Reasons = reasons };
    }

    /// <summary>
    /// "Add with prerequisites": the purchases that meet the skill's prerequisites where each missing
    /// group names one skill, found the same way for those, deepest first, ending with the skill.
    /// </summary>
    /// <param name="book">The rule book.</param>
    /// <param name="plan">The plan as it is.</param>
    /// <param name="skill">The skill to add.</param>
    /// <returns>The purchases, and the groups that need the player's choice.</returns>
    public static PrerequisiteChain Chain(RuleBook book, Plan plan, SkillKey skill)
    {
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(plan);

        var purchases = Holdings.None;
        var unresolved = new List<RequirementGroup>();
        var visited = new HashSet<SkillKey>();

        void Visit(SkillKey current)
        {
            if (!visited.Add(current) || !book.TryGet(current, out var known))
            {
                return;
            }

            var holdings = plan.All.Plus(purchases).Without(current);
            foreach (var group in SkillRules.Missing(known.Requirements, holdings, book))
            {
                if (group.Kind == RequirementKind.Any && group.Skills.Count == 1)
                {
                    var prerequisite = group.Skills[0];
                    Visit(prerequisite);
                    var shortfall = group.Amount - plan.All.Plus(purchases)[prerequisite];
                    purchases = purchases.Add(prerequisite, Math.Max(shortfall, 0));
                }
                else if (!unresolved.Contains(group))
                {
                    unresolved.Add(group);
                }
            }
        }

        Visit(skill);
        return new PrerequisiteChain { Purchases = purchases.Add(skill, 1), Unresolved = unresolved };
    }

    /// <summary>
    /// The planned skills that would lose a prerequisite if one purchase of <paramref name="skill"/>
    /// were taken out, and the ones that would then lose theirs, and so on (DESIGN.md §5, "remove
    /// these too"). Skills the character already holds are fixed and never listed.
    /// </summary>
    /// <param name="book">The rule book.</param>
    /// <param name="plan">The plan as it is.</param>
    /// <param name="skill">The planned skill to take one purchase of out.</param>
    /// <returns>The skills that would break, in plan order; empty when nothing does.</returns>
    public static IReadOnlyList<SkillKey> Dependants(RuleBook book, Plan plan, SkillKey skill)
    {
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(plan);

        var missingBefore = plan.Planned.Skills.ToDictionary(planned => planned, planned => MissingFor(book, plan.All, planned));
        var planned = plan.Planned.Add(skill, -1);
        var broken = new List<SkillKey>();

        bool found;
        do
        {
            var all = plan.Held.Plus(planned);
            var newlyBroken = planned.Skills
                .Where(candidate => !broken.Contains(candidate))
                .Where(candidate => MissingFor(book, all, candidate).Except(missingBefore.GetValueOrDefault(candidate, [])).Any())
                .ToList();

            found = newlyBroken.Count > 0;
            foreach (var candidate in newlyBroken)
            {
                broken.Add(candidate);
                planned = planned.Without(candidate);
            }
        }
        while (found);

        return [.. plan.Planned.Skills.Where(broken.Contains)];
    }

    private static (decimal Xp, decimal Remaining) NewCharacter(RuleBook book, Plan plan, List<Finding> findings)
    {
        foreach (var (skill, times) in plan.Planned)
        {
            if (!Known(book, skill, findings, out var known))
            {
                continue;
            }

            if (known.IsExtra)
            {
                findings.Add(new NotAtCreation { Skill = skill });
            }
            else if (times > Math.Min(known.BaseMax, known.TimesMax))
            {
                findings.Add(known.BaseMax <= known.TimesMax
                    ? new AboveCreationMaximum { Skill = skill, Times = times, Maximum = known.BaseMax }
                    : new AboveMaximum { Skill = skill, Times = times, Maximum = known.TimesMax });
            }

            AddMissing(book, plan, known, findings);
        }

        var xp = book.Creation.StartXp;
        var spent = SkillRules.Spent(plan.Planned, book);
        var left = xp - spent;
        if (left < 0)
        {
            findings.Add(new NotEnoughXp { Needed = spent, Available = xp });
        }
        else if (left > book.Creation.MaxLeftOver)
        {
            findings.Add(new TooMuchLeftOver { Left = left, Limit = book.Creation.MaxLeftOver });
        }

        return (xp, left);
    }

    private static (decimal Xp, decimal Remaining) Free(RuleBook book, Plan plan, List<Finding> findings)
    {
        foreach (var (skill, times) in plan.Planned)
        {
            if (!Known(book, skill, findings, out var known))
            {
                continue;
            }

            if (times > known.TimesMax)
            {
                findings.Add(new AboveMaximum { Skill = skill, Times = times, Maximum = known.TimesMax });
            }

            AddMissing(book, plan, known, findings);
        }

        var spent = SkillRules.Spent(plan.Planned, book);
        if (spent > plan.Xp)
        {
            findings.Add(new NotEnoughXp { Needed = spent, Available = plan.Xp });
        }

        return (plan.Xp, plan.Xp - spent);
    }

    /// <summary>
    /// An existing character, as the envelope checkout plans it (CHECKOUT-DESIGN §6): whole XP only;
    /// a first purchase of an extra skill goes through a teacher, takes the XP first, and may be paid
    /// off over later events; everything else must fit in what is left.
    /// </summary>
    private static (decimal Xp, decimal Remaining) Existing(RuleBook book, Plan plan, List<Finding> findings)
    {
        var teachings = new List<RuleSkill>();
        foreach (var (skill, _) in plan.Planned)
        {
            if (!Known(book, skill, findings, out var known))
            {
                continue;
            }

            var total = plan.All[skill];
            if (total > known.TimesMax)
            {
                findings.Add(new AboveMaximum { Skill = skill, Times = total, Maximum = known.TimesMax });
            }

            if (known.IsExtra && plan.Held[skill] == 0)
            {
                findings.Add(new NeedsTeacher { Skill = skill });
                teachings.Add(known);
            }

            AddMissing(book, plan, known, findings);
        }

        if (teachings.Count > 1)
        {
            findings.Add(new MoreThanOneTeaching { Skills = [.. teachings.Select(teaching => teaching.Key)] });
        }

        var available = (decimal)SkillRules.Whole(plan.Xp - SkillRules.Spent(plan.Held, book));
        var free = available;
        foreach (var teaching in teachings)
        {
            var paid = Math.Min(free, teaching.Cost);
            free -= paid;
            if (paid < teaching.Cost)
            {
                findings.Add(new TeachingNotPaidOff { Skill = teaching.Key, Owed = teaching.Cost - paid });
            }
        }

        var basis = SkillRules.Spent(plan.Planned, book) - teachings.Sum(teaching => (decimal)teaching.Cost);
        if (basis > free)
        {
            findings.Add(new NotEnoughXp { Needed = basis, Available = free });
        }

        return (plan.Xp, free - basis);
    }

    private static bool Known(RuleBook book, SkillKey skill, List<Finding> findings, out RuleSkill known)
    {
        if (!book.TryGet(skill, out known))
        {
            findings.Add(new UnknownSkill { Skill = skill, Held = false });
            return false;
        }

        if (known.Deprecated)
        {
            findings.Add(new DeprecatedSkill { Skill = skill });
        }

        return true;
    }

    /// <summary>A skill's prerequisites are checked against the character's other skills, never itself.</summary>
    private static void AddMissing(RuleBook book, Plan plan, RuleSkill known, List<Finding> findings)
    {
        foreach (var group in MissingFor(book, plan.All, known.Key))
        {
            findings.Add(new MissingPrerequisite { Skill = known.Key, Group = group });
        }
    }

    private static IReadOnlyList<RequirementGroup> MissingFor(RuleBook book, Holdings all, SkillKey skill)
        => book.TryGet(skill, out var known) ? SkillRules.Missing(known.Requirements, all.Without(skill), book) : [];

    private static IReadOnlyList<EnergyTotal> Energy(RuleBook book, Holdings all)
        => [.. all
            .SelectMany(pair => book.TryGet(pair.Key, out var known)
                ? known.Energy.Select(grant => (grant.Kind, grant.Type, Amount: grant.Amount * pair.Value))
                : [])
            .Where(grant => grant.Kind != EnergyKind.None)
            .GroupBy(grant => (grant.Kind, grant.Type))
            .Select(group => new EnergyTotal { Kind = group.Key.Kind, Type = group.Key.Type, Amount = group.Sum(grant => grant.Amount) })
            .OrderBy(total => total.Kind)
            .ThenBy(total => total.Type, StringComparer.Ordinal)];
}
