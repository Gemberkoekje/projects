using CharacterCreator.core.Content;
using CharacterCreator.core.Plans;
using CharacterCreator.infrastructure.Content;
using CharacterCreator.WebApp.Client.Words;

namespace CharacterCreator.test.Words;

/// <summary>The summary's spells and recipes, grouped by where they come from (UX-REVIEW.md P10).</summary>
public sealed class PlanSummary_specs
{
    private static readonly ContentBook Book = new EmbeddedContentSource().LoadAsync("2026-v07", CancellationToken.None).Result.Value;

    [TestCase("paladijn")]
    [TestCase("arts")]
    [TestCase("doodsaanbidder")]
    public void the_groups_hold_every_spell_and_recipe_exactly_once(string preset)
    {
        var plan = Plan(preset);

        PlanSummary.SpellGroups(Book, plan).SelectMany(group => group.Items).Select(spell => spell.Id)
            .Should().BeEquivalentTo(PlanSummary.Spells(Book, plan).Select(spell => spell.Id));
        PlanSummary.RecipeGroups(Book, plan).SelectMany(group => group.Items).Select(recipe => recipe.Id)
            .Should().BeEquivalentTo(PlanSummary.Recipes(Book, plan).Select(recipe => recipe.Id));
    }

    [Test]
    public void spells_sit_under_the_skill_that_brings_them()
        => PlanSummary.SpellGroups(Book, Plan("paladijn")).Select(group => group.Title.Nl)
            .Should().Equal("Genezingsspreuken A (spiritueel)", "Paladijnspreuken A");

    [Test]
    public void a_skill_without_spells_makes_no_group()
        => PlanSummary.SpellGroups(Book, Plan("paladijn")).Should().AllSatisfy(group => group.Items.Should().NotBeEmpty());

    [Test]
    public void common_recipes_say_they_are_common()
    {
        var titles = PlanSummary.RecipeGroups(Book, Plan("arts")).Select(group => group.Title.Nl).ToList();

        titles.Should().Contain(title => title.EndsWith(" (algemeen)", StringComparison.Ordinal));
    }

    private static Plan Plan(string key) => core.Plans.Plan.NewCharacter(PresetFacts.Holdings(Book.Presets.Single(preset => preset.Key == key)));
}
