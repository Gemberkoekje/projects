using CharacterCreator.core.Plans;
using CharacterCreator.core.Rules;
using static CharacterCreator.test.TestHelpers.Book;

namespace CharacterCreator.test.Rules;

/// <summary>A new character: 15 XP, base_max, no extra skills, at most 3 XP left over (DESIGN.md §3.3).</summary>
public sealed class NewCharacter_specs
{
    [Test]
    public void starts_with_the_rule_books_15_xp_whatever_the_plan_says()
    {
        var plan = Plan.NewCharacter(Holdings.Of((Literacy, 1))) with { Xp = 99m };

        var evaluation = PlanEvaluator.Evaluate(Standard, plan);

        evaluation.Xp.Should().Be(15m);
        evaluation.Spent.Should().Be(1m);
        evaluation.Remaining.Should().Be(14m);
    }

    [Test]
    public void a_plan_within_the_rules_has_no_problems()
    {
        var plan = Plan.NewCharacter(Holdings.Of((ArmourI, 1), (ArmourII, 1), (HealerAElemental, 1), (Paladin, 1), (Perseverance, 1), (LifeforceA, 1), (LifeforceB, 1)));

        var evaluation = PlanEvaluator.Evaluate(Standard, plan);

        evaluation.Spent.Should().Be(14m);
        evaluation.HasProblems.Should().BeFalse();
        evaluation.Findings.Should().BeEmpty();
    }

    [Test]
    public void an_extra_skill_cannot_be_bought_at_creation()
    {
        var plan = Plan.NewCharacter(Holdings.Of((Perseverance, 1), (StrengthB, 1)));

        PlanEvaluator.Evaluate(Standard, plan).For(StrengthB)
            .Should().ContainEquivalentOf(new NotAtCreation { Skill = StrengthB });
    }

    [Test]
    public void a_skill_may_be_bought_base_max_times_at_creation()
    {
        var plan = Plan.NewCharacter(Holdings.Of((ElementalRitualism, 5)));

        PlanEvaluator.Evaluate(Standard, plan).HasProblems.Should().BeFalse();
    }

    [Test]
    public void more_than_base_max_is_a_problem()
    {
        var plan = Plan.NewCharacter(Holdings.Of((ElementalRitualism, 6)));

        PlanEvaluator.Evaluate(Standard, plan).Findings
            .Should().ContainEquivalentOf(new AboveCreationMaximum { Skill = ElementalRitualism, Times = 6, Maximum = 5 });
    }

    [Test]
    public void a_missing_prerequisite_names_the_group_that_does_not_hold()
    {
        var plan = Plan.NewCharacter(Holdings.Of((ArmourII, 1)));

        var evaluation = PlanEvaluator.Evaluate(Standard, plan);

        evaluation.HasProblems.Should().BeTrue();
        evaluation.For(ArmourII).Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new MissingPrerequisite { Skill = ArmourII, Group = core.Skills.RequirementGroup.Any(1, ArmourI) });
    }

    [Test]
    public void every_group_must_hold_a_paladin_without_armour_misses_only_the_armour()
    {
        var plan = Plan.NewCharacter(Holdings.Of((HealerASpiritual, 1), (Paladin, 1)));

        PlanEvaluator.Evaluate(Standard, plan).For(Paladin).OfType<MissingPrerequisite>()
            .Should().ContainSingle().Which.Group.Skills.Should().Equal(ArmourI);
    }

    [Test]
    public void spending_more_than_15_xp_is_a_problem_with_the_numbers()
    {
        var plan = Plan.NewCharacter(Holdings.Of((HealerAElemental, 1), (HealerASpiritual, 1), (ArmourI, 1), (ArmourII, 1), (LifeforceA, 1), (LifeforceB, 1), (Perseverance, 1)));

        var evaluation = PlanEvaluator.Evaluate(Standard, plan);

        evaluation.Findings.Should().ContainEquivalentOf(new NotEnoughXp { Needed = 16m, Available = 15m });
        evaluation.Remaining.Should().Be(-1m);
    }

    [Test]
    public void more_than_3_xp_left_over_is_a_warning_not_a_problem()
    {
        var plan = Plan.NewCharacter(Holdings.Of((HealerAElemental, 1), (ArmourI, 1), (ArmourII, 1), (Paladin, 1), (Literacy, 1)));

        var evaluation = PlanEvaluator.Evaluate(Standard, plan);

        evaluation.Findings.Should().ContainEquivalentOf(new TooMuchLeftOver { Left = 5m, Limit = 3m });
        evaluation.HasProblems.Should().BeFalse("VOID only warns about XP left over");
    }

    [Test]
    public void exactly_3_xp_left_over_is_fine()
    {
        var plan = Plan.NewCharacter(Holdings.Of((HealerAElemental, 1), (ArmourI, 1), (ArmourII, 1), (Paladin, 1), (Literacy, 1), (LifeforceA, 1)));

        PlanEvaluator.Evaluate(Standard, plan).Findings.Should().BeEmpty();
    }

    [Test]
    public void a_deprecated_skill_is_a_problem()
        => PlanEvaluator.Evaluate(Standard, Plan.NewCharacter(Holdings.Of((OldSkill, 1)))).Findings
            .Should().ContainEquivalentOf(new DeprecatedSkill { Skill = OldSkill });

    [Test]
    public void a_planned_skill_the_book_does_not_know_is_a_problem()
        => PlanEvaluator.Evaluate(Standard, Plan.NewCharacter(Holdings.Of((Unknown, 1)))).Findings.OfType<UnknownSkill>()
            .Should().ContainSingle().Which.Severity.Should().Be(FindingSeverity.Problem);

    [Test]
    public void problems_come_before_warnings()
    {
        var plan = Plan.NewCharacter(Holdings.Of((ArmourII, 1)));

        var severities = PlanEvaluator.Evaluate(Standard, plan).Findings.Select(finding => finding.Severity).ToList();

        severities.Should().Equal(FindingSeverity.Problem, FindingSeverity.Warning);
    }

    [Test]
    public void energy_is_summed_per_type_over_every_purchase()
    {
        var plan = Plan.NewCharacter(Holdings.Of((HealerAElemental, 1), (ElementalRitualism, 3), (Perseverance, 1), (GlyphUse, 1)));

        PlanEvaluator.Evaluate(Standard, plan).Energy.Should().BeEquivalentTo(
            [
                new EnergyTotal { Kind = core.Skills.EnergyKind.Energy, Type = "Elemental", Amount = 3 },
                new EnergyTotal { Kind = core.Skills.EnergyKind.Energy, Type = "Elemental ritual", Amount = 3 },
                new EnergyTotal { Kind = core.Skills.EnergyKind.Energy, Type = "Willpower", Amount = 1 },
                new EnergyTotal { Kind = core.Skills.EnergyKind.ImbueCap, Type = "Elemental", Amount = 2 },
            ],
            options => options.WithStrictOrdering());
    }
}
