namespace DatumPrikker.test.Queries;

public sealed class ListAppointments_specs
{
    private readonly InMemoryPollStore polls = new InMemoryPollStore();
    private readonly InMemoryAnswerStore answers = new InMemoryAnswerStore();

    private ListAppointmentsHandler Handler => new ListAppointmentsHandler(polls, answers);

    [Test]
    public async Task somebody_anonymous_has_no_afspraken()
    {
        await polls.SaveAsync(Fixtures.Poll());

        (await Handler.HandleAsync(new ListAppointments())).Should().BeEmpty();
    }

    [Test]
    public async Task polls_made_and_polls_answered_are_merged_once_each()
    {
        var mine = await PollAsync("k7Gm2xQa", Fixtures.Owner);
        var theirs = await PollAsync("k7Gm2xQb", Fixtures.Stranger);
        var unrelated = await PollAsync("k7Gm2xQc", Fixtures.Stranger);
        await answers.SaveAsync(Fixtures.Answer("Arie", "arie@example.com", mine, Availability.Yes));
        await answers.SaveAsync(Fixtures.Answer("Arie", "arie@example.com", theirs, Availability.Maybe));
        await answers.SaveAsync(Fixtures.Answer("Carla", "carla@example.com", mine, Availability.No));
        await answers.SaveAsync(Fixtures.Answer("Carla", "carla@example.com", unrelated, Availability.No));

        var afspraken = await Handler.HandleAsync(new ListAppointments { Account = Fixtures.Owner });

        afspraken.Select(afspraak => afspraak.Poll.Id).Should().BeEquivalentTo([mine.Id, theirs.Id]);

        var made = afspraken.Single(afspraak => afspraak.Poll.Id == mine.Id);
        made.IsOwner.Should().BeTrue();
        made.HasAnswered.Should().BeTrue();
        made.AnswerCount.Should().Be(2);

        var answered = afspraken.Single(afspraak => afspraak.Poll.Id == theirs.Id);
        answered.IsOwner.Should().BeFalse();
        answered.MyAnswer.ChoiceFor(theirs.Options[0].Id).Should().Be(Availability.Maybe);
    }

    [Test]
    public async Task open_polls_come_first_then_the_latest_activity()
    {
        var old = await PollAsync("k7Gm2xQa", Fixtures.Owner, Fixtures.Now.AddDays(-10));
        var closed = await PollAsync("k7Gm2xQb", Fixtures.Owner, Fixtures.Now);
        await polls.SaveAsync(closed.Close(Fixtures.Owner, Fixtures.Now).Value);
        var answeredRecently = await PollAsync("k7Gm2xQc", Fixtures.Stranger, Fixtures.Now.AddDays(-20));
        await answers.SaveAsync(Fixtures.Answer("Arie", "arie@example.com", answeredRecently, Availability.Yes) with { UpdatedOn = Fixtures.Now.AddDays(-1) });

        var afspraken = await Handler.HandleAsync(new ListAppointments { Account = Fixtures.Owner });

        afspraken.Select(afspraak => afspraak.Poll.ShareToken).Should().Equal(answeredRecently.ShareToken, old.ShareToken, closed.ShareToken);
    }

    [Test]
    public async Task without_an_email_only_the_polls_made_count()
    {
        var mine = await PollAsync("k7Gm2xQa", Fixtures.Owner);
        var theirs = await PollAsync("k7Gm2xQb", Fixtures.Stranger);
        await answers.SaveAsync(Fixtures.Answer("Arie", "arie@example.com", theirs, Availability.Yes));

        var afspraken = await Handler.HandleAsync(new ListAppointments { Account = Fixtures.Owner with { Email = string.Empty } });

        afspraken.Should().ContainSingle().Which.Poll.Id.Should().Be(mine.Id);
    }

    [Test]
    public async Task an_earlier_answer_is_found_by_any_spelling_of_the_email()
    {
        var poll = await PollAsync("k7Gm2xQa", Fixtures.Owner);
        await answers.SaveAsync(Fixtures.Answer("Carla", "carla@example.com", poll, Availability.Yes));
        var handler = new GetAnswerHandler(answers);

        (await handler.HandleAsync(new GetAnswer { PollId = poll.Id, Email = " Carla@Example.com " })).IsValid.Should().BeTrue();
        (await handler.HandleAsync(new GetAnswer { PollId = poll.Id, Email = "dirk@example.com" })).IsValid.Should().BeFalse();
        (await handler.HandleAsync(new GetAnswer { PollId = poll.Id, Email = "geen-adres" })).IsValid.Should().BeFalse();
    }

    private async Task<Poll> PollAsync(string token, Account owner) => await PollAsync(token, owner, Fixtures.Now);

    private async Task<Poll> PollAsync(string token, Account owner, DateTime createdOn)
    {
        var poll = Poll.Create(Fixtures.Draft(), owner, token, createdOn).Value;
        await polls.SaveAsync(poll);
        return poll;
    }
}
