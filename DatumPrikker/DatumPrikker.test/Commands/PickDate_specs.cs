namespace DatumPrikker.test.Commands;

public sealed class PickDate_specs
{
    private readonly InMemoryPollStore polls = new InMemoryPollStore();
    private readonly Poll poll = Fixtures.Poll();

    private PickDateHandler Handler => new PickDateHandler(polls, NullLogger<PickDateHandler>.Instance);

    private Poll Stored => polls.All.Single();

    [SetUp]
    public async Task StorePoll() => await polls.SaveAsync(poll);

    [Test]
    public async Task the_owner_picks_and_the_poll_closes()
    {
        using (Clock.SetTimeForCurrentContext(() => Fixtures.Now))
        {
            (await Pick(poll.Options[2].Id, Fixtures.Owner)).IsValid.Should().BeTrue();
        }

        Stored.PickedOptionId.Should().Be(poll.Options[2].Id);
        Stored.ClosedOn.Should().Be(Fixtures.Now);
    }

    [Test]
    public async Task nobody_else_picks_or_undoes()
    {
        (await Pick(poll.Options[0].Id, Fixtures.Stranger)).IsValid.Should().BeFalse();
        (await Pick(poll.Options[0].Id, Fixtures.Admin)).IsValid.Should().BeFalse();
        Stored.HasPick.Should().BeFalse();
        Stored.IsOpenAt(Fixtures.Now).Should().BeTrue();

        await Pick(poll.Options[0].Id, Fixtures.Owner);
        (await Handler.HandleAsync(new UndoPick { ShareToken = poll.ShareToken, By = Fixtures.Stranger })).IsValid.Should().BeFalse();
        Stored.HasPick.Should().BeTrue();
    }

    [Test]
    public async Task a_date_of_another_poll_is_refused()
    {
        (await Pick(Fixtures.Poll().Options[0].Id, Fixtures.Owner)).IsValid.Should().BeFalse();

        Stored.HasPick.Should().BeFalse();
    }

    [Test]
    public async Task the_owner_changes_and_undoes_the_pick()
    {
        await Pick(poll.Options[0].Id, Fixtures.Owner);
        await Pick(poll.Options[1].Id, Fixtures.Owner);
        Stored.PickedOptionId.Should().Be(poll.Options[1].Id);

        (await Handler.HandleAsync(new UndoPick { ShareToken = poll.ShareToken, By = Fixtures.Owner })).IsValid.Should().BeTrue();
        Stored.HasPick.Should().BeFalse();
        Stored.IsOpenAt(Clock.UtcNow()).Should().BeFalse();
    }

    [Test]
    public async Task an_unknown_poll_is_not_found()
    {
        (await Handler.HandleAsync(new PickDate { ShareToken = "k7Gm2xQb", OptionId = poll.Options[0].Id, By = Fixtures.Owner }))
            .Errors().Should().Contain(IPollStore.NotFound);
    }

    private Task<Result> Pick(Guid optionId, Account by)
        => Handler.HandleAsync(new PickDate { ShareToken = poll.ShareToken, OptionId = optionId, By = by });
}
