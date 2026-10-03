using SpaceTraders.Application.Mining;
using SpaceTraders.Application.Trading;

namespace SpaceTraders.Application.Roles;

/// <summary>
/// What a good sold at a market is worth beyond its price (PLAN.md slice 6.9, D39): "mining iron ore isn't that
/// profitable by itself, but iron ore getting refined to iron getting made to machinery is very profitable." When the
/// market makes a pricier good from it (it imports the good and exports something made from it, by the game's
/// production chains, as D15 reads them), a unit counts a share of the price difference, and that share again of the
/// next step: the pricier good at the market in the system that makes the most of it in turn. A step counts fully
/// while its market is SCARCE of the input, less as the supply grows, and not at all at ABUNDANT, where more of it
/// doesn't raise production. Only the role board's comparison reads it: the plans still choose within a role by D15
/// and D28.
/// </summary>
/// <remarks>Built per system and pass; it remembers each market and good it has worked out.</remarks>
public sealed class ChainValues
{
    private const string ImportType = "IMPORT";

    private readonly TradeMarketMap _map;
    private readonly double _share;
    private readonly Dictionary<(string Market, string Good), double> _perUnit = [];
    private readonly Dictionary<string, double> _nextStep = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Creates the values for one system.</summary>
    /// <param name="map">The system: its markets, prices and production chains.</param>
    /// <param name="share">The share of each step's price difference that counts (<c>Roles.ChainValueSharePercent</c>, 0 to 1).</param>
    public ChainValues(TradeMarketMap map, double share)
    {
        ArgumentNullException.ThrowIfNull(map);
        _map = map;
        _share = Math.Clamp(share, 0, 1);
    }

    /// <summary>
    /// How much a step of the chain counts at a market's supply of its input: SCARCE fully, LIMITED three quarters,
    /// MODERATE half, HIGH a quarter, ABUNDANT (or unknown) not at all.
    /// </summary>
    /// <param name="supply">The market's supply of the input, as the API gives it.</param>
    /// <returns>A factor from 0 to 1.</returns>
    public static double SupplyFactor(string supply)
    {
        var rank = MiningPlanner.SupplyRank(supply);
        const int Abundant = 4;
        return rank >= Abundant ? 0 : (Abundant - rank) / (double)Abundant;
    }

    /// <summary>What one unit of a good sold at a market is worth beyond its price; 0 when the market makes nothing pricier from it.</summary>
    /// <param name="market">The market it is sold at.</param>
    /// <param name="good">The good.</param>
    /// <returns>Credits per unit.</returns>
    public double PerUnit(string market, string good)
    {
        if (_share <= 0)
        {
            return 0;
        }

        if (!_perUnit.TryGetValue((market, good), out var value))
        {
            value = Step(market, good, out var made) is var first and > 0
                ? _share * (first + (_share * NextStep(made)))
                : 0;
            _perUnit[(market, good)] = value;
        }

        return value;
    }

    /// <summary>
    /// One step: at a market that imports the good and makes a pricier good from it, the price difference (what the
    /// market charges for each, as D15 compares them) times how short the market is of the good.
    /// </summary>
    private double Step(string market, string good, out string made)
    {
        made = string.Empty;
        if (!_map.TryGetGood(market, good, out var input)
            || !input.Type.Equals(ImportType, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        var factor = SupplyFactor(input.Supply);
        made = _map.PricierGoodMadeFrom(market, good);
        if (factor <= 0 || made.Length == 0 || !_map.TryGetGood(market, made, out var output))
        {
            return 0;
        }

        return factor * (output.PurchasePrice - input.PurchasePrice);
    }

    /// <summary>The best second step for a good made at the first: the market in the system that makes the most of it.</summary>
    private double NextStep(string good)
    {
        if (good.Length == 0)
        {
            return 0;
        }

        if (!_nextStep.TryGetValue(good, out var best))
        {
            best = _map.MarketWaypoints.Select(market => Step(market, good, out _)).DefaultIfEmpty(0).Max();
            _nextStep[good] = best;
        }

        return best;
    }
}
