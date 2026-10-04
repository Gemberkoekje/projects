using System.Globalization;
using SpaceTraders.Application.Automation;
using SpaceTraders.Application.Health;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Trading;

namespace SpaceTraders.Application.Roles;

/// <summary>What the role board's choices read from the settings (PLAN.md slice 6.9).</summary>
public sealed record RoleSettings
{
    /// <summary>The setting that holds the minutes between evaluations of the whole fleet (D41).</summary>
    public const string ReconsiderMinutesSetting = "Roles.ReconsiderMinutes";

    /// <summary>The setting that holds the head start of a ship's current role, in percent (D41).</summary>
    public const string HeadStartPercentSetting = "Roles.HeadStartPercent";

    /// <summary>The setting that holds the share of each production step's price difference that counts, in percent (D39).</summary>
    public const string ChainValueSharePercentSetting = "Roles.ChainValueSharePercent";

    /// <summary>The minutes between evaluations when the setting gives none (D41).</summary>
    internal const int DefaultReconsiderMinutes = 10;

    /// <summary>The head start when the setting gives none: 20% (D41).</summary>
    internal const int DefaultHeadStartPercent = 20;

    /// <summary>The production chains' share when the setting gives none: 50% (D39).</summary>
    internal const int DefaultChainValueSharePercent = 50;

    /// <summary>The setting that holds how many ships get the construction role while the jump gate needs materials (D60).</summary>
    public const string ConstructionShipsSetting = "Construction.Ships";

    /// <summary>The ships with the construction role when the setting gives none: one (D60).</summary>
    internal const int DefaultConstructionShips = 1;

    /// <summary>The plans whose ships the board gives roles, and the contract plan.</summary>
    private static readonly AutomationPlan[] Weighed =
    [
        AutomationPlan.Contract,
        AutomationPlan.Survey,
        AutomationPlan.Mining,
        AutomationPlan.Siphon,
        AutomationPlan.Construction,
        AutomationPlan.Trading,
        AutomationPlan.SpareTime,
    ];

    /// <summary>Creates the settings of one evaluation.</summary>
    /// <param name="Switches">The plans that are on, of those the board weighs.</param>
    /// <param name="Reconsider">The time between evaluations of the whole fleet.</param>
    /// <param name="HeadStart">How much more a ship's current role counts: 0.2 for 20%.</param>
    /// <param name="ChainShare">The share of each production step's price difference that counts, 0 to 1.</param>
    /// <param name="MinProfitPerUnit">The profit per unit, after fuel, a trade trip must earn (D14).</param>
    /// <param name="FuelReserveCredits">The credits cargo must leave for fuel (D24).</param>
    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public RoleSettings(
        IReadOnlySet<AutomationPlan> Switches,
        TimeSpan Reconsider,
        double HeadStart,
        double ChainShare,
        int MinProfitPerUnit,
        long FuelReserveCredits)
    {
        this.Switches = Switches;
        this.Reconsider = Reconsider;
        this.HeadStart = HeadStart;
        this.ChainShare = ChainShare;
        this.MinProfitPerUnit = MinProfitPerUnit;
        this.FuelReserveCredits = FuelReserveCredits;
    }

    /// <summary>The plans that are on, of those the board weighs.</summary>
    public required IReadOnlySet<AutomationPlan> Switches { get; init; }

    /// <summary>The time between evaluations of the whole fleet.</summary>
    public required TimeSpan Reconsider { get; init; }

    /// <summary>How much more a ship's current role counts: 0.2 for 20%.</summary>
    public required double HeadStart { get; init; }

    /// <summary>The share of each production step's price difference that counts, 0 to 1.</summary>
    public required double ChainShare { get; init; }

    /// <summary>The profit per unit, after fuel, a trade trip must earn (D14).</summary>
    public required int MinProfitPerUnit { get; init; }

    /// <summary>The credits cargo must leave for fuel (D24).</summary>
    public required long FuelReserveCredits { get; init; }

    /// <summary>How many ships per system get the construction role while its jump gate needs materials (D60).</summary>
    public int ConstructionShips { get; init; } = DefaultConstructionShips;

    /// <summary>
    /// The systems whose jump gate still needs materials, as the construction cache has it (slice 6.6): only there may a ship
    /// take the construction role. Empty until the role board reads the sites.
    /// </summary>
    public IReadOnlySet<string> ConstructionSystems { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Reads the settings.</summary>
    /// <param name="settings">The settings.</param>
    /// <param name="cancellationToken">Stops the reads.</param>
    /// <returns>The settings of one evaluation.</returns>
    public static Task<RoleSettings> ReadAsync(ISettingsRepository settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return ReadSettingsAsync(settings, cancellationToken);
    }

    private static async Task<RoleSettings> ReadSettingsAsync(ISettingsRepository settings, CancellationToken cancellationToken)
    {
        var switches = new HashSet<AutomationPlan>();
        foreach (var plan in Weighed)
        {
            if (await settings.IsPlanEnabledAsync(plan, cancellationToken))
            {
                switches.Add(plan);
            }
        }

        return new RoleSettings(
            switches,
            TimeSpan.FromMinutes(await settings.ThresholdAsync(ReconsiderMinutesSetting, DefaultReconsiderMinutes, cancellationToken)),
            await PercentAsync(settings, HeadStartPercentSetting, DefaultHeadStartPercent, cancellationToken) / 100.0,
            Math.Min(100, await PercentAsync(settings, ChainValueSharePercentSetting, DefaultChainValueSharePercent, cancellationToken)) / 100.0,
            Math.Max(0, await settings.GetAsync<int>(TradeContextReader.MinProfitPerUnitSetting, cancellationToken)),
            Math.Max(0, await settings.GetAsync<long>(TradeContextReader.FuelReserveCreditsSetting, cancellationToken)))
        {
            ConstructionShips = await settings.ThresholdAsync(ConstructionShipsSetting, DefaultConstructionShips, cancellationToken),
        };
    }

    /// <summary>
    /// The roles a ship could take whose plan is on (D38): surveying with the survey plan; mining with the mining plan,
    /// or while the contract wants ore (D40); siphoning with the siphon plan; trading with the trading plan; constructing
    /// with the construction plan, while the jump gate of the ship's system needs materials (slice 6.6).
    /// </summary>
    /// <param name="ship">The ship.</param>
    /// <param name="contractWantsOre">Whether the contract plan's contract still wants ore.</param>
    /// <returns>Its roles, in the order survey, mine, siphon, trade, construct.</returns>
    public IReadOnlyList<FleetRole> Available(ShipModel ship, bool contractWantsOre)
        => [.. FleetRoles.PotentialRoles(ship).Where(role => role switch
        {
            FleetRole.Survey => Switches.Contains(AutomationPlan.Survey),
            FleetRole.Mine => Switches.Contains(AutomationPlan.Mining) || contractWantsOre,
            FleetRole.Siphon => Switches.Contains(AutomationPlan.Siphon),
            FleetRole.Trade => Switches.Contains(AutomationPlan.Trading),
            FleetRole.Construct => Switches.Contains(AutomationPlan.Construction) && ConstructionSystems.Contains(ship.SystemSymbol ?? string.Empty),
            _ => false,
        })];

    /// <summary>A percentage setting: 0 or more; missing or unreadable means the default.</summary>
    private static async Task<int> PercentAsync(ISettingsRepository settings, string key, int defaultValue, CancellationToken cancellationToken)
    {
        var raw = await settings.GetRawAsync(key, cancellationToken);
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value >= 0 ? value : defaultValue;
    }
}
