namespace DatumPrikker.test.Commands;

public sealed class GiveAnswer_specs
{
    private readonly InMemoryPollStore polls = new InMemoryPollStore();
    private readonly InMemoryAnswerStore answers = new InMemoryAnswerStore();

    [Test]
    public async Task answering_again_with_the_same_email_replaces_the_answer_but_keeps_when_it_was_first_given()
    {
        var poll = Fixtures.Poll();
        await polls.SaveAsync(poll);
        var handler = new GiveAnswerHandler(polls, answers, NullLogger<GiveAnswerHandler>.Instance);
        var first = Fixtures.Now;
        var second = first.AddHours(2);

        using (Clock.SetTimeForCurrentContext(() => first))
        {
            (await handler.HandleAsync(Give(poll, "carla@example.com", Availability.Yes))).IsValid.Should().BeTrue();
        }

        using (Clock.SetTimeForCurrentContext(() => second))
        {
            (await handler.HandleAsync(Give(poll, "CARLA@example.com", Availability.No))).IsValid.Should().BeTrue();
        }

        var stored = answers.All.Should().ContainSingle().Subject;
        stored.ChoiceFor(poll.Options[0].Id).Should().Be(Availability.No);
        stored.CreatedOn.Should().Be(first);
        stored.UpdatedOn.Should().Be(second);
    }

    [Test]
    public async Task different_emails_are_different_respondents()
    {
        var poll = Fixtures.Poll();
        await polls.SaveAsync(poll);
        var handler = new GiveAnswerHandler(polls, answers, NullLogger<GiveAnswerHandler>.Instance);

        await handler.HandleAsync(Give(poll, "carla@example.com", Availability.Yes));
        await handler.HandleAsync(Give(poll, "dirk@example.com", Availability.Maybe));

        answers.All.Should().HaveCount(2);
    }

    [Test]
    public async Task an_unknown_poll_is_not_found()
    {
        var handler = new GiveAnswerHandler(polls, answers, NullLogger<GiveAnswerHandler>.Instance);

        var result = await handler.HandleAsync(Give(Fixtures.Poll(), "carla@example.com", Availability.Yes));

        result.Errors().Should().Contain(IPollStore.NotFound);
        answers.All.Should().BeEmpty();
    }

    private static GiveAnswer Give(Poll poll, string email, Availability first) => new GiveAnswer
    {
        ShareToken = poll.ShareToken,
        Draft = Fixtures.AnswerFrom("Carla", email, poll, first),
    };
}
