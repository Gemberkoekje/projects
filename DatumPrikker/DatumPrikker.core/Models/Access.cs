namespace DatumPrikker.core.Models;

/// <summary>One account's permission to make polls (D14). The id is the account id.</summary>
public sealed record Access
{
    /// <summary>The longest note with a request.</summary>
    public const int MaxNoteLength = 280;

    /// <summary>Who decided when a pre-approval approved the request on the spot (D15).</summary>
    public const string PreApproved = "vooraf goedgekeurd";

    /// <summary>The account id, such as <c>google:1234</c>; also the document id.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>The name the provider gave.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>The normalised email the provider gave; may be empty.</summary>
    public string Email { get; init; } = string.Empty;

    /// <summary>The optional note with the request ("Wie ben je?").</summary>
    public string Note { get; init; } = string.Empty;

    /// <summary>Where the account stands.</summary>
    public AccessState State { get; init; }

    /// <summary>When it asked (UTC).</summary>
    public DateTime RequestedOn { get; init; }

    /// <summary>When the last decision was made (UTC); <see langword="default"/> while waiting.</summary>
    public DateTime DecidedOn { get; init; }

    /// <summary>The admin account that decided last, or <see cref="PreApproved"/>.</summary>
    public string DecidedBy { get; init; } = string.Empty;

    /// <summary>The provider, from the account id's prefix: <c>google</c>, <c>microsoft</c> or <c>dev</c>.</summary>
    public string Provider => Id.Split(':', 2)[0];

    /// <summary>True when it may make polls.</summary>
    public bool IsApproved => State == AccessState.Approved;

    /// <summary>Asks for access.</summary>
    /// <param name="by">The signed-in account asking.</param>
    /// <param name="note">An optional note for the admin.</param>
    /// <param name="preApproved">True when the account's email was pre-approved (D15): then it is approved at once.</param>
    /// <param name="now">The current UTC time.</param>
    /// <returns>The request, or why it can't be made.</returns>
    public static Result<Access> Request(Account by, string note, bool preApproved, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(by);
        ArgumentNullException.ThrowIfNull(note);

        var trimmed = note.Trim();
        if (by.IsAnonymous)
        {
            return Result.WithMessages<Access>(ValidationMessage.Error("Log in om toegang te vragen.", nameof(by)));
        }

        if (trimmed.Length > MaxNoteLength)
        {
            return Result.WithMessages<Access>(ValidationMessage.Error($"Je toelichting mag hooguit {MaxNoteLength} tekens lang zijn.", nameof(note)));
        }

        return new Access
        {
            Id = by.Id,
            Name = by.Name,
            Email = by.Email,
            Note = trimmed,
            State = preApproved ? AccessState.Approved : AccessState.Requested,
            RequestedOn = now,
            DecidedOn = preApproved ? now : default,
            DecidedBy = preApproved ? PreApproved : string.Empty,
        };
    }

    /// <summary>Applies an admin's decision.</summary>
    /// <param name="decision">Approve (from any state), decline (a waiting request) or revoke (an approval).</param>
    /// <param name="admin">The admin's account id.</param>
    /// <param name="now">The current UTC time.</param>
    /// <returns>The new state, or why the decision doesn't apply.</returns>
    public Result<Access> Decide(AccessDecision decision, string admin, DateTime now)
    {
        var next = decision switch
        {
            AccessDecision.Approve => AccessState.Approved,
            AccessDecision.Decline when State == AccessState.Requested => AccessState.Declined,
            AccessDecision.Revoke when State == AccessState.Approved => AccessState.Revoked,
            _ => AccessState.None,
        };

        if (next == AccessState.None)
        {
            return Result.WithMessages<Access>(ValidationMessage.Error("Dat kan niet in deze stand.", nameof(decision)));
        }

        return this with { State = next, DecidedOn = now, DecidedBy = admin };
    }
}
