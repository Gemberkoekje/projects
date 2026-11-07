using CharacterCreator.core.Plans;
using CharacterCreator.core.Rules;

namespace CharacterCreator.test.Rules;

/// <summary>The picker's states for every skill at once (DESIGN.md §5), the same as one at a time.</summary>
public sealed class Options_specs
{
    private static IEnumerable<Plan> Plans()
    {
        yield return Plan.NewCharacter(Holdings.None);
        yield return Plan.NewCharacter(Holdings.Of((Book.ArmourI, 1), (Book.HealerAElemental, 1), (Book.ElementalRitualism, 1)));
        yield return Plan.Free(9m, Holdings.Of((Book.HerbLore, 1), (Book.BrewPoisonA, 1), (Book.LifeforceB, 1)));
        yield return Plan.Existing(30m, Holdings.Of((Book.ElementalRitualism, 3)), Holdings.Of((Book.RitualMaster, 1), (Book.TeachingExpertise, 1)));
    }

    [TestCaseSource(nameof(Plans))]
    public void every_skills_option_matches_asking_for_it_alone(Plan plan)
    {
        var skills = Book.Standard.Skills.Select(skill => skill.Key).Append(Book.Unknown).ToList();

        var options = PlanEvaluator.Options(Book.Standard, plan, skills);

        options.Keys.Should().BeEquivalentTo(skills);
        foreach (var skill in skills)
        {
            var alone = PlanEvaluator.Option(Book.Standard, plan, skill);
            options[skill].CanBuy.Should().Be(alone.CanBuy, skill.ToString());
            options[skill].Offered.Should().Be(alone.Offered, skill.ToString());
            options[skill].Reasons.Should().Equal(alone.Reasons, skill.ToString());
        }
    }

    [Test]
    public void a_skill_asked_twice_is_answered_once()
        => PlanEvaluator.Options(Book.Standard, Plan.NewCharacter(Holdings.None), [Book.Literacy, Book.Literacy]).Should().ContainSingle();
}
