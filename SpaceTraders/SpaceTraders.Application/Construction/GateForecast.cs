using SpaceTraders.Application.Automation;
using SpaceTraders.Application.Trading;

namespace SpaceTraders.Application.Construction;

/// <summary>
/// When the home jump gate will be finished, by the slowest of three limits (PLAN.md slice 6.55, D143), asked on 2026-11-09 for
/// rule 3, "Any ship bought should be bought on the idea that buying the ship will finish the gate faster than without it", and
/// "build it, forecast first": the forecast only observes; ship purchases don't read it yet.
/// <list type="bullet">
///   <item><b>money</b>: what the units the gate still needs cost at their cheapest seller in its system, times
///   <see cref="PriceCreep"/> for the price rising as they are bought, less what a load may spend now, over what the fleet's
///   trades earned an hour lately;</item>
///   <item><b>supply</b>: for each material, the units it still needs over what its sellers give an hour without falling
///   short: <see cref="VolumesPerHour"/> trade volumes an hour, or what was bought of it an hour lately when that is more; the
///   slowest material;</item>
///   <item><b>haul</b>: the units still needed over what the builders carry an hour, a full hold every
///   <see cref="RoundTripMinutes"/> minutes.</item>
/// </list>
/// The units on their way count as delivered. On 2026-11-08 at X1-VP96-F46, about 280 FAB_MATS bought in an hour and a half
/// took it from ABUNDANT to LIMITED and its price from 1,064 to 1,955, and it was back at MODERATE two hours later, about 80 an
/// hour at a trade volume of 20 to 43; ADVANCED_CIRCUITRY at D41 about 35 an hour at 20; a 40-unit load's round trip took 12
/// to 20 minutes.
/// </summary>
public static class GateForecast
{
    /// <summary>What the remaining units cost on average, as a share of their seller's price now: buying moves it up.</summary>
    public const double PriceCreep = 1.25;

    /// <summary>The trade volumes an hour a seller gives without falling short, as a first estimate.</summary>
    public const double VolumesPerHour = 2;

    /// <summary>A builder's round trip: from the gate to a seller and back with a full hold.</summary>
    public const int RoundTripMinutes = 15;

    /// <summary>The hours a limit that can't be met reads: no seller, no earnings, no builder.</summary>
    public const double Never = 999;

    /// <summary>The limit no load is left for: every unit the gate needs is delivered or on its way.</summary>
    public const string Done = "done";

    /// <summary>The money limit.</summary>
    public const string Money = "money";

    /// <summary>The supply limit.</summary>
    public const string Supply = "supply";

    /// <summary>The haul limit.</summary>
    public const string Haul = "haul";

    private const string ExportType = "EXPORT";
    private const string ExchangeType = "EXCHANGE";

    /// <summary>The forecast of a gate (<see cref="GateForecast"/>).</summary>
    /// <param name="map">The gate's system, as the construction plan reads it.</param>
    /// <param name="needs">What the gate still needs, with what is on its way (<see cref="ConstructionPlanner.Needs"/>).</param>
    /// <param name="systemSymbol">The gate's system.</param>
    /// <param name="spendable">What a load may spend now (D137).</param>
    /// <param name="earnedPerHour">What the fleet's trades earned an hour lately, after what they paid for cargo and fuel.</param>
    /// <param name="builderCapacity">The holds of the builders in the gate's system, together.</param>
    /// <param name="boughtPerHour">What was bought of each material an hour lately, by symbol; a material it leaves out, none.</param>
    /// <param name="now">When the forecast is made.</param>
    /// <returns>The forecast.</returns>
    public static GateForecastState Of(
        TradeMarketMap map,
        IReadOnlyList<MaterialNeed> needs,
        string systemSymbol,
        long spendable,
        double earnedPerHour,
        int builderCapacity,
        IReadOnlyDictionary<string, double> boughtPerHour,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(needs);
        ArgumentNullException.ThrowIfNull(systemSymbol);
        ArgumentNullException.ThrowIfNull(boughtPerHour);

        var left = needs.Where(need => need.Remaining > 0).ToList();
        if (left.Count == 0)
        {
            return new GateForecastState { Binding = Done, EarnedPerHour = earnedPerHour, ComputedAt = now };
        }

        var markets = map.MarketWaypoints
            .Where(market => map.SystemOf(market).Equals(systemSymbol, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var cost = 0d;
        var materials = new List<GateForecastMaterialState>();
        foreach (var need in left)
        {
            var seller = markets
                .Select(market => map.TryGetGood(market, need.TradeSymbol, out var good) ? good : null)
                .Where(good => good is not null
                    && good.PurchasePrice > 0
                    && (good.Type.Equals(ExportType, StringComparison.OrdinalIgnoreCase) || good.Type.Equals(ExchangeType, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(good => good!.PurchasePrice)
                .FirstOrDefault();
            var perHour = seller is null ? 0 : Math.Max(VolumesPerHour * Math.Max(1, seller.TradeVolume), boughtPerHour.GetValueOrDefault(need.TradeSymbol));
            cost += seller is null ? 0 : need.Remaining * seller.PurchasePrice * PriceCreep;
            materials.Add(new GateForecastMaterialState
            {
                TradeSymbol = need.TradeSymbol,
                Remaining = need.Remaining,
                SupplyPerHour = Math.Round(perHour, 1),
                SupplyHours = perHour > 0 ? Capped(need.Remaining / perHour) : Never,
            });
        }

        var money = cost <= spendable ? 0 : earnedPerHour > 0 ? Capped((cost - spendable) / earnedPerHour) : Never;
        var supply = materials.Max(material => material.SupplyHours);
        var units = left.Sum(need => need.Remaining);
        var haul = builderCapacity > 0 ? Capped(units / (builderCapacity * (60d / RoundTripMinutes))) : Never;
        var eta = Math.Max(money, Math.Max(supply, haul));
        return new GateForecastState
        {
            EtaHours = eta,
            MoneyHours = money,
            SupplyHours = supply,
            HaulHours = haul,
            Binding = money >= supply && money >= haul ? Money : supply >= haul ? Supply : Haul,
            RemainingCost = (long)Math.Round(cost),
            EarnedPerHour = Math.Round(earnedPerHour),
            Materials = materials,
            ComputedAt = now,
        };
    }

    private static double Capped(double hours) => Math.Round(Math.Min(Never, hours), 2);
}
