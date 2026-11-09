namespace DatumPrikker.core.Models;

/// <summary>An email address the admin approved in advance (D15). The id is the normalised address.</summary>
public sealed record PreApproval
{
    /// <summary>The normalised email address; also the document id.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>When it was added (UTC).</summary>
    public DateTime AddedOn { get; init; }

    /// <summary>The admin account that added it.</summary>
    public string AddedBy { get; init; } = string.Empty;

    /// <summary>Pre-approves an address.</summary>
    /// <param name="email">The address, as typed.</param>
    /// <param name="admin">The admin's account id.</param>
    /// <param name="now">The current UTC time.</param>
    /// <returns>The pre-approval, or why the address is no good.</returns>
    public static Result<PreApproval> For(string email, string admin, DateTime now)
        => Email.TryNormalise(email, out var normalised)
            ? new PreApproval { Id = normalised, AddedOn = now, AddedBy = admin }
            : Result.WithMessages<PreApproval>(ValidationMessage.Error("Vul een geldig e-mailadres in.", nameof(email)));
}
