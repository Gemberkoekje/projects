using System.Text.Json;
using CharacterCreator.application.Rulesets;
using CharacterCreator.application.Void;
using CharacterCreator.core.Content;
using CharacterCreator.core.Rules;
using CharacterCreator.core.Rulesets;
using CharacterCreator.core.Skills;

namespace CharacterCreator.test.Rulesets;

/// <summary>
/// Joining VOID's skills to the content into a ruleset (DESIGN.md §4.2): VOID settles the rules, the
/// content adds the text, and a skill outside the book is never named.
/// </summary>
public sealed class RulesetBuilder_specs
{
    private const int SecretOrder = 9;

    private static readonly DateTimeOffset Now = new (2026, 11, 07, 09, 15, 00, TimeSpan.Zero);

    private static readonly SkillKey Literacy = new ("literacy");
    private static readonly SkillKey LifeforceA = new ("lifeforce-a");
    private static readonly SkillKey LifeforceB = new ("lifeforce-b");
    private static readonly SkillKey Paladin = new ("paladin");
    private static readonly SkillKey OldSkill = new ("old-skill");
    private static readonly SkillKey Missing = new ("missing");

    private static readonly ContentBook Content = ContentBook.Create(
        "2026-test",
        "Regelboek test",
        [
            Skill(Literacy, "literacy", bookCost: 0),
            Skill(LifeforceA, "lifeforce A", bookCost: 2),
            Skill(LifeforceB, "lifeforce B", bookCost: 2),
            Skill(Paladin, "paladin", bookCost: 3),
            Skill(OldSkill, "old skill", bookCost: 1),
            Skill(Missing, "not in void", bookCost: -1),
        ],
        [],
        []).Value;

    [Test]
    public void voids_numbers_count_and_the_content_supplies_the_keys()
    {
        var outcome = Build(Void());

        var paladin = outcome.Ruleset.Rules.Single(rule => rule.Key == Paladin);
        paladin.Cost.Should().Be(2, "VOID's cost counts, not the book's 3");
        paladin.Requirements.Should().ContainSingle()
            .Which.Skills.Should().Equal(LifeforceA, RulesetBuilder.Hidden(SecretOrder));
    }

    [Test]
    public void names_are_matched_ignoring_case_as_voids_own_loader_does()
        => Build(Void()).Ruleset.Rules.Should().Contain(rule => rule.Key == LifeforceB, "VOID calls it 'Lifeforce B'");

    [Test]
    public void a_skill_outside_the_book_is_never_named_in_the_ruleset()
    {
        var outcome = Build(Void());

        var json = JsonSerializer.Serialize(outcome.Ruleset, RulesetJson.Options);

        json.Should().NotContain("secret order").And.Contain("hidden-9");
        outcome.Ruleset.Rules.Should().NotContain(rule => rule.Key == RulesetBuilder.Hidden(SecretOrder), "only book skills get rules");
    }

    [Test]
    public void the_report_names_it_for_sl_eyes()
    {
        var report = Build(Void()).Report;

        report.HiddenPrerequisites.Should().ContainSingle()
            .Which.Should().Be(new HiddenPrerequisite { Skill = Paladin, VoidName = "secret order" });
        report.NotInBook.Should().Equal("secret order");
    }

    [Test]
    public void a_book_skill_void_lacks_is_reported_and_not_offered()
    {
        var outcome = Build(Void());

        outcome.Report.NotInVoid.Should().Equal("missing (not in void)");
        outcome.Ruleset.Offered().Should().NotContain(Missing);
    }

    [Test]
    public void a_cost_that_differs_from_the_book_is_reported_unless_the_book_cost_is_unknown()
        => Build(Void()).Report.CostDifferences.Should().Equal(new CostDifference { Skill = Paladin, VoidCost = 2, BookCost = 3 });

    [Test]
    public void a_skill_void_retired_keeps_its_rules_but_is_not_offered()
    {
        var outcome = Build(Void());

        outcome.Report.DeprecatedOffered.Should().Equal(OldSkill);
        outcome.Ruleset.Rules.Should().Contain(rule => rule.Key == OldSkill && rule.Deprecated, "characters that have it still need its rules");
        outcome.Ruleset.Offered().Should().Equal(Literacy, LifeforceA, LifeforceB, Paladin);
        outcome.Report.Offered.Should().Be(4);
    }

