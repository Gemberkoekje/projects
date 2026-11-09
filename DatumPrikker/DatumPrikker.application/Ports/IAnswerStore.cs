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

    /// <summary>Stores an answer, replacing one with the same id.</summary>
    /// <param name="answer">The answer.</param>
    /// <param name="cancellationToken">Cancels the save.</param>
    /// <returns>OK, or why it could not be stored.</returns>
    Task<Result> SaveAsync(Answer answer, CancellationToken cancellationToken = default);
}
