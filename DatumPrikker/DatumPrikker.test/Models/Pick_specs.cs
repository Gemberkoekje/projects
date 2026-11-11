namespace DatumPrikker.test.Models;

/// <summary>D22: the owner picks "this is the date", can change it and undo it; there's no reopening.</summary>
public sealed class Pick_specs
{
    private readonly Poll poll = Fixtures.Poll(Fixtures.Evening(11), Fixtures.Evening(12), Fixtures.WholeDay(13));

    [Test]
    public void only_the_owner_picks()
    {
        var option = poll.Options[1].Id;

        poll.Pick(option, Fixtures.Stranger, Fixtures.Now).Errors().Should().Contain("Alleen de maker kan de datum kiezen.");
        poll.Pick(option, Fixtures.Admin, Fixtures.Now).IsValid.Should().BeFalse("admins look on, they don't pick (D18)");
        poll.Pick(option, Account.Anonymous, Fixtures.Now).IsValid.Should().BeFalse();
        poll.Pick(option, Fixtures.Owner, Fixtures.Now).IsValid.Should().BeTrue();
    }

    [Test]
    public void only_a_date_of_this_poll_can_be_picked()
    {
        var other = Fixtures.Poll(Fixtures.Evening(12));

        poll.Pick(other.Options[0].Id, Fixtures.Owner, Fixtures.Now).Errors().Should().Contain("Die datum hoort niet bij deze prikker.");
        poll.Pick(Guid.Empty, Fixtures.Owner, Fixtures.Now).IsValid.Should().BeFalse();
    }

    [Test]
    public void picking_closes_an_open_poll()
    {
        var picked = poll.Pick(poll.Options[1].Id, Fixtures.Owner, Fixtures.Now).Value;

        picked.HasPick.Should().BeTrue();
        picked.PickedOption().Should().Be(poll.Options[1]);
        picked.PickedOn.Should().Be(Fixtures.Now);
        picked.ClosedOn.Should().Be(Fixtures.Now);
        picked.IsOpenAt(Fixtures.Now).Should().BeFalse();
    }

    [Test]
    public void picking_after_the_deadline_keeps_it_closed_by_the_deadline()
    {
        var withDeadline = Poll.Create(Fixtures.Draft() with { DeadlineDate = new DateOnly(2026, 12, 01) }, Fixtures.Owner, "k7Gm2xQa", Fixtures.Now).Value;
        var later = withDeadline.Deadline.AddDays(2);

        var picked = withDeadline.Pick(withDeadline.Options[0].Id, Fixtures.Owner, later).Value;

        picked.ClosedOn.Should().Be(default);
        picked.ClosedByDeadlineAt(later).Should().BeTrue();
    }

    [Test]
    public void the_owner_can_change_the_pick()
    {
        var first = poll.Pick(poll.Options[1].Id, Fixtures.Owner, Fixtures.Now).Value;
        var changedOn = Fixtures.Now.AddHours(3);

        var changed = first.Pick(poll.Options[2].Id, Fixtures.Owner, changedOn).Value;

        changed.PickedOption().Should().Be(poll.Options[2]);
        changed.PickedOn.Should().Be(changedOn);
        changed.ClosedOn.Should().Be(Fixtures.Now, "it was closed by the first pick");
    }

    [Test]
    public void picking_the_same_date_again_changes_nothing()
    {
        var first = poll.Pick(poll.Options[1].Id, Fixtures.Owner, Fixtures.Now).Value;

        first.Pick(poll.Options[1].Id, Fixtures.Owner, Fixtures.Now.AddHours(1)).Value.Should().Be(first);
    }

    [Test]
    public void undoing_the_pick_leaves_the_poll_closed()
    {
        var picked = poll.Pick(poll.Options[1].Id, Fixtures.Owner, Fixtures.Now).Value;

        var undone = picked.UndoPick(Fixtures.Owner).Value;

        undone.HasPick.Should().BeFalse();
        undone.PickedOption().Id.Should().Be(Guid.Empty);
        undone.IsOpenAt(Fixtures.Now.AddDays(1)).Should().BeFalse();
        undone.Pick(poll.Options[0].Id, Fixtures.Owner, Fixtures.Now.AddDays(1)).Value.HasPick.Should().BeTrue("the owner can pick again");
    }

    [Test]
    public void only_the_owner_undoes_the_pick()
    {
        var picked = poll.Pick(poll.Options[1].Id, Fixtures.Owner, Fixtures.Now).Value;

        picked.UndoPick(Fixtures.Stranger).Errors().Should().Contain("Alleen de maker kan de keuze ongedaan maken.");
        picked.UndoPick(Fixtures.Admin).IsValid.Should().BeFalse();
    }

    [Test]
    public void a_poll_with_a_deadline_and_a_pick_round_trips()
    {
        var draft = Fixtures.Draft() with { DeadlineDate = new DateOnly(2026, 12, 04), DeadlineHasTime = true, DeadlineTime = new TimeOnly(18, 0) };
        var made = Poll.Create(draft, Fixtures.Owner, "k7Gm2xQa", Fixtures.Now).Value;
        var picked = made.Pick(made.Options[0].Id, Fixtures.Owner, Fixtures.Now.AddDays(1)).Value;
        var options = new System.Text.Json.JsonSerializerOptions();

        var copy = System.Text.Json.JsonSerializer.Deserialize<Poll>(System.Text.Json.JsonSerializer.Serialize(picked, options), options);

        copy.Should().BeEquivalentTo(picked);
        copy.PickedOption().Should().Be(made.Options[0]);
    }
}
