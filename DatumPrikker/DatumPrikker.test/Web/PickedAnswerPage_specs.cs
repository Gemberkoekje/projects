using DatumPrikker.WebApp.Auth;
using DatumPrikker.WebApp.Pages.P;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DatumPrikker.test.Web;

/// <summary>D22: once the owner picked, everyone opening the link sees the pick instead of the form.</summary>
public sealed class PickedAnswerPage_specs
{
    private readonly InMemoryPollStore polls = new InMemoryPollStore();
    private readonly InMemoryAnswerStore answers = new InMemoryAnswerStore();
    private readonly Poll poll = Fixtures.Poll(Fixtures.Evening(12), Fixtures.Evening(13));

    [SetUp]
    public async Task StorePollAndAnswer()
    {
        await polls.SaveAsync(poll);
        await answers.SaveAsync(Fixtures.Answer("Carla", "carla@example.com", poll, Availability.No, Availability.Yes));
    }

    [Test]
    public async Task before_the_pick_somebody_who_answered_sees_the_likely_date()
    {
        var page = await OpenAsync(Carla);

        page.IsOpen.Should().BeTrue();
        page.Poll.HasPick.Should().BeFalse();
        page.ShowsLikely.Should().BeTrue();
    }

    [Test]
    public async Task after_the_pick_somebody_who_answered_sees_what_they_said_to_it_instead_of_the_likely_date()
    {
        await PickAsync(poll.Options[1]);

        var page = await OpenAsync(Carla);

        page.Poll.HasPick.Should().BeTrue();
        page.IsOpen.Should().BeFalse("picking closed the poll, so there is no form");
        page.ShowsLikely.Should().BeFalse("the pick card takes its place");
        page.Pick.Poll.PickedOption().Should().Be(poll.Options[1]);
        page.Pick.YourChoice.Should().Be(Availability.Yes);
        page.Pick.ForOwner.Should().BeFalse();
    }

    [Test]
    public async Task after_the_pick_anybody_with_the_link_sees_it_without_what_somebody_said()
    {
        await PickAsync(poll.Options[0]);

        var page = await OpenAsync(new System.Security.Claims.ClaimsPrincipal());

        page.Poll.HasPick.Should().BeTrue();
        page.HasEarlier.Should().BeFalse();
        page.Pick.YourChoice.Should().Be(Availability.None);
    }

    [Test]
    public async Task past_the_deadline_the_page_is_closed()
    {
        var withDeadline = Poll.Create(Fixtures.Draft() with { DeadlineDate = new DateOnly(2026, 12, 04) }, Fixtures.Owner, "k7Gm2xQc", Fixtures.Now).Value;
        await polls.SaveAsync(withDeadline);

        using (Clock.SetTimeForCurrentContext(() => withDeadline.Deadline.AddSeconds(-1)))
        {
            (await OpenAsync(Carla, withDeadline.ShareToken)).IsOpen.Should().BeTrue();
        }

        using (Clock.SetTimeForCurrentContext(() => withDeadline.Deadline))
        {
            (await OpenAsync(Carla, withDeadline.ShareToken)).IsOpen.Should().BeFalse();
        }
    }

    private static System.Security.Claims.ClaimsPrincipal Carla => AccountClaims.ForDev("Carla", "carla@example.com", "Cookies");

    private async Task PickAsync(DateOption option)
        => await polls.SaveAsync((await polls.LoadAsync(poll.ShareToken)).Value.Pick(option.Id, Fixtures.Owner, Fixtures.Now).Value);

    private async Task<PrikkenModel> OpenAsync(System.Security.Claims.ClaimsPrincipal user, string? token = null)
    {
        var getPoll = new GetPollHandler(polls);
        var page = new PrikkenModel(
            getPoll,
            new GetAnswerHandler(answers),
            new GetLikelyDateHandler(getPoll, answers),
            new GiveAnswerHandler(polls, answers, NullLogger<GiveAnswerHandler>.Instance),
            new RespondentMemory(new EphemeralDataProtectionProvider()))
        {
            PageContext = new PageContext { HttpContext = new DefaultHttpContext { User = user } },
        };

        await page.OnGetAsync(token ?? poll.ShareToken, CancellationToken.None);
        return page;
    }
}
