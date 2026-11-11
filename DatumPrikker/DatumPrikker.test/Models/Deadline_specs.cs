namespace DatumPrikker.test.Models;

/// <summary>D22: an optional deadline, set when making the poll; past it, the poll closes by itself.</summary>
public sealed class Deadline_specs
{
    private static readonly DateOnly Friday = new DateOnly(2026, 12, 04);

    [Test]
    public void without_a_deadline_a_poll_stays_open_until_its_owner_closes_it()
    {
        var poll = Fixtures.Poll();

        poll.HasDeadline.Should().BeFalse();
        poll.IsOpenAt(Fixtures.Now.AddYears(5)).Should().BeTrue();
    }

    [Test]
    public void a_poll_closes_by_itself_at_its_deadline_in_amsterdam_time()
    {
        var poll = WithDeadline(Friday, new TimeOnly(18, 0));

        poll.Deadline.Should().Be(new DateTime(2026, 12, 04, 17, 00, 00, DateTimeKind.Utc), "18:00 in winter is 17:00 UTC");
        poll.DeadlineHasTime.Should().BeTrue();
        poll.IsOpenAt(poll.Deadline.AddSeconds(-1)).Should().BeTrue();
        poll.IsOpenAt(poll.Deadline).Should().BeFalse();
        poll.ClosedByDeadlineAt(poll.Deadline).Should().BeTrue();
    }

    [Test]
    public void a_deadline_without_a_time_closes_at_the_end_of_that_day()
    {
        var winter = WithDeadline(Friday);
        winter.DeadlineHasTime.Should().BeFalse();
        winter.IsOpenAt(new DateTime(2026, 12, 04, 22, 59, 59, DateTimeKind.Utc)).Should().BeTrue("23:59:59 on Friday in Amsterdam");
        winter.IsOpenAt(new DateTime(2026, 12, 04, 23, 00, 00, DateTimeKind.Utc)).Should().BeFalse("midnight in Amsterdam");

        var summer = WithDeadline(new DateOnly(2027, 06, 04));
        summer.Deadline.Should().Be(new DateTime(2027, 06, 04, 22, 00, 00, DateTimeKind.Utc), "summer time is UTC+2");
    }

    [Test]
    public void answering_after_the_deadline_is_refused()
    {
        var poll = WithDeadline(Friday, new TimeOnly(18, 0));
        var draft = Fixtures.AnswerFrom("Carla", "carla@example.com", poll, Availability.Yes);

        Answer.Give(poll, draft, Account.Anonymous, poll.Deadline.AddMinutes(-1)).IsValid.Should().BeTrue();
        Answer.Give(poll, draft, Account.Anonymous, poll.Deadline.AddMinutes(1))
            .Errors().Should().Contain("Deze prikker is gesloten en neemt geen antwoorden meer aan.");
    }

    [Test]
    public async Task the_answer_handler_refuses_once_the_deadline_passed()
    {
        var polls = new InMemoryPollStore();
        var answers = new InMemoryAnswerStore();
        var poll = WithDeadline(Friday, new TimeOnly(18, 0));
        await polls.SaveAsync(poll);
        var handler = new GiveAnswerHandler(polls, answers, NullLogger<GiveAnswerHandler>.Instance);
        var give = new GiveAnswer { ShareToken = poll.ShareToken, Draft = Fixtures.AnswerFrom("Carla", "carla@example.com", poll, Availability.Yes) };

        using (Clock.SetTimeForCurrentContext(() => poll.Deadline.AddSeconds(1)))
        {
            (await handler.HandleAsync(give)).IsValid.Should().BeFalse();
        }

        answers.All.Should().BeEmpty();
    }

    [Test]
    public void closing_by_hand_still_works_before_the_deadline()
    {
        var poll = WithDeadline(Friday, new TimeOnly(18, 0));
        var closedOn = Fixtures.Now.AddDays(1);

        var closed = poll.Close(Fixtures.Owner, closedOn).Value;

        closed.IsOpenAt(closedOn).Should().BeFalse();
        closed.ClosedOn.Should().Be(closedOn);
        closed.ClosedByDeadlineAt(poll.Deadline.AddDays(1)).Should().BeFalse("the owner closed it first");
    }

    [Test]
    public void closing_after_the_deadline_changes_nothing()
    {
        var poll = WithDeadline(Friday, new TimeOnly(18, 0));

        poll.Close(Fixtures.Owner, poll.Deadline.AddDays(1)).Value.ClosedOn.Should().Be(default);
    }

    [Test]
    public void a_deadline_that_already_passed_is_refused()
    {
        var draft = Fixtures.Draft() with { DeadlineDate = DateOnly.FromDateTime(Fixtures.Now), DeadlineHasTime = true, DeadlineTime = new TimeOnly(9, 0) };

        Poll.Create(draft, Fixtures.Owner, "k7Gm2xQa", Fixtures.Now)
            .Errors().Should().Contain("Die sluitdatum is al voorbij. Kies een moment dat nog moet komen.");
    }

    [Test]
    public void today_without_a_time_is_fine_until_the_day_ends()
    {
        var draft = Fixtures.Draft() with { DeadlineDate = DateOnly.FromDateTime(Fixtures.Now) };

        Poll.Create(draft, Fixtures.Owner, "k7Gm2xQa", Fixtures.Now).IsValid.Should().BeTrue();
    }

    [Test]
    public void a_time_needs_a_day()
    {
        var draft = Fixtures.Draft() with { DeadlineHasTime = true, DeadlineTime = new TimeOnly(18, 0) };

        Poll.Create(draft, Fixtures.Owner, "k7Gm2xQa", Fixtures.Now)
            .Errors().Should().Contain("Kies ook een dag bij de sluittijd.");
    }

    [Test]
    public void a_time_skipped_by_summer_time_counts_as_an_hour_later()
    {
        // On 28 March 2027 the clocks jump from 02:00 to 03:00; 02:30 doesn't exist.
        DutchTime.ToUtc(new DateOnly(2027, 03, 28), new TimeOnly(2, 30))
            .Should().Be(new DateTime(2027, 03, 28, 01, 30, 00, DateTimeKind.Utc));
    }

    [Test]
    public void the_very_last_day_still_has_an_end()
    {
        DutchTime.EndOfDay(DateOnly.MaxValue).Should().BeAfter(DutchTime.ToUtc(DateOnly.MaxValue, new TimeOnly(23, 0)));
    }

    private static Poll WithDeadline(DateOnly day, TimeOnly? time = null)
    {
        var draft = Fixtures.Draft() with { DeadlineDate = day, DeadlineHasTime = time.HasValue, DeadlineTime = time ?? default };
        var poll = Poll.Create(draft, Fixtures.Owner, "k7Gm2xQa", Fixtures.Now);
        poll.IsValid.Should().BeTrue();
        return poll.Value;
    }
}
