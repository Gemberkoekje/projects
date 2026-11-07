using CharacterCreator.application.Ports;
using CharacterCreator.application.Rulesets;
using CharacterCreator.application.Void;
using CharacterCreator.infrastructure.Content;
using CharacterCreator.infrastructure.Void;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Qowaiv;
using Qowaiv.Validation.Abstractions;

namespace CharacterCreator.test.Rulesets;

/// <summary>Seeding, previewing and publishing rulesets (DESIGN.md §4.2), against VOID and the store.</summary>
public sealed class Ruleset_handler_specs
{
    private static readonly DateTime Monday = new (2026, 11, 09, 20, 00, 00, DateTimeKind.Utc);

    private readonly EmbeddedContentSource content = new ();
    private readonly EmbeddedOfflineSnapshot offline = new ();
    private InMemoryRulesetStore store = null!;
    private IVoidClient voids = null!;
    private IDisposable clock = null!;

    [SetUp]
    public void Arrange()
    {
        store = new InMemoryRulesetStore();
        voids = Substitute.For<IVoidClient>();
        voids.Source.Returns("https://test-void-api.the-vortex.nl/");
        clock = Clock.SetTimeForCurrentContext(() => Monday);
    }

    [TearDown]
    public void Restore() => clock.Dispose();

    [Test]
    public async Task the_first_start_publishes_the_offline_snapshot()
    {
        var seeded = await Seed().HandleAsync(CancellationToken.None);

        seeded.IsValid.Should().BeTrue();
        var ruleset = store.All.Should().ContainSingle().Subject;
        ruleset.Id.Should().Be("2026-v07@20261109T200000Z");
        ruleset.Provisional.Should().BeTrue();
        ruleset.PublishedBy.Should().Be(SeedRulesetHandler.FirstStart);
    }

    [Test]
    public async Task a_later_start_leaves_the_published_ruleset_alone()
    {
        await Seed().HandleAsync(CancellationToken.None);

        using (Clock.SetTimeForCurrentContext(() => Monday.AddDays(1)))
        {
            (await Seed().HandleAsync(CancellationToken.None)).IsValid.Should().BeTrue();
        }

        store.All.Should().ContainSingle();
    }

    [Test]
    public async Task without_a_snapshot_there_is_nothing_to_seed_and_it_says_why()
    {
        var none = Substitute.For<IOfflineVoidSnapshot>();
        none.LoadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.WithMessages<VoidSnapshot>(ValidationMessage.Error("There is no offline snapshot for ruleset '2026-v07'.")));

        var seeded = await new SeedRulesetHandler(content, none, store, NullLogger<SeedRulesetHandler>.Instance).HandleAsync(CancellationToken.None);

