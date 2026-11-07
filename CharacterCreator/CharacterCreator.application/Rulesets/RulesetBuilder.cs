using System.Globalization;
using CharacterCreator.application.Void;
using CharacterCreator.core.Content;
using CharacterCreator.core.Rules;
using CharacterCreator.core.Rulesets;
using CharacterCreator.core.Skills;

namespace CharacterCreator.application.Rulesets;

/// <summary>
/// Joins VOID's skills to a ruleset's content by VOID name, into a ruleset that could be published,
/// and reports what it found (DESIGN.md §4.2). VOID settles the rules (decision 3); the content adds
/// names and text. Pure: it reads nothing and stores nothing.
/// </summary>
/// <remarks>Stateless and thread-safe.</remarks>
public static class RulesetBuilder
{
    /// <summary>The key a skill outside the book gets in a published ruleset: VOID's id, never its name.</summary>
    /// <param name="voidId">The skill's VOID id.</param>
    /// <returns>The key, such as <c>hidden-42</c>.</returns>
    public static SkillKey Hidden(int voidId) => new (string.Create(CultureInfo.InvariantCulture, $"hidden-{voidId}"));

    /// <summary>Builds a ruleset from VOID's skills and the content.</summary>
    /// <param name="content">The content.</param>
    /// <param name="snapshot">VOID's skills.</param>
    /// <param name="previous">The ruleset published now, to report the changes against; <c>null</c> on the first publish.</param>
    /// <param name="publishedBy">Who is publishing.</param>
    /// <param name="now">The moment of publishing.</param>
    /// <returns>The ruleset and the report.</returns>
    public static ImportOutcome Build(ContentBook content, VoidSnapshot snapshot, PublishedRuleset previous, string publishedBy, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(snapshot);

        var duplicates = snapshot.Skills.GroupBy(skill => skill.Name, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1).Select(group => group.Key).ToList();
        var byName = snapshot.Skills.GroupBy(skill => skill.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var nameById = snapshot.Skills.GroupBy(skill => skill.Id).ToDictionary(group => group.Key, group => group.First().Name);
        var keyById = snapshot.Skills.GroupBy(skill => skill.Id).ToDictionary(
            group => group.Key,
            group => content.TryGetByVoidName(group.First().Name, out var known) ? known.Key : Hidden(group.Key));

        var rules = new List<RuleSkill>();
        var notInVoid = new List<string>();
        var costs = new List<CostDifference>();
        var deprecated = new List<SkillKey>();
        var hidden = new List<HiddenPrerequisite>();

        foreach (var skill in content.Skills)
        {
            if (!byName.TryGetValue(skill.Void, out var voidSkill))
            {
                notInVoid.Add($"{skill.Key} ({skill.Void})");
                continue;
            }

            rules.Add(Rule(skill.Key, voidSkill, keyById));
            hidden.AddRange(voidSkill.Requirements.SelectMany(group => group.Skills)
                .Where(id => !keyById.TryGetValue(id, out var key) || key.ToString().StartsWith("hidden-", StringComparison.Ordinal))
                .Distinct()
                .Select(id => new HiddenPrerequisite { Skill = skill.Key, VoidName = nameById.GetValueOrDefault(id, $"#{id}") }));

            if (skill.BookCost >= 0 && skill.BookCost != voidSkill.Cost)
            {
                costs.Add(new CostDifference { Skill = skill.Key, VoidCost = voidSkill.Cost, BookCost = skill.BookCost });
            }

            if (voidSkill.Deprecated)
            {
                deprecated.Add(skill.Key);
            }
        }

        var errors = Errors(rules, snapshot.Creation);

        var ruleset = new PublishedRuleset
        {
            Id = string.Create(CultureInfo.InvariantCulture, $"{content.Ruleset}@{now.UtcDateTime:yyyyMMdd'T'HHmmss'Z'}"),
            ContentRuleset = content.Ruleset,
            Book = content.Book,
            PublishedAt = now,
            PublishedBy = publishedBy ?? string.Empty,
            Source = snapshot.Source,
            Provisional = snapshot.Synthesized,
            Creation = snapshot.Creation,
            Rules = rules,
            Skills = content.Skills,
            CommonRecipes = content.CommonRecipes,
            Presets = content.Presets,
            Legacy = content.Legacy,
        };

        var report = new ImportReport
        {
            Source = snapshot.Source,
            TakenAt = snapshot.TakenAt,
            Synthesized = snapshot.Synthesized,
            Offered = ruleset.Offered().Count,
            Creation = snapshot.Creation,
            CreationFromVoid = snapshot.CreationFromVoid,
            NotInVoid = notInVoid,
            NotInBook = [.. snapshot.Skills.Where(skill => !content.TryGetByVoidName(skill.Name, out _))
                .Select(skill => skill.Deprecated ? $"{skill.Name} (deprecated)" : skill.Name)
                .Order(StringComparer.OrdinalIgnoreCase)],
            DuplicateVoidNames = duplicates,
            CostDifferences = costs,
            DeprecatedOffered = deprecated,
            HiddenPrerequisites = hidden,
            Changes = previous is null ? [] : Changes(previous.Rules, rules),
            EnergyTypes = [.. snapshot.Skills.SelectMany(skill => new[] { skill.Manatype, skill.ImbueType })
                .Where(type => !string.IsNullOrWhiteSpace(type))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase)],
            Errors = errors,
        };

        return new ImportOutcome { Ruleset = ruleset, Report = report };
    }

