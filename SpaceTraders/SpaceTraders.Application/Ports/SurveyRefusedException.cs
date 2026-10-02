namespace SpaceTraders.Application.Ports;

/// <summary>
/// The API refused an extraction with a survey because of the survey itself: it is exhausted, it
/// expired, its signature doesn't verify (PLAN.md slice 6.4), or the API couldn't read it (B51). The
/// survey can't be used again, and the ship can extract without it.
/// </summary>
public sealed class SurveyRefusedException : Exception
{
    /// <summary>The API's error code for an exhausted survey.</summary>
    public const int ExhaustedErrorCode = 4224;

    /// <summary>The API's error code for an expired survey.</summary>
    public const int ExpiredErrorCode = 4221;

    /// <summary>The API's error code for a survey whose signature doesn't verify.</summary>
    public const int NotVerifiedErrorCode = 4220;

    /// <summary>
    /// HTTP 422 without a game error code: the API couldn't read the survey it was sent (B51). Its
    /// <c>data</c>, in <see cref="Detail"/>, says why.
    /// </summary>
    public const int RejectedErrorCode = 422;

    /// <summary>Creates the exception.</summary>
    /// <param name="signature">The refused survey's signature.</param>
    /// <param name="errorCode">The API's error code.</param>
    /// <param name="innerException">The API error.</param>
    public SurveyRefusedException(string signature, int errorCode, Exception innerException)
        : this(signature, errorCode, innerException, string.Empty)
    {
    }

    /// <summary>Creates the exception, with what the API said about the survey.</summary>
    /// <param name="signature">The refused survey's signature.</param>
    /// <param name="errorCode">The API's error code.</param>
    /// <param name="innerException">The API error.</param>
    /// <param name="detail">The API's response body, or an empty string.</param>
    public SurveyRefusedException(string signature, int errorCode, Exception innerException, string detail)
        : base($"The API refused survey {signature} ({ReasonFor(errorCode)}).", innerException)
    {
        Signature = signature;
        ErrorCode = errorCode;
        Detail = detail;
    }

    /// <summary>The refused survey's signature.</summary>
    public string Signature { get; }

    /// <summary>The API's error code.</summary>
    public int ErrorCode { get; }

    /// <summary>The API's response body, kept for the log; empty when there is none.</summary>
    public string Detail { get; }

    /// <summary>
    /// Why, in a word for the journal: <c>exhausted</c>, <c>expired</c>, <c>not_verified</c> or
    /// <c>rejected</c>.
    /// </summary>
    public string Reason => ReasonFor(ErrorCode);

    /// <summary>Whether an API error code means the survey itself was refused.</summary>
    /// <param name="errorCode">The API's error code.</param>
    /// <returns>True for the exhausted, expired and not-verified codes.</returns>
    public static bool IsSurveyRefusal(int errorCode)
        => errorCode is ExhaustedErrorCode or ExpiredErrorCode or NotVerifiedErrorCode;

    private static string ReasonFor(int errorCode) => errorCode switch
    {
        ExhaustedErrorCode => "exhausted",
        ExpiredErrorCode => "expired",
        RejectedErrorCode => "rejected",
        _ => "not_verified",
    };
}
