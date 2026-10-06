using System.Globalization;
using System.Text.Json.Serialization;

namespace SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Fleet;

public sealed class JettisonResult
{
    [JsonPropertyName("cargo")]
    required public ShipCargo Cargo { get; init; }
}

/// <summary>The answer to a cargo transfer: the transferring ship's cargo after it (slice 6.18).</summary>
public sealed class TransferCargoResult
{
    [JsonPropertyName("cargo")]
    required public ShipCargo Cargo { get; init; }
}

public sealed class NegotiateContractResult
{
    [JsonPropertyName("contract")]
    required public Contracts.Contract Contract { get; init; }
}

public sealed class PatchShipNavResult
{
    [JsonPropertyName("nav")]
    required public ShipNav Nav { get; init; }
}

public sealed class Survey
{
    [JsonPropertyName("signature")]
    required public string Signature { get; init; }

    [JsonPropertyName("symbol")]
    required public string Symbol { get; init; }

    [JsonPropertyName("deposits")]
    required public IReadOnlyList<SurveyDeposit> Deposits { get; init; }

    [JsonPropertyName("expiration")]
    public DateTimeOffset Expiration { get; init; }

    [JsonPropertyName("size")]
    required public string Size { get; init; }
}

public sealed class SurveyDeposit
{
    [JsonPropertyName("symbol")]
    required public string Symbol { get; init; }
}

public sealed class SurveyResult
{
    [JsonPropertyName("surveys")]
    required public IReadOnlyList<Survey> Surveys { get; init; }

    [JsonPropertyName("cooldown")]
    required public Cooldown Cooldown { get; init; }
}

public sealed class ExtractWithSurveyRequest
{
    [JsonPropertyName("signature")]
    required public string Signature { get; init; }

    [JsonPropertyName("symbol")]
    required public string Symbol { get; init; }

    [JsonPropertyName("deposits")]
    required public IReadOnlyList<SurveyDeposit> Deposits { get; init; }

    /// <summary>
    /// The expiry as the API wrote it: UTC, milliseconds and a <c>Z</c>. A <see cref="DateTimeOffset"/>
    /// goes out with an offset (<c>+00:00</c>) instead (B51).
    /// </summary>
    [JsonPropertyName("expiration")]
    required public string Expiration { get; init; }

    [JsonPropertyName("size")]
    required public string Size { get; init; }

    /// <summary>Writes an expiry as the API does (JavaScript's <c>toISOString</c>).</summary>
    /// <param name="expiration">The survey's expiry.</param>
    /// <returns>For example <c>2026-10-02T15:44:51.937Z</c>.</returns>
    public static string FormatExpiration(DateTimeOffset expiration)
        => expiration.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
}

public sealed class SiphonResult
{
    [JsonPropertyName("siphon")]
    required public Siphon Siphon { get; init; }

    [JsonPropertyName("cargo")]
    required public ShipCargo Cargo { get; init; }

    [JsonPropertyName("cooldown")]
    required public Cooldown Cooldown { get; init; }
}

public sealed class Siphon
{
    [JsonPropertyName("shipSymbol")]
    required public string ShipSymbol { get; init; }

    [JsonPropertyName("yield")]
    required public ExtractionYield Yield { get; init; }
}

public sealed class WarpResult
{
    [JsonPropertyName("nav")]
    required public ShipNav Nav { get; init; }

    [JsonPropertyName("fuel")]
    required public ShipFuel Fuel { get; init; }
}

public sealed class JumpResult
{
    [JsonPropertyName("nav")]
    required public ShipNav Nav { get; init; }

    [JsonPropertyName("cooldown")]
    required public Cooldown Cooldown { get; init; }

    [JsonPropertyName("transaction")]
    public MarketTransaction? Transaction { get; init; }

    [JsonPropertyName("agent")]
    public Agents.Agent? Agent { get; init; }
}

public sealed class ChartResult
{
    [JsonPropertyName("chart")]
    required public ChartData Chart { get; init; }

    [JsonPropertyName("waypoint")]
    required public Systems.Waypoint Waypoint { get; init; }

    /// <summary>The agent after the chart, its credits with the chart's reward (the API's spec since 2025-04).</summary>
    [JsonPropertyName("agent")]
    public Agents.Agent? Agent { get; init; }
}

public sealed class ChartData
{
    [JsonPropertyName("waypointSymbol")]
    public string? WaypointSymbol { get; init; }

    [JsonPropertyName("submittedBy")]
    public string? SubmittedBy { get; init; }

    [JsonPropertyName("submittedOn")]
    public DateTimeOffset? SubmittedOn { get; init; }
}
