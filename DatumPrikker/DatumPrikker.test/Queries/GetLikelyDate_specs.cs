using System.Text.Json;

namespace DatumPrikker.test.Queries;

/// <summary>D21: respondents see the most likely date, only after answering, and never the answers.</summary>
public sealed class GetLikelyDate_specs
{
    private const Availability Ja = Availability.Yes;
    private const Availability Misschien = Availability.Maybe;
    private const Availability Nee = Availability.No;

    private readonly InMemoryPollStore polls = new InMemoryPollStore();
    private readonly InMemoryAnswerStore answers = new InMemoryAnswerStore();
    private readonly Poll poll = Fixtures.Poll(Fixtures.Evening(12), Fixtures.Evening(13), Fixtures.WholeDay(14));

    private GetLikelyDateHandler Handler => new GetLikelyDateHandler(new GetPollHandler(polls), answers);

    [SetUp]
    public async Task StorePoll() => await polls.SaveAsync(poll);

    [Test]
    public async Task it_shows_only_once_you_answered_yourself()
    {
        await answers.SaveAsync(Fixtures.Answer("Carla", "carla@example.com", poll, Ja, Nee, Nee));

        (await Ask("dirk@example.com")).Errors().Should().Contain(GetLikelyDateHandler.AnswerFirst);
        (await Ask(string.Empty)).Errors().Should().Contain(GetLikelyDateHandler.AnswerFirst);
        (await Ask("carla@example.com")).IsValid.Should().BeTrue();
    }

    [Test]
    public async Task any_spelling_of_your_address_finds_your_answer()
    {
        await answers.SaveAsync(Fixtures.Answer("Carla", "carla@example.com", poll, Ja, Nee, Nee));

        (await Ask("  CARLA@Example.com ")).IsValid.Should().BeTrue();
    }

    [Test]
    public async Task the_leading_date_has_the_most_yes_then_the_most_maybe()
    {
        await answers.SaveAsync(Fixtures.Answer("Carla", "carla@example.com", poll, Ja, Ja, Nee));
        await answers.SaveAsync(Fixtures.Answer("Dirk", "dirk@example.com", poll, Ja, Ja, Ja));
        await answers.SaveAsync(Fixtures.Answer("Eva", "eva@example.com", poll, Nee, Misschien, Nee));

        var likely = (await Ask("carla@example.com")).Value;

        likely.Leading.Should().ContainSingle();
        likely.Leading[0].Option.Date.Should().Be(new DateOnly(2026, 12, 13));
        likely.Leading[0].Yes.Should().Be(2);
        likely.Leading[0].Maybe.Should().Be(1);
        likely.AnswerCount.Should().Be(3);
        likely.IsTie.Should().BeFalse();
    }

    [Test]
    public async Task a_tie_shows_every_leading_date_in_date_order()
    {
        await answers.SaveAsync(Fixtures.Answer("Carla", "carla@example.com", poll, Ja, Ja, Nee));
        await answers.SaveAsync(Fixtures.Answer("Dirk", "dirk@example.com", poll, Misschien, Misschien, Ja));

        var likely = (await Ask("dirk@example.com")).Value;

        likely.Leading.Select(tally => tally.Option.Date.Day).Should().Equal(12, 13);
        likely.IsTie.Should().BeTrue();
        likely.AnswerCount.Should().Be(2);
    }

    [Test]
    public async Task while_nobody_said_yes_no_date_leads()
    {
        await answers.SaveAsync(Fixtures.Answer("Carla", "carla@example.com", poll, Misschien, Nee, Misschien));

        var likely = await Ask("carla@example.com");

        likely.IsValid.Should().BeTrue();
        likely.Value.Leading.Should().BeEmpty();
        likely.Value.AnswerCount.Should().Be(1);
    }

    [Test]
    public async Task it_still_shows_once_the_poll_is_closed()
    {
        await answers.SaveAsync(Fixtures.Answer("Carla", "carla@example.com", poll, Ja, Nee, Nee));
        await polls.SaveAsync(poll.Close(Fixtures.Owner, Fixtures.Now).Value);

        (await Ask("carla@example.com")).Value.Leading.Should().ContainSingle();
    }

    [Test]
    public async Task it_gives_away_no_names_addresses_or_comments()
    {
        var withComment = Fixtures.AnswerFrom("Carla Visser", "carla@example.com", poll, Ja, Ja, Nee) with { Comment = "Ik neem taart mee" };
        await answers.SaveAsync(Answer.Give(poll, withComment, Account.Anonymous, Fixtures.Now).Value);
        await answers.SaveAsync(Fixtures.Answer("Dirk de Vries", "dirk@example.com", poll, Ja, Nee, Misschien));

        var json = JsonSerializer.Serialize((await Ask("dirk@example.com")).Value);

        json.Should().NotContain("Carla").And.NotContain("Dirk").And.NotContain("@example.com").And.NotContain("taart");
    }

    [TestCase("k7Gm2xQb")]
    [TestCase("niet-een-token")]
    public async Task an_unknown_or_malformed_token_is_not_found(string token)
    {
        (await Handler.HandleAsync(new GetLikelyDate { ShareToken = token, Email = "carla@example.com" }))
            .Errors().Should().Contain(IPollStore.NotFound);
    }

    private Task<Result<LikelyDate>> Ask(string email)
        => Handler.HandleAsync(new GetLikelyDate { ShareToken = poll.ShareToken, Email = email });
}
