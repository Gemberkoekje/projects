using System.Text.Json;
using CharacterCreator.application.Rulesets;
using CharacterCreator.core.Rulesets;
using CharacterCreator.infrastructure.Content;
using CharacterCreator.infrastructure.Void;
using CharacterCreator.WebApp.Client.Services;

namespace CharacterCreator.test.Rulesets;

/// <summary>
/// The compile-time JSON metadata the trimmed build screen reads the ruleset with: no reflection,
/// so nothing the trimmer removes can break it.
/// </summary>
public sealed class RulesetJsonContext_specs
{
    private static readonly PublishedRuleset Seed = RulesetBuilder.Build(
        new EmbeddedContentSource().LoadAsync("2026-v07", CancellationToken.None).Result.Value,
        new EmbeddedOfflineSnapshot().LoadAsync("2026-v07", CancellationToken.None).Result.Value,
        null!,
        SeedRulesetHandler.FirstStart,
        new DateTimeOffset(2026, 11, 07, 09, 15, 00, TimeSpan.Zero)).Ruleset;

    [Test]
    public void a_ruleset_survives_the_round_trip_without_reflection()
    {
        var json = JsonSerializer.Serialize(Seed, RulesetJsonContext.Default.PublishedRuleset);

        var back = JsonSerializer.Deserialize(json, RulesetJsonContext.Default.PublishedRuleset)!;

        back.Rules.Should().Equal(Seed.Rules, (a, b) => a.Key == b.Key && a.Cost == b.Cost && a.Requirements.SequenceEqual(b.Requirements) && a.Energy.SequenceEqual(b.Energy));
        back.Legacy.Should().BeEquivalentTo(Seed.Legacy);
        back.Offered().Should().Equal(Seed.Offered());
    }

    [Test]
    public void it_writes_what_the_api_writes()
        => JsonSerializer.Serialize(Seed, RulesetJsonContext.Default.PublishedRuleset)
            .Should().Be(JsonSerializer.Serialize(Seed, RulesetJson.Options));

    [Test]
    public void computed_members_stay_out_of_the_json()
        => JsonSerializer.Serialize(Seed, RulesetJsonContext.Default.PublishedRuleset)
            .Should().NotContain("\"isExtra\"").And.NotContain("\"exists\"");

    [Test]
    public void a_ruleset_stored_before_the_legacy_map_still_reads()
    {
        var json = JsonSerializer.Serialize(Seed with { Legacy = [] }, RulesetJsonContext.Default.PublishedRuleset).Replace(",\"legacy\":[]", string.Empty, StringComparison.Ordinal);

        var back = JsonSerializer.Deserialize(json, RulesetJsonContext.Default.PublishedRuleset)!;

        json.Should().NotContain("legacy");
        back.Legacy.Should().BeEmpty();
    }

    [Test]
    public void the_build_screen_gets_a_rule_book_and_content_from_it()
    {
        var loaded = RulesetClient.Prepare(Seed);

        loaded.IsValid.Should().BeTrue();
        loaded.Value.Offered.Should().HaveCount(171);
        loaded.Value.Content.Legacy.Should().HaveCount(182);
    }
}
