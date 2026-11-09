namespace DatumPrikker.test.Models;

public sealed class Access_specs
{
    [Test]
    public void a_request_waits_with_its_note_trimmed()
    {
        var access = Access.Request(Fixtures.Stranger, "  Bea, buurvrouw  ", preApproved: false, Fixtures.Now);

        access.Value.Should().BeEquivalentTo(new
        {
            Id = "microsoft:222",
            Name = "Bea",
            Email = "bea@example.com",
            Note = "Bea, buurvrouw",
            State = AccessState.Requested,
            Provider = "microsoft",
        });
    }

    [Test]
    public void a_pre_approved_request_is_approved_at_once()
    {
        var access = Access.Request(Fixtures.Stranger, string.Empty, preApproved: true, Fixtures.Now).Value;

        access.IsApproved.Should().BeTrue();
        access.DecidedBy.Should().Be(Access.PreApproved);
    }

    [Test]
    public void nobody_anonymous_can_ask_and_a_note_has_a_limit()
    {
        Access.Request(Account.Anonymous, string.Empty, false, Fixtures.Now).IsValid.Should().BeFalse();
        Access.Request(Fixtures.Stranger, new string('x', Access.MaxNoteLength + 1), false, Fixtures.Now).IsValid.Should().BeFalse();
    }

    [TestCase(AccessState.Requested, AccessDecision.Approve, AccessState.Approved)]
    [TestCase(AccessState.Requested, AccessDecision.Decline, AccessState.Declined)]
    [TestCase(AccessState.Approved, AccessDecision.Revoke, AccessState.Revoked)]
    [TestCase(AccessState.Declined, AccessDecision.Approve, AccessState.Approved)]
    [TestCase(AccessState.Revoked, AccessDecision.Approve, AccessState.Approved)]
    public void a_decision_moves_the_state(AccessState from, AccessDecision decision, AccessState to)
    {
        var decided = new Access { Id = "microsoft:222", State = from }.Decide(decision, Fixtures.Admin.Id, Fixtures.Now);

        decided.Value.State.Should().Be(to);
        decided.Value.DecidedBy.Should().Be(Fixtures.Admin.Id);
        decided.Value.DecidedOn.Should().Be(Fixtures.Now);
    }

    [TestCase(AccessState.Approved, AccessDecision.Decline)]
    [TestCase(AccessState.Requested, AccessDecision.Revoke)]
    [TestCase(AccessState.Declined, AccessDecision.Revoke)]
    [TestCase(AccessState.Requested, AccessDecision.None)]
    public void a_decision_that_does_not_fit_the_state_is_refused(AccessState from, AccessDecision decision)
    {
        new Access { Id = "microsoft:222", State = from }.Decide(decision, Fixtures.Admin.Id, Fixtures.Now).IsValid.Should().BeFalse();
    }

    [TestCase("google:1", true)]
    [TestCase("google:2", true)]
    [TestCase("google:3", false)]
    [TestCase("GOOGLE:1", false)]
    public void admins_are_configured_account_ids(string id, bool isAdmin)
    {
        var admins = Admins.Parse(" google:1 ,google:2; ");

        admins.Count.Should().Be(2);
        admins.Includes(new Account { Id = id }).Should().Be(isAdmin);
        admins.Includes(Account.Anonymous).Should().BeFalse();
    }

    [Test]
    public void no_configuration_means_no_admins()
    {
        Admins.Parse(string.Empty).Count.Should().Be(0);
        Admins.None.Require(Fixtures.Admin).Errors().Should().Contain(Admins.OnlyMessage);
    }
}