        seeded.IsValid.Should().BeFalse();
        store.All.Should().BeEmpty();
    }

    [Test]
    public async Task a_preview_reads_void_with_the_token_and_stores_nothing()
    {
        await GivenVoidAnswersWithTheBook();

        var outcome = await Preview().HandleAsync(new PreviewImport { AccessToken = "token-123", PublishedBy = "Sanne" }, CancellationToken.None);

        outcome.IsValid.Should().BeTrue();
        outcome.Value.Report.Offered.Should().Be(171);
        outcome.Value.Ruleset.Provisional.Should().BeFalse("these rules came from a VOID");
        await voids.Received(1).GetSkillsAsync("token-123", Arg.Any<CancellationToken>());
        store.All.Should().BeEmpty();
    }

    [Test]
    public async Task a_preview_reports_against_the_published_ruleset()
    {
        await Seed().HandleAsync(CancellationToken.None);
        await GivenVoidAnswersWithTheBook(costOf: ("literacy", 1));

        var outcome = await Preview().HandleAsync(new PreviewImport { AccessToken = "token", PublishedBy = "Sanne" }, CancellationToken.None);

        outcome.Value.Report.Changes.Should().ContainSingle()
            .Which.Should().Be(new RuleChange { Skill = new ("literacy"), Kind = RuleChangeKind.Cost, Before = "0", After = "1" });
    }

    [Test]
    public async Task when_void_cannot_be_read_the_preview_says_why()
    {
        voids.GetSkillsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.WithMessages<VoidSnapshot>(ValidationMessage.Error("VOID refused the sign-in (401).")));

        var outcome = await Preview().HandleAsync(new PreviewImport { AccessToken = "token", PublishedBy = "Sanne" }, CancellationToken.None);

        outcome.IsValid.Should().BeFalse();
        outcome.Messages.Single().Message.Should().Be("VOID refused the sign-in (401).");
    }

    [Test]
    public async Task publishing_stores_the_imported_ruleset_as_the_current_one()
    {
        await Seed().HandleAsync(CancellationToken.None);
        await GivenVoidAnswersWithTheBook();

        Result<ImportOutcome> published;
        using (Clock.SetTimeForCurrentContext(() => Monday.AddHours(1)))
        {
            published = await Publish().HandleAsync(new PublishRuleset { AccessToken = "token", PublishedBy = "Sanne" }, CancellationToken.None);
        }

        published.IsValid.Should().BeTrue();
        var current = (await store.CurrentAsync(CancellationToken.None)).Value;
        current.Id.Should().Be("2026-v07@20261109T210000Z");
        current.PublishedBy.Should().Be("Sanne");
        current.Source.Should().Be("https://test-void-api.the-vortex.nl/");
        current.Provisional.Should().BeFalse();
        store.All.Should().HaveCount(2, "a published ruleset is kept for the plans made on it");
    }

    [Test]
    public async Task a_ruleset_that_cannot_be_published_is_not_stored()
    {
        voids.GetSkillsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Result.For(new VoidSnapshot
        {
            Source = "https://test-void-api.the-vortex.nl/",
            TakenAt = DateTimeOffset.UnixEpoch,
            Synthesized = false,
            Skills = [],
        }));

        var published = await Publish().HandleAsync(new PublishRuleset { AccessToken = "token", PublishedBy = "Sanne" }, CancellationToken.None);

        published.IsValid.Should().BeFalse();
        published.Messages.Single().Message.Should().Contain("Not one book skill was found in VOID");
        store.All.Should().BeEmpty();
    }

    [Test]
    public async Task publishing_twice_in_the_same_second_keeps_the_first()
    {
        await GivenVoidAnswersWithTheBook();
        await Publish().HandleAsync(new PublishRuleset { AccessToken = "token", PublishedBy = "Sanne" }, CancellationToken.None);

        var again = await Publish().HandleAsync(new PublishRuleset { AccessToken = "token", PublishedBy = "Pim" }, CancellationToken.None);

        again.IsValid.Should().BeFalse();
        store.All.Should().ContainSingle().Which.PublishedBy.Should().Be("Sanne");
    }

    [Test]
    public async Task the_build_screen_reads_the_newest()
    {
        await Seed().HandleAsync(CancellationToken.None);

        var current = await new ReadCurrentRulesetHandler(store).HandleAsync(CancellationToken.None);

        current.Value.Should().BeSameAs(store.All.Single());
    }

    private SeedRulesetHandler Seed() => new (content, offline, store, NullLogger<SeedRulesetHandler>.Instance);

    private PreviewImportHandler Preview() => new (content, voids, store, NullLogger<PreviewImportHandler>.Instance);

    private PublishRulesetHandler Publish() => new (Preview(), store, NullLogger<PublishRulesetHandler>.Instance);

    /// <summary>VOID answers with the offline snapshot's book skills, as a real VOID would, optionally with one cost changed.</summary>
    private async Task GivenVoidAnswersWithTheBook((string Name, int Cost) costOf = default)
    {
        var book = (await offline.LoadAsync("2026-v07", CancellationToken.None)).Value;
        var answer = book with
        {
            Source = voids.Source,
            Synthesized = false,
            Skills = [.. book.Skills.Select(skill => skill.Name == costOf.Name ? skill with { Cost = costOf.Cost } : skill)],
        };
        voids.GetSkillsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Result.For(answer));
    }
}
