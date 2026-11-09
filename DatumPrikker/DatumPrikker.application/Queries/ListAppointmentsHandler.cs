namespace DatumPrikker.application.Queries;

/// <summary>
/// Merges the polls an account made (found by account id) with the polls answered under its email
/// (D6): open ones first, then the most recent activity first.
/// </summary>
public sealed class ListAppointmentsHandler(IPollStore polls, IAnswerStore answers)
{
    private readonly IPollStore polls = polls ?? throw new ArgumentNullException(nameof(polls));
    private readonly IAnswerStore answers = answers ?? throw new ArgumentNullException(nameof(answers));

    /// <summary>Handles the query.</summary>
    /// <param name="query">Whose afspraken.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The afspraken; empty for somebody anonymous.</returns>
    public Task<IReadOnlyList<Appointment>> HandleAsync(ListAppointments query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        return query.Account.IsAnonymous
            ? Task.FromResult<IReadOnlyList<Appointment>>([])
            : ListAsync(query.Account, cancellationToken);
    }

    private async Task<IReadOnlyList<Appointment>> ListAsync(Account account, CancellationToken cancellationToken)
    {
        var owned = await polls.ListOwnedByAsync(account.Id, cancellationToken);
        var mine = account.Email.Length > 0
            ? await answers.ListByEmailAsync(account.Email, cancellationToken)
            : [];

        var ownedIds = owned.Select(poll => poll.Id).ToHashSet();
        var answeredOnly = await polls.LoadManyAsync([.. mine.Select(answer => answer.PollId).Where(id => !ownedIds.Contains(id)).Distinct()], cancellationToken);
        var all = owned.Concat(answeredOnly).ToArray();
        var counts = await answers.CountForPollsAsync([.. ownedIds], cancellationToken);

        return [.. all
            .Select(poll => new Appointment
            {
                Poll = poll,
                IsOwner = ownedIds.Contains(poll.Id),
                MyAnswer = mine.FirstOrDefault(answer => answer.PollId == poll.Id) ?? new Answer(),
                AnswerCount = counts.TryGetValue(poll.Id, out var count) ? count : 0,
            })
            .OrderByDescending(appointment => appointment.Poll.IsOpen)
            .ThenByDescending(appointment => appointment.LastActivity)];
    }
}
