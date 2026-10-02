using System.Text.Json.Serialization;

namespace SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Markets;

/// <summary>
/// The game's production chains (<c>GET market/supply-chain</c>): for each good a market exports,
/// the goods it imports to make it. Static per game version.
/// </summary>
public sealed class SupplyChain
{
    /// <summary>Each exported good, with the goods it is made from.</summary>
    [JsonPropertyName("exportToImportMap")]
    public IReadOnlyDictionary<string, IReadOnlyList<string>>? ExportToImportMap { get; init; }
}
