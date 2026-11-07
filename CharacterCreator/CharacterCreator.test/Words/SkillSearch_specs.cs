using CharacterCreator.core.Content;
using CharacterCreator.infrastructure.Content;
using CharacterCreator.WebApp.Client.Words;

namespace CharacterCreator.test.Words;

/// <summary>The picker's search (UX-REVIEW.md P8), on the 2026 V07 content.</summary>
public sealed class SkillSearch_specs
{
    private static readonly ContentBook Book = new EmbeddedContentSource().LoadAsync("2026-v07", CancellationToken.None).Result.Value;
    private static readonly SkillSearch Dutch = new (Ui.Dutch, Book.Legacy);
    private static readonly SkillSearch English = new (new Ui(Language.English), Book.Legacy);

    [Test]
    public void accents_and_case_dont_matter()
        => Hits(Dutch, "MAGIER").Should().Contain(["mage-a-white", "mage-b-fire"]);

    [Test]
    public void a_spell_finds_the_skills_that_teach_it_and_says_so()
    {
        Hits(Dutch, "heal wounds").Should().Contain(["healer-a-elemental", "healer-a-spiritual"]);
        Dutch.Match(Skill("healer-a-spiritual"), "heal wounds").Via.Should().Be("via Heal Wounds");
    }

    [Test]
    public void thorfgins_old_name_finds_the_skill_it_became()
        => English.Match(Skill("glyph-use"), "glyph gebruiken")
            .Should().BeEquivalentTo(new { Matched = true, Via = "old name: Glyph Gebruiken" }, "today it is Glyphs Gebruiken");

    [Test]
    public void a_recipe_finds_its_skill()
        => Dutch.Match(Skill("brew-healing-potions-a"), "heal scrape").Via.Should().Be("via Heal scrape");

    [Test]
    public void a_match_on_the_name_needs_no_explaining()
        => Dutch.Match(Skill("healer-a-spiritual"), "genezing").Should().Be(SearchHit.Direct);

    [Test]
    public void two_letters_only_look_at_names()
    {
        Dutch.Match(Skill("healer-a-spiritual"), "wo").Should().Be(SearchHit.None, "the spells start at three letters");
        Dutch.Match(Skill("healer-a-spiritual"), "wou").Via.Should().StartWith("via ", "three letters do search the spells");
    }

    [Test]
    public void an_empty_search_matches_everything()
        => Hits(Dutch, "  ").Should().HaveCount(Book.Skills.Count);

    [Test]
    public void folding_takes_the_accents_off_dutch_words()
        => SkillSearch.Fold(" Druïde Magiër Ëlf ").Should().Be("druide magier elf");

    private static List<string> Hits(SkillSearch search, string query)
        => [.. Book.Skills.Where(skill => search.Match(skill, query).Matched).Select(skill => skill.Key.ToString())];

    private static SkillContent Skill(string key) => Book.Skills.Single(skill => skill.Key.ToString() == key);
}
