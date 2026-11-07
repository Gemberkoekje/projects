using CharacterCreator.core.Plans;
using CharacterCreator.core.Rules;
using CharacterCreator.core.Skills;
using static CharacterCreator.test.TestHelpers.Book;

namespace CharacterCreator.test.Rules;

/// <summary>
/// What the picker shows per skill (DESIGN.md §5): whether one more purchase breaks a rule, and why,
/// so finding out "why not" never takes a click.
/// </summary>
public sealed class Picker_specs
{
    [Test]
    public void a_skill_without_prerequisites_and_with_xp_to_spare_can_be_bought()
    {
        var option = PlanEvaluator.Option(Standard, Plan.NewCharacter(Holdings.None), Literacy);

        option.CanBuy.Should().BeTrue();
        option.Reasons.Should().BeEmpty();
    }

    [Test]
    public void a_skill_with_a_missing_prerequisite_says_which()
    {
        var option = PlanEvaluator.Option(Standard, Plan.NewCharacter(Holdings.None), ArmourII);

        option.CanBuy.Should().BeFalse();
        option.Reasons.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new MissingPrerequisite { Skill = ArmourII, Group = RequirementGroup.Any(1, ArmourI) });
    }

    [Test]
    public void a_new_character_is_told_an_extra_skill_is_not_for_creation()
        => PlanEvaluator.Option(Standard, Plan.NewCharacter(Holdings.Of((Perseverance, 1))), StrengthB).Reasons
            .Should().ContainEquivalentOf(new NotAtCreation { Skill = StrengthB });

    [Test]
    public void an_existing_character_is_told_an_extra_skill_needs_a_teacher_and_may_buy_it()
    {
        var option = PlanEvaluator.Option(Standard, Plan.Existing(20m, Holdings.Of((Perseverance, 1)), Holdings.None), StrengthB);

        option.CanBuy.Should().BeTrue();
        option.Reasons.Should().ContainEquivalentOf(new NeedsTeacher { Skill = StrengthB });
    }

    [Test]
    public void a_skill_at_its_maximum_says_so()
        => PlanEvaluator.Option(Standard, Plan.NewCharacter(Holdings.Of((Literacy, 1))), Literacy).Reasons
            .Should().ContainEquivalentOf(new AboveCreationMaximum { Skill = Literacy, Times = 2, Maximum = 1 });

    [Test]
    public void a_skill_that_costs_more_than_is_left_gives_the_numbers()
    {
        var plan = Plan.NewCharacter(Holdings.Of((HealerAElemental, 1), (HealerASpiritual, 1), (LifeforceA, 1), (LifeforceB, 1), (Literacy, 1)));

        PlanEvaluator.Option(Standard, plan, ArmourIII).Reasons
            .Should().ContainEquivalentOf(new NotEnoughXp { Skill = ArmourIII, Needed = 3m, Available = 2m });
    }

    [Test]
    public void a_new_teaching_is_never_short_of_xp_it_is_paid_off_later()
    {
        var plan = Plan.Existing(6m, Holdings.Of((Perseverance, 1)), Holdings.None);

        var option = PlanEvaluator.Option(Standard, plan, StrengthB);

        option.CanBuy.Should().BeTrue();
        option.Reasons.OfType<NotEnoughXp>().Should().BeEmpty();
    }

    [Test]
    public void a_second_teaching_carries_the_one_per_event_warning()
    {
        var plan = Plan.Existing(30m, Holdings.Of((Perseverance, 1)), Holdings.Of((StrengthB, 1)));

        PlanEvaluator.Option(Standard, plan, TeachingExpertise).Reasons.OfType<MoreThanOneTeaching>().Should().ContainSingle();
    }

    [Test]
    public void a_deprecated_skill_is_not_offered()
    {
        var option = PlanEvaluator.Option(Standard, Plan.NewCharacter(Holdings.None), OldSkill);

        option.Offered.Should().BeFalse();
        option.CanBuy.Should().BeFalse();
    }

    [Test]
    public void a_skill_the_book_does_not_know_is_not_offered()
        => PlanEvaluator.Option(Standard, Plan.NewCharacter(Holdings.None), Unknown).Offered.Should().BeFalse();
}
