namespace DatumPrikker.test.Models;

public sealed class Poll_specs
{
    [Test]
    public void a_valid_draft_becomes_an_open_poll_with_its_dates_in_order()
    {
        var poll = Poll.Create(Fixtures.Draft(Fixtures.WholeDay(14), Fixtures.Evening(12)), Fixtures.Owner, "k7Gm2xQa", Fixtures.Now);

        poll.IsValid.Should().BeTrue();
        poll.Value.IsOpen.Should().BeTrue();
        poll.Value.OwnerAccountId.Should().Be("google:111");
        poll.Value.Options.Select(option => option.Date.Day).Should().Equal(12, 14);
        poll.Value.Options.Should().OnlyContain(option => option.Id != Guid.Empty);
    }

    [Test]
    public void the_maker_chooses_the_name_respondents_see()
    {
        var poll = Poll.Create(Fixtures.Draft() with { OwnerName = "  Arie  " }, Fixtures.Owner with { Name = "Gember \"Gemberkoekje\" Koekje" }, "k7Gm2xQa", Fixtures.Now);

        poll.Value.OwnerName.Should().Be("Arie");
    }

    [TestCase("")]
    [TestCase("   ")]
    public void without_a_name_the_accounts_name_stands_in(string typed)
    {
        Poll.Create(Fixtures.Draft() with { OwnerName = typed }, Fixtures.Owner, "k7Gm2xQa", Fixtures.Now)
            .Value.OwnerName.Should().Be(Fixtures.Owner.Name);
    }

    [Test]
    public void the_makers_name_has_a_limit()
    {
        Poll.Create(Fixtures.Draft() with { OwnerName = new string('x', Poll.MaxOwnerNameLength + 1) }, Fixtures.Owner, "k7Gm2xQa", Fixtures.Now)
            .Errors().Should().Contain($"Je naam mag hooguit {Poll.MaxOwnerNameLength} tekens lang zijn.");
    }

    [Test]
    public async Task the_name_field_suggests_the_name_on_the_latest_poll()
    {
        var polls = new InMemoryPollStore();
        var handler = new GetMakerNameHandler(polls);
        var gember = Fixtures.Owner with { Name = "Gember \"Gemberkoekje\" Koekje" };

        (await handler.HandleAsync(new GetMakerName { Account = gember })).Should().Be(gember.Name, "no polls yet: the provider's name");

        await polls.SaveAsync(Poll.Create(Fixtures.Draft() with { OwnerName = "Gember" }, gember, "k7Gm2xQa", Fixtures.Now.AddDays(-2)).Value);
        await polls.SaveAsync(Poll.Create(Fixtures.Draft() with { OwnerName = "Arie" }, gember, "k7Gm2xQb", Fixtures.Now).Value);

        (await handler.HandleAsync(new GetMakerName { Account = gember })).Should().Be("Arie");
        (await handler.HandleAsync(new GetMakerName { Account = Account.Anonymous })).Should().BeEmpty();
    }

    [Test]
    public void nobody_anonymous_can_make_a_poll()
    {
        Poll.Create(Fixtures.Draft(), Account.Anonymous, "k7Gm2xQa", Fixtures.Now)
            .Errors().Should().Contain("Log in om een prikker te maken.");
    }

    [TestCase("")]
    [TestCase("   ")]
    public void a_poll_needs_a_title(string title)
    {
        Poll.Create(Fixtures.Draft() with { Title = title }, Fixtures.Owner, "k7Gm2xQa", Fixtures.Now)
            .Errors().Should().Contain("Geef je prikker een titel.");
    }

    [Test]
    public void a_poll_needs_at_least_one_date()
    {
        Poll.Create(Fixtures.Draft() with { Options = [] }, Fixtures.Owner, "k7Gm2xQa", Fixtures.Now)
            .Errors().Should().Contain("Stel minstens één datum voor.");
    }

    [Test]
    public void a_poll_takes_at_most_thirty_dates()
    {
        var options = Enumerable.Range(0, Poll.MaxOptions + 1)
            .Select(day => new DateOption { Date = new DateOnly(2027, 01, 01).AddDays(day) })
            .ToArray();

        Poll.Create(Fixtures.Draft(options), Fixtures.Owner, "k7Gm2xQa", Fixtures.Now)
            .Errors().Should().Contain("Stel hooguit 30 datums voor.");
    }

    [Test]
    public void an_end_time_must_come_after_the_start()
    {
        var backwards = Fixtures.Evening(12) with { Start = new TimeOnly(22, 0), End = new TimeOnly(19, 0) };

        Poll.Create(Fixtures.Draft(backwards), Fixtures.Owner, "k7Gm2xQa", Fixtures.Now)
            .Errors().Should().Contain("Een eindtijd moet na de begintijd liggen.");
    }

    [Test]
    public void the_same_slot_twice_is_refused_but_the_same_day_at_other_times_is_fine()
    {
        Poll.Create(Fixtures.Draft(Fixtures.Evening(12), Fixtures.Evening(12)), Fixtures.Owner, "k7Gm2xQa", Fixtures.Now)
            .Errors().Should().Contain("Je hebt dezelfde datum twee keer voorgesteld.");

        var lunch = Fixtures.Evening(12) with { Start = new TimeOnly(12, 0), End = new TimeOnly(13, 0) };
        Poll.Create(Fixtures.Draft(Fixtures.Evening(12), lunch, Fixtures.WholeDay(12)), Fixtures.Owner, "k7Gm2xQa", Fixtures.Now)
            .IsValid.Should().BeTrue();
    }

    [Test]
    public void only_the_owner_can_close_a_poll()
    {
        var poll = Fixtures.Poll();

        poll.Close(Fixtures.Stranger, Fixtures.Now).IsValid.Should().BeFalse();
        poll.Close(Account.Anonymous, Fixtures.Now).IsValid.Should().BeFalse();

        var closed = poll.Close(Fixtures.Owner, Fixtures.Now);
        closed.IsValid.Should().BeTrue();
        closed.Value.IsOpen.Should().BeFalse();
    }
}
