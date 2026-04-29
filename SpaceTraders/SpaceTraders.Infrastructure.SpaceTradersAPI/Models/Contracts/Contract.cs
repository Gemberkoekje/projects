using System.Text.Json.Serialization;

namespace SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Contracts;

public sealed class Contract
{
    [JsonPropertyName("id")]
    required public string Id { get; init; }

    [JsonPropertyName("factionSymbol")]
    required public string FactionSymbol { get; init; }

    [JsonPropertyName("type")]
    required public string Type { get; init; }

    [JsonPropertyName("accepted")]
    public bool Accepted { get; init; }

    [JsonPropertyName("fulfilled")]
    public bool Fulfilled { get; init; }

    [JsonPropertyName("expiration")]
    public DateTimeOffset? Expiration { get; init; }

    [JsonPropertyName("deadlineToAccept")]
    public DateTimeOffset? DeadlineToAccept { get; init; }

    [JsonPropertyName("terms")]
    public ContractTerms? Terms { get; init; }
}

public sealed class ContractTerms
{
    [JsonPropertyName("deadline")]
    public DateTimeOffset? Deadline { get; init; }

    [JsonPropertyName("payment")]
    public ContractPayment? Payment { get; init; }

    [JsonPropertyName("deliver")]
    public IReadOnlyList<ContractDeliverGood>? Deliver { get; init; }
}

public sealed class ContractPayment
{
    [JsonPropertyName("onAccepted")]
    public long OnAccepted { get; init; }

    [JsonPropertyName("onFulfilled")]
    public long OnFulfilled { get; init; }
}

public sealed class ContractDeliverGood
{
    [JsonPropertyName("tradeSymbol")]
    required public string TradeSymbol { get; init; }

    [JsonPropertyName("destinationSymbol")]
    required public string DestinationSymbol { get; init; }

    [JsonPropertyName("unitsRequired")]
    public int UnitsRequired { get; init; }

    [JsonPropertyName("unitsFulfilled")]
    public int UnitsFulfilled { get; init; }
}
