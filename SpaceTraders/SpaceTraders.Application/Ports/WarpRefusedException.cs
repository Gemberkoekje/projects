namespace SpaceTraders.Application.Ports;

/// <summary>
/// The API refused a warp (PLAN.md slice 6.31): the answer was a client error, such as a tank short of the fuel or a system out
/// of the drive's range. Sent again, it would fail again on every step, so the ways leave that system alone for a while
/// (<c>WarpRefusals</c>) instead.
/// </summary>
public sealed class WarpRefusedException : Exception
{
    /// <summary>The API's error code for a flight or a warp the fuel aboard doesn't pay for (<c>navigateInsufficientFuelError</c>).</summary>
    public const int InsufficientFuel = 4203;

    /// <summary>Creates the exception.</summary>
    /// <param name="destinationWaypointSymbol">The waypoint the ship was to warp to.</param>
    /// <param name="errorCode">The API's error code; 0 when it gave none.</param>
    /// <param name="apiMessage">What the API said.</param>
    /// <param name="innerException">The API error.</param>
    public WarpRefusedException(string destinationWaypointSymbol, int errorCode, string apiMessage, Exception innerException)
        : base($"The API refused the warp to {destinationWaypointSymbol}: {apiMessage}", innerException)
    {
        DestinationWaypointSymbol = destinationWaypointSymbol;
        ErrorCode = errorCode;
    }

    /// <summary>The waypoint the ship was to warp to.</summary>
    public string DestinationWaypointSymbol { get; }

    /// <summary>The API's error code; 0 when it gave none.</summary>
    public int ErrorCode { get; }
}