    private static RuleSkill Rule(SkillKey key, VoidSkill skill, Dictionary<int, SkillKey> keyById)
        => new ()
        {
            Key = key,
            Cost = skill.Cost,
            BaseMax = skill.BaseMax,
            TimesMax = skill.TimesMax,
            Deprecated = skill.Deprecated,
            Requirements = [.. skill.Requirements.Select(group => new RequirementGroup(
                RequirementKinds.FromVoid(group.Kind),
                group.Amount,
                group.Skills.Select(id => keyById.GetValueOrDefault(id, Hidden(id)))))],
            Energy = Energy(skill),
        };

    private static List<string> Errors(List<RuleSkill> rules, CreationRules creation)
    {
        var errors = new List<string>();
        var book = RuleBook.Create(rules, creation);
        if (!book.IsValid)
        {
            errors.AddRange(book.Messages.Select(message => message.Message));
        }

        if (rules.Count == 0)
        {
            errors.Add("Not one book skill was found in VOID; check the VOID address and that the rulebook draft is loaded there.");
        }

        return errors;
    }

    private static IReadOnlyList<EnergyGrant> Energy(VoidSkill skill)
    {
        var grants = new List<EnergyGrant>();
        if (!string.IsNullOrWhiteSpace(skill.Manatype) && skill.ManaAmount != 0)
        {
            grants.Add(new EnergyGrant { Kind = EnergyKind.Energy, Type = skill.Manatype, Amount = skill.ManaAmount });
        }

        if (!string.IsNullOrWhiteSpace(skill.ImbueType) && skill.ImbueCap != 0)
        {
            grants.Add(new EnergyGrant { Kind = EnergyKind.ImbueCap, Type = skill.ImbueType, Amount = skill.ImbueCap });
        }

        return grants;
    }

    private static List<RuleChange> Changes(IReadOnlyList<RuleSkill> before, IReadOnlyList<RuleSkill> after)
    {
        var old = before.ToDictionary(skill => skill.Key);
        var current = after.ToDictionary(skill => skill.Key);
        var changes = new List<RuleChange>();

        foreach (var skill in after)
        {
            if (!old.TryGetValue(skill.Key, out var was))
            {
                changes.Add(Change(skill.Key, RuleChangeKind.Added, string.Empty, Describe(skill)));
                continue;
            }

            Compare(changes, skill.Key, RuleChangeKind.Cost, was.Cost, skill.Cost);
            Compare(changes, skill.Key, RuleChangeKind.BaseMax, was.BaseMax, skill.BaseMax);
            Compare(changes, skill.Key, RuleChangeKind.TimesMax, was.TimesMax, skill.TimesMax);
            Compare(changes, skill.Key, RuleChangeKind.Deprecated, was.Deprecated, skill.Deprecated);
            Compare(changes, skill.Key, RuleChangeKind.Requirements, Describe(was.Requirements), Describe(skill.Requirements));
            Compare(changes, skill.Key, RuleChangeKind.Energy, Describe(was.Energy), Describe(skill.Energy));
        }

        changes.AddRange(before.Where(skill => !current.ContainsKey(skill.Key))
            .Select(skill => Change(skill.Key, RuleChangeKind.Removed, Describe(skill), string.Empty)));

        return changes;
    }

    private static void Compare<T>(List<RuleChange> changes, SkillKey skill, RuleChangeKind kind, T before, T after)
    {
        var was = Convert.ToString(before, CultureInfo.InvariantCulture) ?? string.Empty;
        var now = Convert.ToString(after, CultureInfo.InvariantCulture) ?? string.Empty;
        if (!string.Equals(was, now, StringComparison.Ordinal))
        {
            changes.Add(Change(skill, kind, was, now));
        }
    }

    private static RuleChange Change(SkillKey skill, RuleChangeKind kind, string before, string after)
        => new () { Skill = skill, Kind = kind, Before = before, After = after };

    private static string Describe(RuleSkill skill)
        => string.Create(CultureInfo.InvariantCulture, $"{skill.Cost} XP, {skill.BaseMax}/{skill.TimesMax}");

    private static string Describe(IReadOnlyList<RequirementGroup> groups)
        => string.Join("; ", groups.Select(group => string.Create(
            CultureInfo.InvariantCulture,
            $"{group.Kind} {group.Amount}: {string.Join(", ", group.Skills.Select(key => key.ToString()))}")));

    private static string Describe(IReadOnlyList<EnergyGrant> grants)
        => string.Join("; ", grants.Select(grant => string.Create(CultureInfo.InvariantCulture, $"{grant.Kind} {grant.Type} {grant.Amount}")));
}
