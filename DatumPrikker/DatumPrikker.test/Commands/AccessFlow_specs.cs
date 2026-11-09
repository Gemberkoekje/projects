namespace DatumPrikker.test.Commands;

/// <summary>D14–D18 through the handlers: asking, deciding, pre-approving, the gate and the admin lists.</summary>
public sealed class AccessFlow_specs
{
    private readonly InMemoryPollStore polls = new InMemoryPollStore();
    private readonly InMemoryAnswerStore answers = new InMemoryAnswerStore();
    private readonly InMemoryAccessStore access = new InMemoryAccessStore();
    private readonly InMemoryPreApprovalStore preApprovals = new InMemoryPreApprovalStore();

    private RequestAccessHandler Request => new RequestAccessHandler(access, preApprovals, NullLogger<RequestAccessHandler>.Instance);

    private DecideAccessHandler Decide => new DecideAccessHandler(access, Fixtures.Admins, NullLogger<DecideAccessHandler>.Instance);

    private PreApprovalHandler PreApprove => new PreApprovalHandler(preApprovals, Fixtures.Admins, NullLogger<PreApprovalHandler>.Instance);

    private CreatePollHandler Create => new CreatePollHandler(polls, access, Fixtures.Admins, NullLogger<CreatePollHandler>.Instance);

    [Test]
    public async Task asking_twice_keeps_one_waiting_request()
    {
        await Request.HandleAsync(new RequestAccess { By = Fixtures.Stranger, Note = "Bea" });
        var again = await Request.HandleAsync(new RequestAccess { By = Fixtures.Stranger, Note = "Bea nog eens" });

        again.Value.Note.Should().Be("Bea");
        access.All.Should().ContainSingle().Which.State.Should().Be(AccessState.Requested);
    }

    [Test]
    public async Task a_pre_approved_address_is_approved_on_asking()
    {
        await PreApprove.HandleAsync(new PreApprove { By = Fixtures.Admin, Email = " BEA@example.com " });

        var asked = await Request.HandleAsync(new RequestAccess { By = Fixtures.Stranger });

        asked.Value.IsApproved.Should().BeTrue();
    }

    [Test]
    public async Task after_a_decline_there_is_no_asking_again()
    {
        await Request.HandleAsync(new RequestAccess { By = Fixtures.Stranger });
        await Decide.HandleAsync(new DecideAccess { By = Fixtures.Admin, AccountId = Fixtures.Stranger.Id, Decision = AccessDecision.Decline });

        (await Request.HandleAsync(new RequestAccess { By = Fixtures.Stranger })).Errors().Should().Contain(RequestAccessHandler.AlreadyDecided);
        access.All.Single().State.Should().Be(AccessState.Declined);
    }

    [Test]
    public async Task only_an_admin_decides()
    {
        await Request.HandleAsync(new RequestAccess { By = Fixtures.Stranger });

        (await Decide.HandleAsync(new DecideAccess { By = Fixtures.Owner, AccountId = Fixtures.Stranger.Id, Decision = AccessDecision.Approve }))
            .Errors().Should().Contain(Admins.OnlyMessage);
        (await PreApprove.HandleAsync(new PreApprove { By = Fixtures.Owner, Email = "x@example.com" }))
            .Errors().Should().Contain(Admins.OnlyMessage);
        access.All.Single().State.Should().Be(AccessState.Requested);
        preApprovals.All.Should().BeEmpty();
    }

    [Test]
    public async Task the_gate_lets_only_admins_and_approved_accounts_make_polls()
    {
        (await Create.HandleAsync(new CreatePoll { Draft = Fixtures.Draft(), Owner = Fixtures.Admin })).IsValid.Should().BeTrue("admins are always allowed");
        (await Create.HandleAsync(new CreatePoll { Draft = Fixtures.Draft(), Owner = Fixtures.Stranger })).Errors().Should().Contain(CreatePollHandler.NoAccess);

        await Request.HandleAsync(new RequestAccess { By = Fixtures.Stranger });
        (await Create.HandleAsync(new CreatePoll { Draft = Fixtures.Draft(), Owner = Fixtures.Stranger })).Errors().Should().Contain(CreatePollHandler.NoAccess);

        await Decide.HandleAsync(new DecideAccess { By = Fixtures.Admin, AccountId = Fixtures.Stranger.Id, Decision = AccessDecision.Approve });
        (await Create.HandleAsync(new CreatePoll { Draft = Fixtures.Draft(), Owner = Fixtures.Stranger })).IsValid.Should().BeTrue();

        await Decide.HandleAsync(new DecideAccess { By = Fixtures.Admin, AccountId = Fixtures.Stranger.Id, Decision = AccessDecision.Revoke });
        (await Create.HandleAsync(new CreatePoll { Draft = Fixtures.Draft(), Owner = Fixtures.Stranger })).Errors().Should().Contain(CreatePollHandler.NoAccess);

        polls.All.Should().HaveCount(2, "revoking stops new polls; the approved one stays");
    }

