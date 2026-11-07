using CharacterCreator.application.Rulesets;
using CharacterCreator.core.Content;
using CharacterCreator.core.Rules;
using CharacterCreator.infrastructure.Content;
using CharacterCreator.infrastructure.Void;
using CharacterCreator.WebApp.Client.Words;

namespace CharacterCreator.test.Words;

/// <summary>What the presets page shows of a preset at a glance (UX-REVIEW.md P9), on the 2026 V07 seed.</summary>
public sealed class PresetFacts_specs
{
    private static readonly ImportOutcome Seed = RulesetBuilder.Build(
        new EmbeddedContentSource().LoadAsync("2026-v07", CancellationToken.None).Result.Value,
        new EmbeddedOfflineSnapshot().LoadAsync("2026-v07", CancellationToken.None).Result.Value,
        null!,
        SeedRulesetHandler.FirstStart,
        new DateTimeOffset(2026, 11, 07, 09, 15, 00, TimeSpan.Zero));

    private static readonly ContentBook Content = Seed.Ruleset.ToContentBook().Value;
    private static readonly RuleBook Rules = Seed.Ruleset.ToRuleBook().Value;

    [TestCase("vechter-zwaar", new[] { PresetKind.Fighting })]
    [TestCase("paladijn", new[] { PresetKind.Fighting, PresetKind.Healing })]
    [TestCase("magier-ritualist", new[] { PresetKind.Magic, PresetKind.Ritual })]
    [TestCase("arts", new[] { PresetKind.Healing, PresetKind.Craft })]
    [TestCase("shamaan-druide", new[] { PresetKind.Magic })]
    public void a_preset_is_the_kinds_it_spends_at_least_4_xp_in(string key, PresetKind[] kinds)
        => PresetFacts.Kinds(Preset(key), Content, Rules).Should().Equal(kinds);

    [Test]
    public void every_preset_is_at_least_one_kind()
        => Content.Presets.Should().AllSatisfy(preset => PresetFacts.Kinds(preset, Content, Rules).Should().NotBeEmpty(preset.Key));

    [Test]
    public void the_defining_skills_are_the_dearest_in_the_presets_order()
        => PresetFacts.Defining(Preset("paladijn"), Rules, 4).Select(key => key.ToString())
            .Should().Equal("healer-a-spiritual", "paladin-spells-a", "shield-use", "armour-ii");

    [TestCase("paladijn", PresetKind.Fighting)]
    [TestCase("oorlogsmagier", PresetKind.Magic)]
    [TestCase("arts", PresetKind.Healing)]
    public void a_presets_main_kind_is_where_it_spends_the_most(string key, PresetKind kind)
        => PresetFacts.Primary(Preset(key), Content, Rules).Should().Be(kind);

    [Test]
    public void a_first_visit_offers_the_first_preset_of_each_main_kind()
        => PresetFacts.Starters(Content, Rules).Select(preset => preset.Key)
            .Should().Equal("vechter-licht", "oorlogsmagier", "genezer-elementair", "alchemist");

    [TestCase("vechten", PresetKind.Fighting)]
    [TestCase("RITUEEL", PresetKind.Ritual)]
    [TestCase("", PresetKind.None)]
    [TestCase("draken", PresetKind.None)]
    public void a_kind_is_found_by_its_slug(string slug, PresetKind kind)
        => PresetFacts.FromSlug(slug).Should().Be(kind);

    private static PresetContent Preset(string key) => Content.Presets.Single(preset => preset.Key == key);
}
