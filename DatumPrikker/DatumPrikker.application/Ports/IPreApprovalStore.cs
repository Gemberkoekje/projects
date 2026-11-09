namespace DatumPrikker.application.Ports;

/// <summary>Keeps the <see cref="PreApproval"/> documents, one per email address.</summary>
public interface IPreApprovalStore
{
    /// <summary>True when the address was pre-approved.</summary>
    /// <param name="email">The normalised email address.</param>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <returns>True when pre-approved.</returns>
    Task<bool> IsPreApprovedAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>Every pre-approved address.</summary>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The pre-approvals, in no particular order.</returns>
    Task<IReadOnlyList<PreApproval>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Stores a pre-approval, replacing one for the same address.</summary>
    /// <param name="preApproval">The pre-approval.</param>
    /// <param name="cancellationToken">Cancels the save.</param>
    /// <returns>OK, or why it could not be stored.</returns>
    Task<Result> SaveAsync(PreApproval preApproval, CancellationToken cancellationToken = default);

    /// <summary>Removes the pre-approval of an address; removing a missing one is fine.</summary>
    /// <param name="email">The normalised email address.</param>
    /// <param name="cancellationToken">Cancels the delete.</param>
    /// <returns>OK, or why it could not be removed.</returns>
    Task<Result> DeleteAsync(string email, CancellationToken cancellationToken = default);
}