    [Test]
    public void energy_and_imbue_cap_come_from_void()
    {
        var outcome = Build(Void());

        outcome.Ruleset.Rules.Single(rule => rule.Key == Paladin).Energy.Should().BeEquivalentTo(
        [
            new EnergyGrant { Kind = EnergyKind.Energy, Type = "Spiritual", Amount = 3 },
            new EnergyGrant { Kind = EnergyKind.ImbueCap, Type = "Elemental", Amount = 2 },
        ]);
        outcome.Report.EnergyTypes.Should().Equal("Elemental", "Spiritual");
    }

    [Test]
    public void the_ruleset_records_who_when_and_from_where()
    {
        var ruleset = Build(Void() with { Synthesized = true }).Ruleset;

        ruleset.Id.Should().Be("2026-test@20261107T091500Z");
        ruleset.ContentRuleset.Should().Be("2026-test");
        ruleset.Book.Should().Be("Regelboek test");
        ruleset.PublishedAt.Should().Be(Now);
        ruleset.PublishedBy.Should().Be("Sanne");
        ruleset.Source.Should().Be("https://void.test/");
        ruleset.Provisional.Should().BeTrue("the rules came from a synthesized snapshot");
        ruleset.ToRuleBook().IsValid.Should().BeTrue();
        ruleset.ToContentBook().IsValid.Should().BeTrue();
    }

    [Test]
    public void voids_rules_for_a_new_character_are_the_ones_published()
    {
        var creation = new CreationRules { StartXp = 16m, MaxLeftOver = 2m };

        var outcome = Build(Void() with { Creation = creation, CreationFromVoid = true });

        outcome.Ruleset.Creation.Should().Be(creation);
        outcome.Ruleset.ToRuleBook().Value.Creation.Should().Be(creation);
        outcome.Report.Creation.Should().Be(creation);
        outcome.Report.CreationFromVoid.Should().BeTrue();
    }

    [Test]
    public void a_void_without_them_publishes_the_character_creators_own_and_says_so()
    {
        var report = Build(Void()).Report;

        report.Creation.Should().Be(CreationRules.Vortex);
        report.CreationFromVoid.Should().BeFalse();
    }

    [Test]
    public void the_first_publish_has_no_changes_to_report()
        => Build(Void()).Report.Changes.Should().BeEmpty();

    [Test]
    public void a_later_publish_reports_what_changed_skill_by_skill()
    {
        var previous = Build(Void()).Ruleset;
        var changed = Void() with
        {
            Skills =
            [
                VoidSkill(1, "literacy", cost: 1),
                VoidSkill(2, "lifeforce A", cost: 2),
                VoidSkill(4, "paladin", cost: 2) with
                {
                    Manatype = "Spiritual",
                    ManaAmount = 3,
                    ImbueType = "Elemental",
                    ImbueCap = 2,
                    Requirements = [Any(1, 2, SecretOrder)],
                },
                VoidSkill(5, "old skill", cost: 1) with { Deprecated = true },
                VoidSkill(6, "not in void", cost: 1),
                VoidSkill(SecretOrder, "secret order", cost: 1),
            ],
        };

        var report = RulesetBuilder.Build(Content, changed, previous, "Sanne", Now.AddDays(1)).Report;

        report.Changes.Should().BeEquivalentTo(
        [
            new RuleChange { Skill = Literacy, Kind = RuleChangeKind.Cost, Before = "0", After = "1" },
            new RuleChange { Skill = Missing, Kind = RuleChangeKind.Added, Before = string.Empty, After = "1 XP, 1/1" },
            new RuleChange { Skill = LifeforceB, Kind = RuleChangeKind.Removed, Before = "2 XP, 1/1", After = string.Empty },
        ]);
    }

    [Test]
    public void a_change_in_prerequisites_is_reported_with_both_versions()
    {
        var previous = Build(Void()).Ruleset;
        var changed = Void() with
        {
            Skills = [.. Void().Skills.Select(skill => skill.Id == 3 ? skill with { Requirements = [Points(4, 2)] } : skill)],
        };

        var change = RulesetBuilder.Build(Content, changed, previous, "Sanne", Now.AddDays(1)).Report.Changes.Should().ContainSingle().Subject;

        change.Skill.Should().Be(LifeforceB);
        change.Kind.Should().Be(RuleChangeKind.Requirements);
        change.Before.Should().Be("Any 1: lifeforce-a");
        change.After.Should().Be("Points 4: lifeforce-a");
    }

