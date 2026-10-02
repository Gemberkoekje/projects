namespace SpaceTraders.Application.Ports;

/// <summary>
/// The API refused an extraction with a survey because of the survey itself: it is exhausted, it
/// expired, or its signature doesn't verify (PLAN.md slice 6.4). The survey can't be used again, and the
/// ship can extract without it.
/// </summary>
public sealed class SurveyRefusedException : Exception
{
    /// <summary>The API's error code for an exhausted survey.</summary>
    public const int ExhaustedErrorCode = 4224;

    /// <summary>The API's error code for an expired survey.</summary>
    public const int ExpiredErrorCode = 4221;

    /// <summary>The API's error code for a survey whose signature doesn't verify.</summary>
    public const int NotVerifiedErrorCode = 4220;

    /// <summary>Creates the exception.</summary>
    /// <param name="signature">The refused survey's signature.</param>
    /// <param name="errorCode">The API's error code.</param>
    /// <param name="innerException">The API error.</param>
    public SurveyRefusedException(string signature, int errorCode, Exception innerException)
        : base($"The API refused survey {signature} ({ReasonFor(errorCode)}).", innerException)
    {
        Signature = signature;
        ErrorCode = errorCode;
    }

    /// <summary>The refused survey's signature.</summary>
    public string Signature { get; }

    /// <summary>The API's error code.</summary>
    public int ErrorCode { get; }

    /// <summary>Why, in a word for the journal: <c>exhausted</c>, <c>expired</c> or <c>not_verified</c>.</summary>
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
        _ => "not_verified",
    };
}
