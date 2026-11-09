namespace DatumPrikker.test.Commands;

/// <summary>D4: an answer given while signed in can't be replaced anonymously.</summary>
public sealed class ClaimedAnswer_specs
{
    private readonly InMemoryPollStore polls = new InMemoryPollStore();
    private readonly InMemoryAnswerStore answers = new InMemoryAnswerStore();

    private GiveAnswerHandler Handler => new GiveAnswerHandler(polls, answers, NullLogger<GiveAnswerHandler>.Instance);

    [Test]
    public void only_a_claimed_answer_refuses_anonymous_replacement()
    {
        var poll = Fixtures.Poll();
        var anonymous = Fixtures.Answer("Bea", "bea@example.com", poll, Availability.Yes);
        var claimed = anonymous with { AccountId = Fixtures.Stranger.Id };

        anonymous.ReplaceableBy(Account.Anonymous).IsValid.Should().BeTrue();
        claimed.ReplaceableBy(Fixtures.Stranger).IsValid.Should().BeTrue();
        claimed.ReplaceableBy(Account.Anonymous).Errors().Should().Contain(Answer.ClaimedMessage);
    }

    [Test]
    public async Task an_anonymous_answer_cannot_replace_one_given_signed_in()
    {
        var poll = await StoredPollAsync();
        await Handler.HandleAsync(new GiveAnswer { ShareToken = poll.ShareToken, Draft = Draft(poll, "bea@example.com", Availability.Yes), By = Fixtures.Stranger });

        var result = await Handler.HandleAsync(new GiveAnswer { ShareToken = poll.ShareToken, Draft = Draft(poll, "BEA@example.com", Availability.No) });

        result.Errors().Should().Contain(Answer.ClaimedMessage);
        answers.All.Single().ChoiceFor(poll.Options[0].Id).Should().Be(Availability.Yes);
    }

    [Test]
    public async Task signing_in_and_answering_again_claims_an_anonymous_answer()
    {
        var poll = await StoredPollAsync();
        await Handler.HandleAsync(new GiveAnswer { ShareToken = poll.ShareToken, Draft = Draft(poll, "bea@example.com", Availability.Yes) });

        var result = await Handler.HandleAsync(new GiveAnswer { ShareToken = poll.ShareToken, Draft = Draft(poll, "ignored@example.com", Availability.Maybe), By = Fixtures.Stranger });

        result.IsValid.Should().BeTrue();
        var stored = answers.All.Should().ContainSingle().Subject;
        stored.IsClaimed.Should().BeTrue();
        stored.ChoiceFor(poll.Options[0].Id).Should().Be(Availability.Maybe);
    }

    [Test]
    public async Task another_account_with_the_same_email_may_replace_a_claimed_answer()
    {
        var poll = await StoredPollAsync();
        var sameEmailOtherProvider = Fixtures.Stranger with { Id = "google:333" };
        await Handler.HandleAsync(new GiveAnswer { ShareToken = poll.ShareToken, Draft = Draft(poll, "bea@example.com", Availability.Yes), By = Fixtures.Stranger });

        var result = await Handler.HandleAsync(new GiveAnswer { ShareToken = poll.ShareToken, Draft = Draft(poll, "bea@example.com", Availability.No), By = sameEmailOtherProvider });

        result.IsValid.Should().BeTrue();
        answers.All.Single().AccountId.Should().Be("google:333");
    }

    private async Task<Poll> StoredPollAsync()
    {
        var poll = Fixtures.Poll();
        await polls.SaveAsync(poll);
        return poll;
    }

    private static AnswerDraft Draft(Poll poll, string email, Availability first) => Fixtures.AnswerFrom("Bea", email, poll, first);
}
