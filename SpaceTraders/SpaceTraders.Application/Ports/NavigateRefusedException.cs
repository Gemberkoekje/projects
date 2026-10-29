namespace SpaceTraders.Application.Ports;

/// <summary>
/// The API refused a navigation because the ship isn't where the bot stored it (B83): it is in transit, or already at the
/// destination. That is what a navigate whose answer was lost leaves behind: the API carried it out, the bot still has the
/// ship at the waypoint it left, and the same navigate would fail again on every step. The port raises it only for the
/// codes below; other refusals, such as too little fuel, stay the API's error.
/// </summary>
public sealed class NavigateRefusedException : Exception
{
    /// <summary>The API's <c>navigateInTransitError</c>: the ship is in transit.</summary>
    public const int InTransitErrorCode = 4200;

    /// <summary>The API's <c>navigateSameDestinationError</c>: the ship is already at the destination.</summary>
    public const int SameDestinationErrorCode = 4204;

    /// <summary>The API's <c>shipInTransitError</c>: the ship is in transit.</summary>
    public const int ShipInTransitErrorCode = 4214;

    /// <summary>Creates the exception.</summary>
    /// <param name="destinationWaypointSymbol">The waypoint the ship was to fly to.</param>
    /// <param name="errorCode">The API's error code, one of the codes above.</param>
    /// <param name="apiMessage">What the API said.</param>
    /// <param name="innerException">The API error.</param>
    public NavigateRefusedException(string destinationWaypointSymbol, int errorCode, string apiMessage, Exception innerException)
        : base($"The API refused the navigation to {destinationWaypointSymbol}: {apiMessage}", innerException)
    {
        DestinationWaypointSymbol = destinationWaypointSymbol;
        ErrorCode = errorCode;
    }

    /// <summary>The waypoint the ship was to fly to.</summary>
    public string DestinationWaypointSymbol { get; }

    /// <summary>The API's error code, one of the codes above.</summary>
    public int ErrorCode { get; }

    /// <summary>Whether <paramref name="errorCode"/> says the ship is in transit or already at the destination.</summary>
    /// <param name="errorCode">The API's error code.</param>
    /// <returns><c>true</c> for <see cref="InTransitErrorCode"/>, <see cref="SameDestinationErrorCode"/> and <see cref="ShipInTransitErrorCode"/>.</returns>
    public static bool IsShipElsewhere(int errorCode)
        => errorCode is InTransitErrorCode or SameDestinationErrorCode or ShipInTransitErrorCode;
}
