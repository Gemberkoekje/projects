namespace DatumPrikker.application.Queries;

/// <summary>Writes the agenda file of a poll's picked date; refused until a date is picked.</summary>
public sealed class GetAgendaHandler(GetPollHandler polls)
{
    /// <summary>The refusal while no date is picked.</summary>
    public const string NothingPicked = "Er is (nog) geen datum gekozen.";

    private readonly GetPollHandler polls = polls ?? throw new ArgumentNullException(nameof(polls));

    /// <summary>Handles the query.</summary>
    /// <param name="query">Which poll, and the site's address.</param>
    /// <param name="cancellationToken">Cancels the load.</param>
    /// <returns>The file, <see cref="NothingPicked"/>, or <see cref="IPollStore.NotFound"/>.</returns>
    public Task<Result<Agenda>> HandleAsync(GetAgenda query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        return WriteAsync(query, cancellationToken);
    }

    private async Task<Result<Agenda>> WriteAsync(GetAgenda query, CancellationToken cancellationToken)
    {
        var poll = await polls.HandleAsync(new GetPoll { ShareToken = query.ShareToken }, cancellationToken);
        if (!poll.IsValid)
        {
            return Result.WithMessages<Agenda>(poll.Messages);
        }

        if (!poll.Value.HasPick)
        {
            return Result.WithMessages<Agenda>(ValidationMessage.Error(NothingPicked, nameof(query.ShareToken)));
        }

        return new Agenda
        {
            Title = poll.Value.Title,
            Text = AgendaFile.For(poll.Value, $"{query.SiteUrl.TrimEnd('/')}/p/{poll.Value.ShareToken}"),
        };
    }
}
