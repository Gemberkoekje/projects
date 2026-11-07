using CharacterCreator.core.Plans;
using CharacterCreator.core.Rules;
using CharacterCreator.core.Skills;
using static CharacterCreator.test.TestHelpers.Book;

namespace CharacterCreator.test.Rules;

/// <summary>"Add with prerequisites" and "remove these too" (DESIGN.md §3.2 and §5).</summary>
public sealed class ChainAndDependants_specs
{
    [Test]
    public void a_chain_of_single_prerequisites_is_added_deepest_first()
    {
        var chain = PlanEvaluator.Chain(Standard, Plan.NewCharacter(Holdings.None), ArmourIII);

        chain.IsComplete.Should().BeTrue();
        chain.Purchases.Should().Equal(Pairs((ArmourI, 1), (ArmourII, 1), (ArmourIII, 1)));
    }

    [Test]
    public void what_is_already_held_is_not_added_again()
    {
        var chain = PlanEvaluator.Chain(Standard, Plan.Existing(20m, Holdings.Of((ArmourI, 1)), Holdings.None), ArmourIII);

        chain.Purchases.Should().Equal(Pairs((ArmourII, 1), (ArmourIII, 1)));
    }

    [Test]
    public void a_level_adds_the_purchases_still_short()
    {
        var chain = PlanEvaluator.Chain(Standard, Plan.Free(30m, Holdings.Of((ElementalRitualism, 1))), RitualMaster);

        chain.Purchases.Should().Equal(Pairs((ElementalRitualism, 2), (RitualMaster, 1)));
    }

    [Test]
    public void a_real_choice_is_left_to_the_player_and_the_rest_is_still_added()
    {
        var chain = PlanEvaluator.Chain(Standard, Plan.NewCharacter(Holdings.None), Paladin);

        chain.IsComplete.Should().BeFalse();
        chain.Unresolved.Should().ContainSingle().Which.Should().Be(RequirementGroup.Any(1, HealerAElemental, HealerASpiritual));
        chain.Purchases.Should().Equal(Pairs((ArmourI, 1), (Paladin, 1)));
    }

    [Test]
    public void xp_in_a_set_is_left_to_the_player()
    {
        var chain = PlanEvaluator.Chain(Standard, Plan.Free(30m, Holdings.None), BrewExpertise);

        chain.Unresolved.Should().ContainSingle().Which.Kind.Should().Be(RequirementKind.Points);
    }

    [Test]
    public void removing_the_first_of_a_chain_breaks_the_rest()
    {
        var plan = Plan.NewCharacter(Holdings.Of((ArmourI, 1), (ArmourII, 1), (ArmourIII, 1), (Literacy, 1)));

        PlanEvaluator.Dependants(Standard, plan, ArmourI).Should().Equal(ArmourII, ArmourIII);
    }

    [Test]
    public void removing_the_last_of_a_chain_breaks_nothing()
    {
        var plan = Plan.NewCharacter(Holdings.Of((ArmourI, 1), (ArmourII, 1), (ArmourIII, 1)));

        PlanEvaluator.Dependants(Standard, plan, ArmourIII).Should().BeEmpty();
    }

    [Test]
    public void xp_in_a_set_breaks_when_a_counted_skill_goes()
    {
        var plan = Plan.Free(30m, Holdings.Of((HerbLore, 1), (BrewPoisonA, 1), (BrewExpertise, 1)));

        PlanEvaluator.Dependants(Standard, plan, HerbLore).Should().Equal(BrewPoisonA, BrewExpertise);
    }

    [Test]
    public void taking_one_purchase_out_of_a_level_breaks_what_needs_the_level()
    {
        var plan = Plan.Free(30m, Holdings.Of((ElementalRitualism, 3), (RitualMaster, 1)));

        PlanEvaluator.Dependants(Standard, plan, ElementalRitualism).Should().Equal(RitualMaster);
    }

    [Test]
    public void a_skill_that_was_already_missing_a_prerequisite_is_not_blamed_on_the_removal()
    {
        var plan = Plan.NewCharacter(Holdings.Of((ArmourIII, 1), (Literacy, 1)));

        PlanEvaluator.Dependants(Standard, plan, Literacy).Should().BeEmpty();
    }

    [Test]
    public void held_skills_are_fixed_and_never_listed()
    {
        var plan = Plan.Existing(20m, Holdings.Of((ArmourII, 1)), Holdings.Of((ArmourI, 1), (ArmourIII, 1)));

        PlanEvaluator.Dependants(Standard, plan, ArmourI).Should().BeEmpty("armour II is held, so armour III still has what it needs");
    }

    private static KeyValuePair<SkillKey, int>[] Pairs(params (SkillKey Skill, int Times)[] pairs)
        => [.. pairs.Select(pair => new KeyValuePair<SkillKey, int>(pair.Skill, pair.Times))];
}
