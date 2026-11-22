namespace SpaceTraders.Application.Ports;

/// <summary>
/// The API refused a sale because the ship holds fewer units of the good than the bot asked to sell (B95). That is what a
/// sale whose answer was lost leaves behind: the API carried it out, the bot's cargo still has the units, and the same sale
/// would fail again on every step. The port raises it for that refusal only; other refusals stay the API's error.
/// </summary>
/// <remarks>
/// The API's message is matched, not its code: "Failed to update ship cargo. Ship SPECTER-1F4 cargo does not contain 45
/// unit(s) of AMMUNITION. Ship has 40 unit(s) of AMMUNITION." (2026-11-21).
/// </remarks>
public sealed class CargoRefusedException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="shipSymbol">The ship that was to sell.</param>
    /// <param name="tradeSymbol">The good it was to sell.</param>
    /// <param name="units">The units it was to sell.</param>
    /// <param name="apiMessage">What the API said.</param>
    /// <param name="innerException">The API error.</param>
    public CargoRefusedException(string shipSymbol, string tradeSymbol, int units, string apiMessage, Exception innerException)
        : base($"The API refused ship {shipSymbol}'s sale of {units} {tradeSymbol}: {apiMessage}", innerException)
    {
        ShipSymbol = shipSymbol;
        TradeSymbol = tradeSymbol;
        Units = units;
    }

    /// <summary>The ship that was to sell.</summary>
    public string ShipSymbol { get; }

    /// <summary>The good it was to sell.</summary>
    public string TradeSymbol { get; }

    /// <summary>The units it was to sell.</summary>
    public int Units { get; }

    /// <summary>Whether <paramref name="apiMessage"/> refuses a sale for cargo the ship doesn't hold.</summary>
    /// <param name="apiMessage">What the API said.</param>
    /// <returns><c>true</c> when it says the ship's cargo does not contain what was to be sold.</returns>
    public static bool IsCargoShort(string apiMessage)
        => apiMessage.Contains("cargo does not contain", StringComparison.OrdinalIgnoreCase);
}