    [Test]
    public void a_void_name_used_twice_is_reported_and_the_first_one_is_used()
    {
        var doubled = Void() with { Skills = [.. Void().Skills, VoidSkill(20, "Literacy", cost: 7)] };

        var outcome = Build(doubled);

        outcome.Report.DuplicateVoidNames.Should().Equal("literacy");
        outcome.Ruleset.Rules.Single(rule => rule.Key == Literacy).Cost.Should().Be(0);
    }

    [Test]
    public void a_void_without_one_book_skill_cannot_be_published()
    {
        var report = Build(Void() with { Skills = [VoidSkill(SecretOrder, "secret order", cost: 1)] }).Report;

        report.CanPublish.Should().BeFalse();
        report.Errors.Should().ContainSingle().Which.Should().Contain("Not one book skill was found in VOID");
    }

    [Test]
    public void rules_the_engine_would_refuse_cannot_be_published()
    {
        var broken = Void() with
        {
            Skills = [.. Void().Skills.Select(skill => skill.Id == 1 ? skill with { Cost = -1 } : skill)],
        };

        var report = Build(broken).Report;

        report.CanPublish.Should().BeFalse();
        report.Errors.Should().ContainSingle().Which.Should().Contain("'literacy' has a negative cost");
    }

    [Test]
    public void a_prerequisite_on_a_skill_void_did_not_send_is_hidden_by_its_id()
    {
        var dangling = Void() with
        {
            Skills = [.. Void().Skills.Select(skill => skill.Id == 3 ? skill with { Requirements = [Any(1, 99)] } : skill)],
        };

        var outcome = Build(dangling);

        outcome.Ruleset.Rules.Single(rule => rule.Key == LifeforceB).Requirements.Single().Skills.Should().Equal(new SkillKey("hidden-99"));
        outcome.Report.HiddenPrerequisites.Should().Contain(new HiddenPrerequisite { Skill = LifeforceB, VoidName = "#99" });
    }

    private static ImportOutcome Build(VoidSnapshot snapshot) => RulesetBuilder.Build(Content, snapshot, null!, "Sanne", Now);

    /// <summary>A VOID with the content's skills (one renamed in case, one retired, one missing) and an SL-only one.</summary>
    private static VoidSnapshot Void() => new ()
    {
        Source = "https://void.test/",
        TakenAt = Now,
        Synthesized = false,
        Skills =
        [
            VoidSkill(1, "literacy", cost: 0),
            VoidSkill(2, "lifeforce A", cost: 2),
            VoidSkill(3, "Lifeforce B", cost: 2) with { Requirements = [Any(1, 2)] },
            VoidSkill(4, "paladin", cost: 2) with
            {
                Manatype = "Spiritual",
                ManaAmount = 3,
                ImbueType = "Elemental",
                ImbueCap = 2,
                Requirements = [Any(1, 2, SecretOrder)],
            },
            VoidSkill(5, "old skill", cost: 1) with { Deprecated = true },
            VoidSkill(SecretOrder, "secret order", cost: 1),
        ],
    };

    private static VoidSkill VoidSkill(int id, string name, int cost) => new ()
    {
        Id = id,
        Name = name,
        Cost = cost,
        BaseMax = 1,
        TimesMax = 1,
        Deprecated = false,
        Manatype = string.Empty,
        ManaAmount = 0,
        ImbueType = string.Empty,
        ImbueCap = 0,
        Loresheet = false,
        Requirements = [],
    };

    private static VoidRequirement Any(int amount, params int[] skills) => new () { Kind = "any", Amount = amount, Skills = skills };

    private static VoidRequirement Points(int amount, params int[] skills) => new () { Kind = "points", Amount = amount, Skills = skills };

    private static SkillContent Skill(SkillKey key, string voidName, int bookCost) => new ()
    {
        Key = key,
        Void = voidName,
        Book = [voidName],
        Name = new LocalizedText { Nl = voidName, En = voidName },
        Category = new LocalizedText { Nl = "Test", En = "Test" },
        Page = 11,
        PageFrom = "2026-test",
        BookCost = bookCost,
        Description = LocalizedText.Empty,
        Properties = [],
        Spells = [],
        Recipes = [],
        Loresheet = Loresheet.None,
    };
}
