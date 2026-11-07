using System.Text.Json;
using System.Text.RegularExpressions;
using CharacterCreator.application.Rulesets;
using CharacterCreator.core.Rulesets;
using CharacterCreator.infrastructure.Content;
using CharacterCreator.infrastructure.Void;

namespace CharacterCreator.test.Rulesets;

/// <summary>
/// What the first start publishes: the 2026 V07 content with the offline snapshot shipped next to it
/// (DESIGN.md §4.2), and how that ruleset travels as JSON to the build screen.
/// </summary>
public sealed partial class Offline_seed_specs
{
    private static readonly ImportOutcome Seed = RulesetBuilder.Build(
        new EmbeddedContentSource().LoadAsync("2026-v07", CancellationToken.None).Result.Value,
        new EmbeddedOfflineSnapshot().LoadAsync("2026-v07", CancellationToken.None).Result.Value,
        null!,
        SeedRulesetHandler.FirstStart,
        new DateTimeOffset(2026, 11, 07, 09, 15, 00, TimeSpan.Zero));

    [Test]
    public void the_snapshot_says_it_is_synthesized_and_when()
    {
        var snapshot = new EmbeddedOfflineSnapshot().LoadAsync("2026-v07", CancellationToken.None).Result.Value;

        snapshot.Synthesized.Should().BeTrue();
        snapshot.TakenAt.Should().Be(new DateTimeOffset(2026, 11, 07, 00, 00, 00, TimeSpan.Zero));
        snapshot.Source.Should().StartWith("offline snapshot (2026-v07)");
    }

    [Test]
    public void there_is_no_snapshot_for_a_ruleset_without_content()
        => new EmbeddedOfflineSnapshot().LoadAsync("1999-v01", CancellationToken.None).Result.IsValid.Should().BeFalse();

    [Test]
    public void every_book_skill_is_offered_and_it_can_be_published()
    {
        Seed.Report.Errors.Should().BeEmpty();
        Seed.Report.Offered.Should().Be(171);
        Seed.Report.NotInVoid.Should().BeEmpty();
        Seed.Report.DuplicateVoidNames.Should().BeEmpty();
    }

    [Test]
    public void it_is_marked_provisional()
    {
        Seed.Ruleset.Provisional.Should().BeTrue("the numbers come from the public draft, not from a VOID");
        Seed.Ruleset.PublishedBy.Should().Be(SeedRulesetHandler.FirstStart);
    }

    [Test]
    public void the_snapshot_holds_book_skills_only()
    {
        Seed.Report.NotInBook.Should().BeEmpty("the repo is public: no SL-only skill may be in it");
        Seed.Report.HiddenPrerequisites.Should().BeEmpty();
    }

    [Test]
    public void its_costs_are_the_books()
        => Seed.Report.CostDifferences.Should().BeEmpty();

    [Test]
    public void the_engine_accepts_its_rules_and_content()
    {
        Seed.Ruleset.ToRuleBook().IsValid.Should().BeTrue();
        Seed.Ruleset.ToContentBook().IsValid.Should().BeTrue();
    }

    [Test]
    public void every_key_it_names_is_a_book_skill_or_a_hidden_id()
    {
        var book = Seed.Ruleset.Skills.Select(skill => skill.Key.ToString()).ToHashSet();
        var named = Seed.Ruleset.Rules.Select(rule => rule.Key)
            .Concat(Seed.Ruleset.Rules.SelectMany(rule => rule.Requirements).SelectMany(group => group.Skills))
            .Select(key => key.ToString());

        named.Should().AllSatisfy(key => (book.Contains(key) || HiddenKey().IsMatch(key)).Should().BeTrue(key));
    }

    [Test]
    public void it_survives_the_trip_to_the_build_screen_as_json()
    {
        var json = JsonSerializer.Serialize(Seed.Ruleset, RulesetJson.Options);

        var back = JsonSerializer.Deserialize<PublishedRuleset>(json, RulesetJson.Options)!;

        back.Id.Should().Be(Seed.Ruleset.Id);
        back.Rules.Should().Equal(Seed.Ruleset.Rules, (a, b) => a.Key == b.Key
            && a.Cost == b.Cost
            && a.Requirements.SequenceEqual(b.Requirements)
            && a.Energy.SequenceEqual(b.Energy));
        back.Offered().Should().Equal(Seed.Ruleset.Offered());
        back.ToRuleBook().IsValid.Should().BeTrue();
        back.ToContentBook().IsValid.Should().BeTrue();
    }

    [Test]
    public void the_json_is_camel_case_with_enums_by_name()
    {
        var json = JsonSerializer.Serialize(Seed.Ruleset, RulesetJson.Options);

        json.Should().Contain("\"contentRuleset\":\"2026-v07\"")
            .And.Contain("\"kind\":\"Any\"")
            .And.NotContain("\"ContentRuleset\"");
    }

    [GeneratedRegex("^hidden-[0-9]+$")]
    private static partial Regex HiddenKey();
}
