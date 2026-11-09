namespace DatumPrikker.test.Commands;

public sealed class CreateAndClosePoll_specs
{
    private readonly InMemoryPollStore polls = new InMemoryPollStore();

    [Test]
    public async Task a_made_poll_is_stored_with_a_well_formed_token()
    {
        var handler = new CreatePollHandler(polls, NullLogger<CreatePollHandler>.Instance);

        var created = await handler.HandleAsync(new CreatePoll { Draft = Fixtures.Draft(), Owner = Fixtures.Owner });

        created.IsValid.Should().BeTrue();
        ShareToken.IsWellFormed(created.Value.ShareToken).Should().BeTrue();
        polls.All.Should().ContainSingle().Which.Id.Should().Be(created.Value.Id);
    }

    [Test]
    public async Task an_invalid_draft_stores_nothing()
    {
        var handler = new CreatePollHandler(polls, NullLogger<CreatePollHandler>.Instance);

        var created = await handler.HandleAsync(new CreatePoll { Draft = Fixtures.Draft() with { Title = string.Empty }, Owner = Fixtures.Owner });

        created.IsValid.Should().BeFalse();
        polls.All.Should().BeEmpty();
    }

    [Test]
    public async Task closing_is_for_the_owner_only()
    {
        var poll = Fixtures.Poll();
        await polls.SaveAsync(poll);
        var handler = new ClosePollHandler(polls, NullLogger<ClosePollHandler>.Instance);

        (await handler.HandleAsync(new ClosePoll { ShareToken = poll.ShareToken, By = Fixtures.Stranger })).IsValid.Should().BeFalse();
        polls.All.Single().IsOpen.Should().BeTrue();

        (await handler.HandleAsync(new ClosePoll { ShareToken = poll.ShareToken, By = Fixtures.Owner })).IsValid.Should().BeTrue();
        polls.All.Single().IsOpen.Should().BeFalse();
    }
}