    [Test]
    public async Task an_anonymous_maker_gets_the_log_in_message_not_the_gate()
    {
        (await Create.HandleAsync(new CreatePoll { Draft = Fixtures.Draft(), Owner = Account.Anonymous }))
            .Errors().Should().Contain("Log in om een prikker te maken.");
    }

    [Test]
    public async Task pre_approvals_can_be_removed_and_bad_addresses_are_refused()
    {
        (await PreApprove.HandleAsync(new PreApprove { By = Fixtures.Admin, Email = "geen-adres" })).IsValid.Should().BeFalse();

        await PreApprove.HandleAsync(new PreApprove { By = Fixtures.Admin, Email = "bea@example.com" });
        await PreApprove.HandleAsync(new RemovePreApproval { By = Fixtures.Admin, Email = "Bea@Example.com" });

        preApprovals.All.Should().BeEmpty();
    }

    [Test]
    public async Task the_access_list_sorts_and_is_for_admins_only()
    {
        var carla = new Account { Id = "google:444", Name = "Carla", Email = "carla@example.com" };
        var dirk = new Account { Id = "google:555", Name = "Dirk", Email = "dirk@example.com" };
        await Request.HandleAsync(new RequestAccess { By = Fixtures.Stranger });
        await Request.HandleAsync(new RequestAccess { By = carla });
        await Request.HandleAsync(new RequestAccess { By = dirk });
        await Decide.HandleAsync(new DecideAccess { By = Fixtures.Admin, AccountId = carla.Id, Decision = AccessDecision.Approve });
        await Decide.HandleAsync(new DecideAccess { By = Fixtures.Admin, AccountId = dirk.Id, Decision = AccessDecision.Decline });
        var list = new ListAccessHandler(access, preApprovals, Fixtures.Admins);

        var overview = (await list.HandleAsync(new ListAccess { By = Fixtures.Admin })).Value;

        overview.Requested.Select(x => x.Name).Should().Equal("Bea");
        overview.Approved.Select(x => x.Name).Should().Equal("Carla");
        overview.Refused.Select(x => x.Name).Should().Equal("Dirk");
        (await list.HandleAsync(new ListAccess { By = Fixtures.Owner })).IsValid.Should().BeFalse();
    }

    [Test]
    public async Task the_badge_counts_waiting_requests_for_admins_only()
    {
        await Request.HandleAsync(new RequestAccess { By = Fixtures.Stranger });
        var count = new CountPendingRequestsHandler(access, Fixtures.Admins);

        (await count.HandleAsync(new CountPendingRequests { By = Fixtures.Admin })).Should().Be(1);
        (await count.HandleAsync(new CountPendingRequests { By = Fixtures.Stranger })).Should().Be(0);
    }

    [Test]
    public async Task all_polls_come_newest_first_with_their_answer_counts_for_admins_only()
    {
        var older = Poll.Create(Fixtures.Draft(), Fixtures.Owner, "k7Gm2xQa", Fixtures.Now.AddDays(-1)).Value;
        var newer = Poll.Create(Fixtures.Draft(), Fixtures.Stranger, "k7Gm2xQb", Fixtures.Now).Value;
        await polls.SaveAsync(older);
        await polls.SaveAsync(newer);
        await answers.SaveAsync(Fixtures.Answer("Carla", "carla@example.com", older, Availability.Yes));
        var list = new ListAllPollsHandler(polls, answers, Fixtures.Admins);

        var all = (await list.HandleAsync(new ListAllPolls { By = Fixtures.Admin })).Value;

        all.Select(x => x.Poll.Id).Should().Equal(newer.Id, older.Id);
        all.Select(x => x.AnswerCount).Should().Equal(0, 1);
        (await list.HandleAsync(new ListAllPolls { By = Fixtures.Owner })).IsValid.Should().BeFalse();
    }

    [Test]
    public async Task the_status_says_who_may_make_polls()
    {
        var status = new GetAccessHandler(access, Fixtures.Admins);

        (await status.HandleAsync(new GetAccess { Account = Fixtures.Admin })).CanCreatePolls.Should().BeTrue();
        (await status.HandleAsync(new GetAccess { Account = Account.Anonymous })).CanCreatePolls.Should().BeFalse();

        var before = await status.HandleAsync(new GetAccess { Account = Fixtures.Stranger });
        before.HasAsked.Should().BeFalse();

        await Request.HandleAsync(new RequestAccess { By = Fixtures.Stranger });
        var after = await status.HandleAsync(new GetAccess { Account = Fixtures.Stranger });
        after.HasAsked.Should().BeTrue();
        after.CanCreatePolls.Should().BeFalse();
    }
}
