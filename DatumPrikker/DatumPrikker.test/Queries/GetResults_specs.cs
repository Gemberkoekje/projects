namespace DatumPrikker.test.Queries;

public sealed class GetResults_specs
{
    private readonly InMemoryPollStore polls = new InMemoryPollStore();
    private readonly InMemoryAnswerStore answers = new InMemoryAnswerStore();

    private GetResultsHandler Handler => new GetResultsHandler(new GetPollHandler(polls), answers, NullLogger<GetResultsHandler>.Instance);

    [Test]
    public async Task the_owner_gets_the_tally()
    {
        var poll = Fixtures.Poll();
        await polls.SaveAsync(poll);
        await answers.SaveAsync(Fixtures.Answer("Carla", "carla@example.com", poll, Availability.Yes));

        var results = await Handler.HandleAsync(new GetResults { ShareToken = poll.ShareToken, By = Fixtures.Owner });

        results.IsValid.Should().BeTrue();
        results.Value.Answers.Should().ContainSingle();
    }

    [Test]
    public async Task anybody_else_is_refused()
    {
        var poll = Fixtures.Poll();
        await polls.SaveAsync(poll);

        (await Handler.HandleAsync(new GetResults { ShareToken = poll.ShareToken, By = Fixtures.Stranger }))
            .Errors().Should().Contain(GetResultsHandler.OwnerOnly);
        (await Handler.HandleAsync(new GetResults { ShareToken = poll.ShareToken, By = Account.Anonymous }))
            .Errors().Should().Contain(GetResultsHandler.OwnerOnly);
    }

    [TestCase("k7Gm2xQb")]
    [TestCase("niet-een-token")]
    public async Task an_unknown_or_malformed_token_is_not_found(string token)
    {
        await polls.SaveAsync(Fixtures.Poll());

        (await Handler.HandleAsync(new GetResults { ShareToken = token, By = Fixtures.Owner }))
            .Errors().Should().Contain(IPollStore.NotFound);
    }
}
