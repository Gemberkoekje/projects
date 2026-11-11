using DatumPrikker.WebApp;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using System.Text;

namespace DatumPrikker.test.Queries;

/// <summary>D22: <c>/p/{token}/agenda.ics</c> is open to anyone with the link, and a 404 until a date is picked.</summary>
public sealed class GetAgenda_specs
{
    private readonly InMemoryPollStore polls = new InMemoryPollStore();
    private readonly Poll poll = Fixtures.Poll(Fixtures.Evening(12), Fixtures.Evening(13));

    private GetAgendaHandler Handler => new GetAgendaHandler(new GetPollHandler(polls));

    [SetUp]
    public async Task StorePoll() => await polls.SaveAsync(poll);

    [Test]
    public async Task without_a_pick_there_is_no_agenda()
    {
        (await Ask(poll.ShareToken)).Errors().Should().Contain(GetAgendaHandler.NothingPicked);
    }

    [Test]
    public async Task with_a_pick_anyone_gets_the_event_with_the_polls_link()
    {
        await polls.SaveAsync(poll.Pick(poll.Options[1].Id, Fixtures.Owner, Fixtures.Now).Value);

        var agenda = (await Ask(poll.ShareToken)).Value;

        agenda.Title.Should().Be(poll.Title);
        agenda.Text.Should().Contain("DTSTART:20261213T180000Z").And.Contain("https://datumprikker.example/p/k7Gm2xQa");
    }

    [TestCase("k7Gm2xQb")]
    [TestCase("niet-een-token")]
    public async Task an_unknown_or_malformed_token_is_not_found(string token)
    {
        (await Ask(token)).Errors().Should().Contain(IPollStore.NotFound);
    }

    [Test]
    public async Task the_endpoint_is_a_404_until_a_date_is_picked()
    {
        (await Download(poll.ShareToken)).Result.Should().BeOfType<NotFound>();
        (await Download("k7Gm2xQb")).Result.Should().BeOfType<NotFound>();

        await polls.SaveAsync(poll.Pick(poll.Options[0].Id, Fixtures.Owner, Fixtures.Now).Value);

        var file = (await Download(poll.ShareToken)).Result.Should().BeOfType<FileContentHttpResult>().Subject;
        file.ContentType.Should().Be("text/calendar; charset=utf-8");
        file.FileDownloadName.Should().Be("etentje-met-de-buren.ics");
        Encoding.UTF8.GetString(file.FileContents.Span).Should().Contain("BEGIN:VEVENT");
    }

    private Task<Result<Agenda>> Ask(string token)
        => Handler.HandleAsync(new GetAgenda { ShareToken = token, SiteUrl = "https://datumprikker.example/" });

    private Task<Results<FileContentHttpResult, NotFound>> Download(string token)
    {
        var context = new DefaultHttpContext();
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("datumprikker.example");
        return AgendaEndpoint.HandleAsync(token, context.Request, Handler, CancellationToken.None);
    }
}
