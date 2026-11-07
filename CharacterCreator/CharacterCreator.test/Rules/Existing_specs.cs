using CharacterCreator.core.Plans;
using CharacterCreator.core.Rules;
using static CharacterCreator.test.TestHelpers.Book;

namespace CharacterCreator.test.Rules;

/// <summary>
/// An existing character, as the envelope checkout plans it (CHECKOUT-DESIGN §6): whole XP only, a
/// first purchase of an extra skill goes through a teacher and takes the XP first, one per event.
/// </summary>
public sealed class Existing_specs
{
    /// <summary>20 XP, 5 of them spent on held skills: 15 whole XP available.</summary>
    private static readonly Holdings Held = Holdings.Of((Perseverance, 1), (ArmourI, 1), (HerbLore, 1), (LifeforceA, 1));

    [Test]
    public void only_whole_xp_buys_a_quarter_point_stays_saved()
    {
        var plan = Plan.Existing(17.75m, Held, Holdings.Of((ArmourII, 1)));

        var evaluation = PlanEvaluator.Evaluate(Standard, plan);

        evaluation.Remaining.Should().Be(10m, "17.75 - 5 = 12.75, of which 12 can buy; armour II costs 2");
        evaluation.HasProblems.Should().BeFalse();
    }

    [Test]
    public void basis_purchases_beyond_the_whole_xp_are_a_problem_with_the_numbers()
    {
        var plan = Plan.Existing(9.75m, Held, Holdings.Of((ArmourII, 1), (ArmourIII, 1)));

        PlanEvaluator.Evaluate(Standard, plan).Findings
            .Should().ContainEquivalentOf(new NotEnoughXp { Needed = 5m, Available = 4m });
    }

    [Test]
    public void held_skills_are_fixed_their_prerequisites_are_not_checked()
    {
        var plan = Plan.Existing(20m, Holdings.Of((ArmourIII, 1)), Holdings.None);

        PlanEvaluator.Evaluate(Standard, plan).Findings.Should().BeEmpty("what VOID holds is VOID's record");
    }

    [Test]
    public void a_planned_skill_counts_the_held_ones_for_its_prerequisites()
        => PlanEvaluator.Evaluate(Standard, Plan.Existing(20m, Held, Holdings.Of((LifeforceB, 1)))).Findings.Should().BeEmpty();

    [Test]
    public void an_extra_skill_bought_the_first_time_needs_a_teacher()
    {
        var plan = Plan.Existing(20m, Held, Holdings.Of((StrengthB, 1)));

        var evaluation = PlanEvaluator.Evaluate(Standard, plan);

        evaluation.Findings.Should().ContainEquivalentOf(new NeedsTeacher { Skill = StrengthB });
        evaluation.HasProblems.Should().BeFalse("a teacher is how an extra skill is bought, not a broken rule");
    }

    [Test]
    public void an_extra_skill_bought_again_is_a_basis_purchase()
    {
        var plan = Plan.Existing(20m, Held.Add(TeachingExpertise, 1).Add(StrengthB, 1), Holdings.Of((TeachingExpertise, 1)));

        PlanEvaluator.Evaluate(Standard, plan).Findings.OfType<NeedsTeacher>().Should().BeEmpty();
    }

    [Test]
    public void a_teaching_takes_the_xp_first_so_a_basis_purchase_may_no_longer_fit()
    {
        var plan = Plan.Existing(9m, Held, Holdings.Of((StrengthB, 1), (ArmourII, 1)));

        var evaluation = PlanEvaluator.Evaluate(Standard, plan);

        evaluation.Findings.Should().ContainEquivalentOf(new NotEnoughXp { Needed = 2m, Available = 1m }, "4 whole XP, of which the teaching takes 3");
        evaluation.Findings.OfType<TeachingNotPaidOff>().Should().BeEmpty();
    }

    [Test]
    public void a_teaching_may_start_before_it_is_paid_off()
    {
        var plan = Plan.Existing(6.5m, Held, Holdings.Of((StrengthB, 1)));

        var evaluation = PlanEvaluator.Evaluate(Standard, plan);

        evaluation.Findings.Should().ContainEquivalentOf(new TeachingNotPaidOff { Skill = StrengthB, Owed = 2m });
        evaluation.HasProblems.Should().BeFalse("the rest is paid from the XP of later events");
        evaluation.Remaining.Should().Be(0m);
    }

    [Test]
    public void one_extra_skill_per_event_more_is_a_warning()
    {
        var plan = Plan.Existing(30m, Held, Holdings.Of((StrengthB, 1), (TeachingExpertise, 1)));

        PlanEvaluator.Evaluate(Standard, plan).Findings.OfType<MoreThanOneTeaching>()
            .Should().ContainSingle().Which.Skills.Should().Equal(StrengthB, TeachingExpertise);
    }

    [Test]
    public void times_max_counts_the_held_purchases_too()
    {
        var plan = Plan.Existing(30m, Held.Add(ElementalRitualism, 4), Holdings.Of((ElementalRitualism, 2)));

        PlanEvaluator.Evaluate(Standard, plan).Findings
            .Should().ContainEquivalentOf(new AboveMaximum { Skill = ElementalRitualism, Times = 6, Maximum = 5 });
    }

    [Test]
    public void base_max_does_not_apply_after_creation()
        => PlanEvaluator.Evaluate(Standard, Plan.Existing(30m, Held, Holdings.Of((ElementalRitualism, 5)))).HasProblems.Should().BeFalse();

    [Test]
    public void a_held_skill_the_book_does_not_know_is_only_something_to_know()
    {
        var evaluation = PlanEvaluator.Evaluate(Standard, Plan.Existing(20m, Held.Add(Unknown, 1), Holdings.None));

        evaluation.Findings.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new UnknownSkill { Skill = Unknown, Held = true });
        evaluation.Findings.Single().Severity.Should().Be(FindingSeverity.Info);
    }

    [Test]
    public void an_extra_prerequisite_counts_held_extra_skills()
        => PlanEvaluator.Evaluate(Standard, Plan.Existing(30m, Held.Add(StrengthB, 1), Holdings.Of((TeachingExpertise, 1)))).For(TeachingExpertise)
            .OfType<MissingPrerequisite>().Should().BeEmpty();

    [Test]
    public void a_skill_never_meets_its_own_prerequisite()
    {
        var plan = Plan.Existing(30m, Holdings.Of((HerbLore, 1)), Holdings.Of((BrewExpertise, 2)));

        PlanEvaluator.Evaluate(Standard, plan).For(BrewExpertise).OfType<MissingPrerequisite>()
            .Should().ContainSingle("brew expertise is in its own set, but its own XP does not count towards it");
    }

    [Test]
    public void a_teaching_skill_does_not_count_as_its_own_extra_skill()
    {
        var plan = Plan.Existing(30m, Held, Holdings.Of((TeachingExpertise, 1)));

        PlanEvaluator.Evaluate(Standard, plan).For(TeachingExpertise).OfType<MissingPrerequisite>().Should().ContainSingle();
    }
}
