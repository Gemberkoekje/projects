namespace SpaceTraders.Application.Ports;

/// <summary>
/// The API refused a jump (exploring, asked on 2026-10-04): the answer was a client error, such as a gate still under
/// construction or one the ship's gate doesn't connect to. Sent again, it would fail again on every step, so the explore
/// plan leaves that gate alone for a while instead.
/// </summary>
public sealed class JumpRefusedException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="destinationWaypointSymbol">The gate the ship was to jump to.</param>
    /// <param name="errorCode">The API's error code; 0 when it gave none.</param>
    /// <param name="apiMessage">What the API said.</param>
    /// <param name="innerException">The API error.</param>
    public JumpRefusedException(string destinationWaypointSymbol, int errorCode, string apiMessage, Exception innerException)
        : base($"The API refused the jump to {destinationWaypointSymbol}: {apiMessage}", innerException)
    {
        DestinationWaypointSymbol = destinationWaypointSymbol;
        ErrorCode = errorCode;
    }

    /// <summary>The gate the ship was to jump to.</summary>
    public string DestinationWaypointSymbol { get; }

    /// <summary>The API's error code; 0 when it gave none.</summary>
    public int ErrorCode { get; }
}
