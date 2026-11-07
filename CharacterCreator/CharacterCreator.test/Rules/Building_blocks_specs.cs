using CharacterCreator.core.Plans;
using CharacterCreator.core.Rules;
using CharacterCreator.core.Skills;
using static CharacterCreator.test.TestHelpers.Book;

namespace CharacterCreator.test.Rules;

/// <summary>The pieces under the engine: keys, holdings, the rule book, the XP table, free planning.</summary>
public sealed class Building_blocks_specs
{
    [TestCase("any", RequirementKind.Any)]
    [TestCase("points", RequirementKind.Points)]
    [TestCase("extra", RequirementKind.Extra)]
    [TestCase(" Any ", RequirementKind.Any)]
    [TestCase("level", RequirementKind.None)]
    [TestCase("", RequirementKind.None)]
    public void void_names_requirement_kinds_in_lower_case(string text, RequirementKind kind)
        => RequirementKinds.FromVoid(text).Should().Be(kind);

    [Test]
    public void a_group_of_an_unknown_kind_always_holds_as_in_void()
        => SkillRules.Holds(new RequirementGroup(RequirementKind.None, 9, [ArmourI]), Holdings.None, Standard).Should().BeTrue();

    [Test]
    public void a_skill_key_is_trimmed_and_never_blank()
    {
        new SkillKey("  armour-i ").Should().Be(ArmourI);
        FluentActions.Invoking(() => new SkillKey(" ")).Should().Throw<ArgumentException>();
        SkillKey.Empty.IsEmpty.Should().BeTrue();
        default(SkillKey).Should().Be(SkillKey.Empty);
    }

    [Test]
    public void holdings_count_purchases_and_forget_a_skill_bought_zero_times()
    {
        var holdings = Holdings.Of((ArmourI, 1), (Literacy, 2), (ArmourI, 1)).Add(Literacy, -2);

        holdings[ArmourI].Should().Be(2);
        holdings[Literacy].Should().Be(0);
        holdings.Skills.Should().Equal(ArmourI);
    }

    [Test]
    public void holdings_keep_the_order_skills_were_first_added()
        => Holdings.Of((Literacy, 1)).Plus(Holdings.Of((ArmourI, 1), (Literacy, 1))).Skills.Should().Equal(Literacy, ArmourI);

    [Test]
    public void holdings_never_change_in_place()
    {
        var before = Holdings.Of((ArmourI, 1));

        _ = before.Add(ArmourII, 1).Without(ArmourI);

        before.Skills.Should().Equal(ArmourI);
    }

    [Test]
    public void a_rule_book_refuses_duplicate_keys_and_negative_numbers()
    {
        var result = RuleBook.Create(
        [
            new RuleSkill { Key = ArmourI, Cost = 1, BaseMax = 1, TimesMax = 1 },
            new RuleSkill { Key = ArmourI, Cost = 2, BaseMax = 1, TimesMax = 1 },
            new RuleSkill { Key = Literacy, Cost = -1, BaseMax = 1, TimesMax = 1 },
        ]);

        result.IsValid.Should().BeFalse();
        result.Messages.Select(message => message.Message).Should().HaveCount(2)
            .And.Contain(message => message.Contains("armour-i", StringComparison.Ordinal))
            .And.Contain(message => message.Contains("literacy", StringComparison.Ordinal));
    }

    [Test]
    public void a_rule_book_allows_a_prerequisite_on_a_skill_it_does_not_know()
    {
        var result = RuleBook.Create([new RuleSkill { Key = ArmourII, Cost = 2, BaseMax = 1, TimesMax = 1, Requirements = [RequirementGroup.Any(1, Unknown)] }]);

        result.IsValid.Should().BeTrue("VOID ignores a deleted skill the same way");
    }

    [Test]
    public void a_skill_with_base_max_zero_is_extra()
    {
        Standard.TryGet(StrengthB, out var strength).Should().BeTrue();
        strength.IsExtra.Should().BeTrue();
        Standard.TryGet(ArmourI, out var armour).Should().BeTrue();
        armour.IsExtra.Should().BeFalse();
    }

    [TestCase(15, 1, 16.5)]
    [TestCase(15, 4, 21)]
    [TestCase(15, 5, 22.25)]
    [TestCase(49.75, 1, 50.5)]
    public void the_xp_table_awards_each_event_from_the_total_before_it(decimal xp, int events, decimal after)
        => XpTable.Vortex.After(xp, events).Should().Be(after);

    [Test]
    public void a_book_of_experiences_adds_a_quarter_point_per_event()
        => XpTable.Vortex.After(15m, 2, withBook: true).Should().Be(18.5m);

    [Test]
    public void free_planning_is_held_to_times_max_and_the_xp_only()
    {
        var plan = Plan.Free(4m, Holdings.Of((StrengthB, 1), (Perseverance, 1), (Literacy, 2)));

        var findings = PlanEvaluator.Evaluate(Standard, plan).Findings;

        findings.Should().ContainEquivalentOf(new AboveMaximum { Skill = Literacy, Times = 2, Maximum = 1 });
        findings.Should().ContainEquivalentOf(new NotEnoughXp { Needed = 6m, Available = 4m });
        findings.OfType<NotAtCreation>().Should().BeEmpty();
        findings.OfType<NeedsTeacher>().Should().BeEmpty();
    }

    [Test]
    public void a_plan_without_a_mode_is_a_programming_error()
        => FluentActions.Invoking(() => PlanEvaluator.Evaluate(Standard, new Plan { Mode = PlanMode.None }))
            .Should().Throw<ArgumentException>();
}
