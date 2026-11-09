namespace DatumPrikker.application.Ports;

/// <summary>Keeps the <see cref="Answer"/> documents.</summary>
public interface IAnswerStore
{
    /// <summary>Loads one answer.</summary>
    /// <param name="id">The answer's id (see <see cref="Answer.IdFor"/>).</param>
    /// <param name="cancellationToken">Cancels the load.</param>
    /// <returns>The answer, or an error when there is none.</returns>
    Task<Result<Answer>> LoadAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>Every answer to one poll.</summary>
    /// <param name="pollId">The poll.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The answers, in no particular order.</returns>
    Task<IReadOnlyList<Answer>> ListForPollAsync(Guid pollId, CancellationToken cancellationToken = default);

    /// <summary>Every answer given with one email address, to any poll.</summary>
    /// <param name="email">The normalised email address.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The answers, in no particular order.</returns>
    Task<IReadOnlyList<Answer>> ListByEmailAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>How many answers each of these polls has.</summary>
    /// <param name="pollIds">The polls.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The count per poll; polls without answers are missing.</returns>
    Task<IReadOnlyDictionary<Guid, int>> CountForPollsAsync(IReadOnlyCollection<Guid> pollIds, CancellationToken cancellationToken = default);

    /// <summary>Stores an answer, replacing one with the same id.</summary>
    /// <param name="answer">The answer.</param>
    /// <param name="cancellationToken">Cancels the save.</param>
    /// <returns>OK, or why it could not be stored.</returns>
    Task<Result> SaveAsync(Answer answer, CancellationToken cancellationToken = default);
}
