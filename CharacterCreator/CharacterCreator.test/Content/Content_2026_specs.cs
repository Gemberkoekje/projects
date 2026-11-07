using CharacterCreator.core.Content;
using CharacterCreator.core.Skills;
using CharacterCreator.infrastructure.Content;

namespace CharacterCreator.test.Content;

/// <summary>
/// The 2026 V07 content as committed (DESIGN.md §4.1): made by scripts/import-thorfgin-content.py,
/// checked against the 2026 PDFs by scripts/check-against-rulebook.py.
/// </summary>
public sealed class Content_2026_specs
{
    private static readonly ContentBook Book = new EmbeddedContentSource().LoadAsync("2026-v07", CancellationToken.None).Result.Value;

    [Test]
    public void the_assembly_carries_the_2026_v07_content()
        => new EmbeddedContentSource().Rulesets.Should().Contain("2026-v07");

    [Test]
    public void it_follows_the_2026_v07_book()
    {
        Book.Ruleset.Should().Be("2026-v07");
        Book.Book.Should().Contain("2026 V07");
    }

    [Test]
    public void it_offers_one_skill_per_void_skill_in_the_book()
        => Book.Skills.Should().HaveCount(171, "VOID's own loader matched 171 of its skills to the 2026 book");

    [Test]
    public void every_skill_has_a_key_a_void_name_and_both_names()
        => Book.Skills.Should().AllSatisfy(skill =>
        {
            skill.Key.IsEmpty.Should().BeFalse();
            skill.Void.Should().NotBeNullOrWhiteSpace();
            skill.Name.Nl.Should().NotBeNullOrWhiteSpace();
            skill.Name.En.Should().NotBeNullOrWhiteSpace();
            skill.Category.Nl.Should().NotBeNullOrWhiteSpace();
        });

    [Test]
    public void every_page_is_from_the_2026_book()
        => Book.Skills.Should().AllSatisfy(skill =>
        {
            skill.PageFrom.Should().Be("2026-v07");
            skill.Page.Should().BeInRange(11, 75, "the skills chapters of the 2026 book");
            skill.BookCost.Should().BeGreaterThanOrEqualTo(0, "every skill was found with the points it costs");
        });

    [Test]
    public void a_skill_void_splits_is_offered_twice_with_the_books_text_on_both()
    {
        Book.TryGetByVoidName("healer A (elemental)", out var elemental).Should().BeTrue();
        Book.TryGetByVoidName("Healer A (Spiritual)", out var spiritual).Should().BeTrue("VOID names match ignoring case, as VOID's loader matches");

        elemental.Name.Nl.Should().Be("Genezingsspreuken A (elementair)");
        spiritual.Name.En.Should().Be("Healing Spells A (spiritual)");
        elemental.Book.Should().Equal("Genezingsspreuken A");
        elemental.Spells.Should().NotBeEmpty();
    }

    [Test]
    public void spells_and_recipes_came_along()
    {
        Book.Skills.Sum(skill => skill.Spells.Count).Should().BeGreaterThan(200);
        Book.Skills.Sum(skill => skill.Recipes.Count).Should().BeGreaterThan(100);
        Book.CommonRecipes.Should().NotBeEmpty();
    }

    [Test]
    public void a_loresheet_points_at_the_vortex_website()
        => Book.Skills.Where(skill => skill.Loresheet.Exists).Should().NotBeEmpty()
            .And.AllSatisfy(skill => skill.Loresheet.Url.Should().StartWith("https://the-vortex.nl/wp-content/uploads/"));

    [Test]
    public void every_preset_names_skills_that_are_in_the_content()
    {
        Book.Presets.Should().HaveCount(15);
        Book.Presets.SelectMany(preset => preset.Skills).Should().AllSatisfy(skill => Book.TryGet(skill.Key, out _).Should().BeTrue());
    }

    [Test]
    public void a_free_skill_costs_zero_in_the_book()
    {
        Book.TryGet(new SkillKey("literacy"), out var literacy).Should().BeTrue();
        literacy.BookCost.Should().Be(0);
    }

    [Test]
    public void an_unknown_ruleset_has_no_content()
    {
        var result = new EmbeddedContentSource().LoadAsync("1999-v01", CancellationToken.None).Result;

        result.IsValid.Should().BeFalse();
    }
}
