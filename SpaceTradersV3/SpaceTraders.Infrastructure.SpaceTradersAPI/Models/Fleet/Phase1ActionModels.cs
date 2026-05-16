using System.Text.Json.Serialization;

namespace SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Fleet;

public sealed class JettisonResult
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

    [JsonPropertyName("expiration")]
    public DateTimeOffset Expiration { get; init; }

    [JsonPropertyName("size")]
    required public string Size { get; init; }
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
}

public sealed class ChartResult
{
    [JsonPropertyName("chart")]
    required public ChartData Chart { get; init; }

    [JsonPropertyName("waypoint")]
    required public Systems.Waypoint Waypoint { get; init; }
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
