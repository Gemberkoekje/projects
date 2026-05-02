using SpaceTraders.Domain.Enums;

namespace SpaceTraders.Application.Ports;

public sealed record AgentModel
{
    public required string Symbol { get; init; }

    public string? AccountId { get; init; }

    public string? HeadquartersSymbol { get; init; }

    public required long Credits { get; init; }

    public required string StartingFaction { get; init; }

    public required int ShipCount { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public AgentModel(string Symbol, string? AccountId, string? HeadquartersSymbol, long Credits, string StartingFaction, int ShipCount)
    {
        this.Symbol = Symbol;
        this.AccountId = AccountId;
        this.HeadquartersSymbol = HeadquartersSymbol;
        this.Credits = Credits;
        this.StartingFaction = StartingFaction;
        this.ShipCount = ShipCount;
    }
}

public sealed record ServerStatusModel
{
    public required string Status { get; init; }

    public required string Version { get; init; }

    public string? ResetDateRaw { get; init; }

    public DateTimeOffset? ResetDate { get; init; }

    public string? NextResetRaw { get; init; }

    public DateTimeOffset? NextResetAt { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public ServerStatusModel(
        string Status,
        string Version,
        string? ResetDateRaw,
        DateTimeOffset? ResetDate,
        string? NextResetRaw,
        DateTimeOffset? NextResetAt)
    {
        this.Status = Status;
        this.Version = Version;
        this.ResetDateRaw = ResetDateRaw;
        this.ResetDate = ResetDate;
        this.NextResetRaw = NextResetRaw;
        this.NextResetAt = NextResetAt;
    }
}

public sealed record NavModel
{
    public required string Status { get; init; }

    public required string SystemSymbol { get; init; }

    public required string WaypointSymbol { get; init; }

    public required string FlightMode { get; init; }

    public string? DestWaypointSymbol { get; init; }

    public DateTimeOffset? ArrivesAt { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public NavModel(string Status, string SystemSymbol, string WaypointSymbol, string FlightMode, string? DestWaypointSymbol, DateTimeOffset? ArrivesAt)
    {
        this.Status = Status;
        this.SystemSymbol = SystemSymbol;
        this.WaypointSymbol = WaypointSymbol;
        this.FlightMode = FlightMode;
        this.DestWaypointSymbol = DestWaypointSymbol;
        this.ArrivesAt = ArrivesAt;
    }
}

public sealed record FuelModel
{
    public required int Current { get; init; }

    public required int Capacity { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public FuelModel(int Current, int Capacity)
    {
        this.Current = Current;
        this.Capacity = Capacity;
    }
}

public sealed record CargoItemModel
{
    public required string Symbol { get; init; }

    public required int Units { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public CargoItemModel(string Symbol, int Units)
    {
        this.Symbol = Symbol;
        this.Units = Units;
    }
}

public sealed record CargoModel
{
    public required int Units { get; init; }

    public required int Capacity { get; init; }

    public IReadOnlyList<CargoItemModel>? Inventory { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public CargoModel(int Units, int Capacity, IReadOnlyList<CargoItemModel>? Inventory = null)
    {
        this.Units = Units;
        this.Capacity = Capacity;
        this.Inventory = Inventory;
    }
}

public sealed record NavigateActionResult
{
    public required NavModel Nav { get; init; }

    public FuelModel? Fuel { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public NavigateActionResult(NavModel Nav, FuelModel? Fuel)
    {
        this.Nav = Nav;
        this.Fuel = Fuel;
    }
}

public sealed record TradeActionResult
{
    public required string AgentSymbol { get; init; }

    public required long AgentCredits { get; init; }

    public required CargoModel Cargo { get; init; }

    public required long Revenue { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public TradeActionResult(string AgentSymbol, long AgentCredits, CargoModel Cargo, long Revenue)
    {
        this.AgentSymbol = AgentSymbol;
        this.AgentCredits = AgentCredits;
        this.Cargo = Cargo;
        this.Revenue = Revenue;
    }
}

public sealed record RefuelActionResult
{
    public required long AgentCredits { get; init; }

    public required FuelModel Fuel { get; init; }

    public required long Cost { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public RefuelActionResult(long AgentCredits, FuelModel Fuel, long Cost)
    {
        this.AgentCredits = AgentCredits;
        this.Fuel = Fuel;
        this.Cost = Cost;
    }
}

public sealed record ExtractionActionResult
{
    public required string YieldSymbol { get; init; }

    public required int YieldUnits { get; init; }

    public required CargoModel Cargo { get; init; }

    public required int CooldownSeconds { get; init; }

    /// <summary>The UTC time at which the extraction cooldown expires, as returned by the API.</summary>
    public DateTimeOffset? CooldownExpiresAt { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public ExtractionActionResult(string YieldSymbol, int YieldUnits, CargoModel Cargo, int CooldownSeconds, DateTimeOffset? CooldownExpiresAt = null)
    {
        this.YieldSymbol = YieldSymbol;
        this.YieldUnits = YieldUnits;
        this.Cargo = Cargo;
        this.CooldownSeconds = CooldownSeconds;
        this.CooldownExpiresAt = CooldownExpiresAt;
    }
}

public sealed record PurchaseShipActionResult
{
    public required AgentModel Agent { get; init; }

    public required string ShipSymbol { get; init; }

    public required NavModel ShipNav { get; init; }

    public required FuelModel ShipFuel { get; init; }

    public required long Cost { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public PurchaseShipActionResult(AgentModel Agent, string ShipSymbol, NavModel ShipNav, FuelModel ShipFuel, long Cost)
    {
        this.Agent = Agent;
        this.ShipSymbol = ShipSymbol;
        this.ShipNav = ShipNav;
        this.ShipFuel = ShipFuel;
        this.Cost = Cost;
    }
}

public sealed record ContractActionResult
{
    public required string ContractId { get; init; }

    public required string FactionSymbol { get; init; }

    public required string ContractType { get; init; }

    public required bool IsAccepted { get; init; }

    public required bool IsFulfilled { get; init; }

    public DateTimeOffset? Expiration { get; init; }

    public DateTimeOffset? DeadlineToAccept { get; init; }

    public DateTimeOffset? TermsDeadline { get; init; }

    public IReadOnlyList<ContractDeliverableModel> Deliverables { get; init; }

    public string? AgentSymbol { get; init; }

    public long? AgentCredits { get; init; }

    public CargoModel? ShipCargo { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public ContractActionResult(
        string ContractId,
        bool IsAccepted,
        bool IsFulfilled,
        string? AgentSymbol,
        long? AgentCredits,
        CargoModel? ShipCargo)
        : this(
            ContractId,
            string.Empty,
            string.Empty,
            IsAccepted,
            IsFulfilled,
            null,
            null,
            null,
            [],
            AgentSymbol,
            AgentCredits,
            ShipCargo)
    {
    }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public ContractActionResult(
        string ContractId,
        string FactionSymbol,
        string ContractType,
        bool IsAccepted,
        bool IsFulfilled,
        DateTimeOffset? Expiration,
        DateTimeOffset? DeadlineToAccept,
        DateTimeOffset? TermsDeadline,
        IReadOnlyList<ContractDeliverableModel>? Deliverables,
        string? AgentSymbol,
        long? AgentCredits,
        CargoModel? ShipCargo)
    {
        this.ContractId = ContractId;
        this.FactionSymbol = FactionSymbol;
        this.ContractType = ContractType;
        this.IsAccepted = IsAccepted;
        this.IsFulfilled = IsFulfilled;
        this.Expiration = Expiration;
        this.DeadlineToAccept = DeadlineToAccept;
        this.TermsDeadline = TermsDeadline;
        this.Deliverables = Deliverables ?? [];
        this.AgentSymbol = AgentSymbol;
        this.AgentCredits = AgentCredits;
        this.ShipCargo = ShipCargo;
    }
}

public sealed record SystemDataModel
{
    public required string Symbol { get; init; }

    public required string SectorSymbol { get; init; }

    public required string Type { get; init; }

    public required int X { get; init; }

    public required int Y { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public SystemDataModel(string Symbol, string SectorSymbol, string Type, int X, int Y)
    {
        this.Symbol = Symbol;
        this.SectorSymbol = SectorSymbol;
        this.Type = Type;
        this.X = X;
        this.Y = Y;
    }
}

public sealed record WaypointDataModel
{
    public required string Symbol { get; init; }

    public required string SystemSymbol { get; init; }

    public required string Type { get; init; }

    public required int X { get; init; }

    public required int Y { get; init; }

    public required bool HasMarket { get; init; }

    public required bool HasShipyard { get; init; }

    public string? TraitsJson { get; init; }

    public string? ModifiersJson { get; init; }

    public string? OrbitalsJson { get; init; }

    public string? ParentSymbol { get; init; }

    public bool IsUnderConstruction { get; init; }

    public string? ChartJson { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public WaypointDataModel(
        string Symbol,
        string SystemSymbol,
        string Type,
        int X,
        int Y,
        bool HasMarket,
        bool HasShipyard,
        string? TraitsJson = null,
        string? ModifiersJson = null,
        string? OrbitalsJson = null,
        string? ParentSymbol = null,
        bool IsUnderConstruction = false,
        string? ChartJson = null)
    {
        this.Symbol = Symbol;
        this.SystemSymbol = SystemSymbol;
        this.Type = Type;
        this.X = X;
        this.Y = Y;
        this.HasMarket = HasMarket;
        this.HasShipyard = HasShipyard;
        this.TraitsJson = TraitsJson;
        this.ModifiersJson = ModifiersJson;
        this.OrbitalsJson = OrbitalsJson;
        this.ParentSymbol = ParentSymbol;
        this.IsUnderConstruction = IsUnderConstruction;
        this.ChartJson = ChartJson;
    }
}

public sealed record ShipModel
{
    public required string Symbol { get; init; }

    public string? SystemSymbol { get; init; }

    public string? WaypointSymbol { get; init; }

    public string? Status { get; init; }

    public string? FlightMode { get; init; }

    public required int FuelCurrent { get; init; }

    public required int FuelCapacity { get; init; }

    public DateTimeOffset? ArrivesAt { get; init; } = null;

    public string? DestWaypointSymbol { get; init; } = null;

    public int CargoCurrent { get; init; } = 0;

    public int CargoCapacity { get; init; } = 0;

    public DateTimeOffset LastSyncedAt { get; init; } = default;

    public string ShipType { get; init; } = "";

    public IReadOnlyList<string>? MountSymbols { get; init; } = null;

    public IReadOnlyList<CargoItemModel>? CargoInventory { get; init; } = null;

    public string? ModulesJson { get; init; } = null;

    public string? FrameJson { get; init; } = null;

    public string? ReactorJson { get; init; } = null;

    public string? EngineJson { get; init; } = null;

    public DateTimeOffset? CooldownExpiresAt { get; init; } = null;

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public ShipModel(
        string Symbol,
        string? SystemSymbol,
        string? WaypointSymbol,
        string? Status,
        string? FlightMode,
        int FuelCurrent,
        int FuelCapacity,
        DateTimeOffset? ArrivesAt = null,
        string? DestWaypointSymbol = null,
        int CargoCurrent = 0,
        int CargoCapacity = 0,
        DateTimeOffset LastSyncedAt = default,
        string ShipType = "",
        IReadOnlyList<string>? MountSymbols = null,
        IReadOnlyList<CargoItemModel>? CargoInventory = null,
        string? ModulesJson = null,
        string? FrameJson = null,
        string? ReactorJson = null,
        string? EngineJson = null,
        DateTimeOffset? CooldownExpiresAt = null)
    {
        this.Symbol = Symbol;
        this.SystemSymbol = SystemSymbol;
        this.WaypointSymbol = WaypointSymbol;
        this.Status = Status;
        this.FlightMode = FlightMode;
        this.FuelCurrent = FuelCurrent;
        this.FuelCapacity = FuelCapacity;
        this.ArrivesAt = ArrivesAt;
        this.DestWaypointSymbol = DestWaypointSymbol;
        this.CargoCurrent = CargoCurrent;
        this.CargoCapacity = CargoCapacity;
        this.LastSyncedAt = LastSyncedAt;
        this.ShipType = ShipType;
        this.MountSymbols = MountSymbols;
        this.CargoInventory = CargoInventory;
        this.ModulesJson = ModulesJson;
        this.FrameJson = FrameJson;
        this.ReactorJson = ReactorJson;
        this.EngineJson = EngineJson;
        this.CooldownExpiresAt = CooldownExpiresAt;
    }

    public bool IsInTransit => ArrivesAt.HasValue && ArrivesAt.Value > TimeProvider.System.GetUtcNow();

    /// <summary>
    /// Local ship status mapped from the SpaceTraders API nav status string.
    /// </summary>
    public ShipLocalStatus LocalStatus => ShipLocalStatusMapper.FromApiStatus(Status);

    public bool HasMiningEquipment =>
        ShipType.Equals("SHIP_MINING_DRONE", StringComparison.OrdinalIgnoreCase) ||
        ShipType.Equals("SHIP_ORE_HOUND", StringComparison.OrdinalIgnoreCase) ||
        (MountSymbols ?? []).Any(m =>
            m.Contains("MINING", StringComparison.OrdinalIgnoreCase) ||
            m.Contains("SURVEYOR", StringComparison.OrdinalIgnoreCase));

    public bool HasSurveyEquipment =>
        (MountSymbols ?? []).Any(m => m.Contains("SURVEYOR", StringComparison.OrdinalIgnoreCase));

    public bool HasGasSiphonEquipment =>
        ShipType.Equals("SHIP_SIPHON_DRONE", StringComparison.OrdinalIgnoreCase) ||
        (MountSymbols ?? []).Any(m => m.Contains("GAS_SIPHON", StringComparison.OrdinalIgnoreCase));

    public bool HasGasProcessor =>
        !string.IsNullOrWhiteSpace(ModulesJson) && ModulesJson.Contains("MODULE_GAS_PROCESSOR", StringComparison.OrdinalIgnoreCase);

    public bool HasMineralProcessor =>
        !string.IsNullOrWhiteSpace(ModulesJson) && ModulesJson.Contains("MODULE_MINERAL_PROCESSOR", StringComparison.OrdinalIgnoreCase);

    public bool HasSensorArray =>
        (MountSymbols ?? []).Any(m => m.Contains("SENSOR_ARRAY", StringComparison.OrdinalIgnoreCase));

    public decimal AverageIntegrity
    {
        get
        {
            var integrities = new List<decimal>();
            AddIntegrity(FrameJson, integrities);
            AddIntegrity(ReactorJson, integrities);
            AddIntegrity(EngineJson, integrities);
            return integrities.Count == 0 ? 1m : integrities.Average();
        }
    }

    public decimal AverageCondition
    {
        get
        {
            var conditions = new List<decimal>();
            AddCondition(FrameJson, conditions);
            AddCondition(ReactorJson, conditions);
            AddCondition(EngineJson, conditions);
            return conditions.Count == 0 ? 1m : conditions.Average();
        }
    }

    private static void AddIntegrity(string? json, List<decimal> values)
    {
        if (TryReadComponentMetric(json, "integrity", out var value))
        {
            values.Add(value);
        }
    }

    private static void AddCondition(string? json, List<decimal> values)
    {
        if (TryReadComponentMetric(json, "condition", out var value))
        {
            values.Add(value);
        }
    }

    private static bool TryReadComponentMetric(string? json, string property, out decimal value)
    {
        value = 0m;
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object)
            {
                return false;
            }

            if (!document.RootElement.TryGetProperty(property, out var metricElement))
            {
                return false;
            }

            return metricElement.ValueKind switch
            {
                System.Text.Json.JsonValueKind.Number when metricElement.TryGetDecimal(out var numeric) => SetMetric(numeric, out value),
                System.Text.Json.JsonValueKind.String when decimal.TryParse(metricElement.GetString(), out var parsed) => SetMetric(parsed, out value),
                _ => false,
            };
        }
        catch (System.Text.Json.JsonException)
        {
            return false;
        }
    }

    private static bool SetMetric(decimal parsed, out decimal value)
    {
        value = parsed;
        return true;
    }
}

public sealed record ShipRepairQuoteModel
{
    public required long Cost { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public ShipRepairQuoteModel(long Cost)
    {
        this.Cost = Cost;
    }
}

public sealed record ShipScrapQuoteModel
{
    public required long Value { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public ShipScrapQuoteModel(long Value)
    {
        this.Value = Value;
    }
}

public sealed record ShipRepairActionResult
{
    public required AgentModel Agent { get; init; }

    public required ShipModel Ship { get; init; }

    public required long Cost { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public ShipRepairActionResult(AgentModel Agent, ShipModel Ship, long Cost)
    {
        this.Agent = Agent;
        this.Ship = Ship;
        this.Cost = Cost;
    }
}

public sealed record ShipScrapActionResult
{
    public required AgentModel Agent { get; init; }

    public required long Value { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public ShipScrapActionResult(AgentModel Agent, long Value)
    {
        this.Agent = Agent;
        this.Value = Value;
    }
}

public sealed record ShipOutfitActionResult
{
    public required AgentModel Agent { get; init; }

    public required CargoModel Cargo { get; init; }

    public IReadOnlyList<string>? MountSymbols { get; init; }

    public string? ModulesJson { get; init; }

    public required long Cost { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public ShipOutfitActionResult(AgentModel Agent, CargoModel Cargo, IReadOnlyList<string>? MountSymbols, string? ModulesJson, long Cost)
    {
        this.Agent = Agent;
        this.Cargo = Cargo;
        this.MountSymbols = MountSymbols;
        this.ModulesJson = ModulesJson;
        this.Cost = Cost;
    }
}

public sealed record ContractDeliverableModel
{
    public required string TradeSymbol { get; init; }

    public required string DestinationSymbol { get; init; }

    public required int UnitsRequired { get; init; }

    public required int UnitsFulfilled { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public ContractDeliverableModel(string TradeSymbol, string DestinationSymbol, int UnitsRequired, int UnitsFulfilled)
    {
        this.TradeSymbol = TradeSymbol;
        this.DestinationSymbol = DestinationSymbol;
        this.UnitsRequired = UnitsRequired;
        this.UnitsFulfilled = UnitsFulfilled;
    }
}

public sealed record ContractModel
{
    public required string Id { get; init; }

    public required string FactionSymbol { get; init; }

    public required string Type { get; init; }

    public required bool IsAccepted { get; init; }

    public required bool IsFulfilled { get; init; }

    public DateTimeOffset? Expiration { get; init; }

    public DateTimeOffset? DeadlineToAccept { get; init; }

    public DateTimeOffset? TermsDeadline { get; init; }

    public IReadOnlyList<ContractDeliverableModel> Deliverables { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public ContractModel(
        string Id,
        string FactionSymbol,
        string Type,
        bool IsAccepted,
        bool IsFulfilled,
        DateTimeOffset? Expiration,
        DateTimeOffset? DeadlineToAccept)
        : this(
            Id,
            FactionSymbol,
            Type,
            IsAccepted,
            IsFulfilled,
            Expiration,
            DeadlineToAccept,
            null,
            [])
    {
    }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public ContractModel(
        string Id,
        string FactionSymbol,
        string Type,
        bool IsAccepted,
        bool IsFulfilled,
        DateTimeOffset? Expiration,
        DateTimeOffset? DeadlineToAccept,
        DateTimeOffset? TermsDeadline,
        IReadOnlyList<ContractDeliverableModel>? Deliverables = null)
    {
        this.Id = Id;
        this.FactionSymbol = FactionSymbol;
        this.Type = Type;
        this.IsAccepted = IsAccepted;
        this.IsFulfilled = IsFulfilled;
        this.Expiration = Expiration;
        this.DeadlineToAccept = DeadlineToAccept;
        this.TermsDeadline = TermsDeadline;
        this.Deliverables = Deliverables ?? [];
    }
}

public sealed record MarketDataModel
{
    public required string WaypointSymbol { get; init; }

    public required string SystemSymbol { get; init; }

    public string? TradeGoodsJson { get; init; }

    public string? ImportsJson { get; init; }

    public string? ExportsJson { get; init; }

    public string? ExchangeJson { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public MarketDataModel(
        string WaypointSymbol,
        string SystemSymbol,
        string? TradeGoodsJson,
        string? ImportsJson,
        string? ExportsJson,
        string? ExchangeJson)
    {
        this.WaypointSymbol = WaypointSymbol;
        this.SystemSymbol = SystemSymbol;
        this.TradeGoodsJson = TradeGoodsJson;
        this.ImportsJson = ImportsJson;
        this.ExportsJson = ExportsJson;
        this.ExchangeJson = ExchangeJson;
    }
}

public sealed record ShipyardDataModel
{
    public required string WaypointSymbol { get; init; }

    public required string SystemSymbol { get; init; }

    public string? ShipTypesJson { get; init; }

    public string? ShipsDetailJson { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public ShipyardDataModel(string WaypointSymbol, string SystemSymbol, string? ShipTypesJson, string? ShipsDetailJson = null)
    {
        this.WaypointSymbol = WaypointSymbol;
        this.SystemSymbol = SystemSymbol;
        this.ShipTypesJson = ShipTypesJson;
        this.ShipsDetailJson = ShipsDetailJson;
    }
}

public sealed record PagedResult<T>
{
    public required IReadOnlyList<T> Items { get; init; }

    public required int Total { get; init; }

    public required int Page { get; init; }

    public required int Limit { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public PagedResult(IReadOnlyList<T> Items, int Total, int Page, int Limit)
    {
        this.Items = Items;
        this.Total = Total;
        this.Page = Page;
        this.Limit = Limit;
    }
}

public sealed record RegisterResult
{
    public required string AgentToken { get; init; }

    public required string AgentSymbol { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public RegisterResult(string AgentToken, string AgentSymbol)
    {
        this.AgentToken = AgentToken;
        this.AgentSymbol = AgentSymbol;
    }
}

public sealed record JettisonActionResult
{
    public required CargoModel Cargo { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public JettisonActionResult(CargoModel Cargo)
    {
        this.Cargo = Cargo;
    }
}

public sealed record SurveyDepositModel
{
    public required string Symbol { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public SurveyDepositModel(string Symbol)
    {
        this.Symbol = Symbol;
    }
}

public sealed record SurveyModel
{
    public required string Signature { get; init; }

    public required string WaypointSymbol { get; init; }

    public required IReadOnlyList<SurveyDepositModel> Deposits { get; init; }

    public required DateTimeOffset Expiration { get; init; }

    public required string Size { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public SurveyModel(string Signature, string WaypointSymbol, IReadOnlyList<SurveyDepositModel> Deposits, DateTimeOffset Expiration, string Size)
    {
        this.Signature = Signature;
        this.WaypointSymbol = WaypointSymbol;
        this.Deposits = Deposits;
        this.Expiration = Expiration;
        this.Size = Size;
    }
}

public sealed record SurveyActionResult
{
    public required IReadOnlyList<SurveyModel> Surveys { get; init; }

    public required int CooldownSeconds { get; init; }

    /// <summary>The UTC time at which the survey cooldown expires, as returned by the API.</summary>
    public DateTimeOffset? CooldownExpiresAt { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public SurveyActionResult(IReadOnlyList<SurveyModel> Surveys, int CooldownSeconds, DateTimeOffset? CooldownExpiresAt = null)
    {
        this.Surveys = Surveys;
        this.CooldownSeconds = CooldownSeconds;
        this.CooldownExpiresAt = CooldownExpiresAt;
    }
}

public sealed record SiphonActionResult
{
    public required string YieldSymbol { get; init; }

    public required int YieldUnits { get; init; }

    public required CargoModel Cargo { get; init; }

    public required int CooldownSeconds { get; init; }

    /// <summary>The UTC time at which the siphon cooldown expires, as returned by the API.</summary>
    public DateTimeOffset? CooldownExpiresAt { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public SiphonActionResult(string YieldSymbol, int YieldUnits, CargoModel Cargo, int CooldownSeconds, DateTimeOffset? CooldownExpiresAt = null)
    {
        this.YieldSymbol = YieldSymbol;
        this.YieldUnits = YieldUnits;
        this.Cargo = Cargo;
        this.CooldownSeconds = CooldownSeconds;
        this.CooldownExpiresAt = CooldownExpiresAt;
    }
}

public sealed record WarpActionResult
{
    public required NavModel Nav { get; init; }

    public required FuelModel Fuel { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public WarpActionResult(NavModel Nav, FuelModel Fuel)
    {
        this.Nav = Nav;
        this.Fuel = Fuel;
    }
}

public sealed record JumpActionResult
{
    public required NavModel Nav { get; init; }

    public required int CooldownSeconds { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public JumpActionResult(NavModel Nav, int CooldownSeconds)
    {
        this.Nav = Nav;
        this.CooldownSeconds = CooldownSeconds;
    }
}

public sealed record ChartActionResult
{
    public required string WaypointSymbol { get; init; }

    public required string WaypointType { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public ChartActionResult(string WaypointSymbol, string WaypointType)
    {
        this.WaypointSymbol = WaypointSymbol;
        this.WaypointType = WaypointType;
    }
}

public sealed record JumpGateConnectionModel
{
    public required string WaypointSymbol { get; init; }

    public required IReadOnlyList<string> ConnectedSystems { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public JumpGateConnectionModel(string WaypointSymbol, IReadOnlyList<string> ConnectedSystems)
    {
        this.WaypointSymbol = WaypointSymbol;
        this.ConnectedSystems = ConnectedSystems;
    }
}

public sealed record NegotiateContractActionResult
{
    public required ContractModel Contract { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public NegotiateContractActionResult(ContractModel Contract)
    {
        this.Contract = Contract;
    }
}

// Phase 10: jump-gate construction models
public sealed record ConstructionMaterialModel
{
    public required string TradeSymbol { get; init; }

    public required int Required { get; init; }

    public required int Fulfilled { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public ConstructionMaterialModel(string TradeSymbol, int Required, int Fulfilled)
    {
        this.TradeSymbol = TradeSymbol;
        this.Required = Required;
        this.Fulfilled = Fulfilled;
    }
}

public sealed record ConstructionSiteModel
{
    public required string WaypointSymbol { get; init; }

    public required bool IsComplete { get; init; }

    public required IReadOnlyList<ConstructionMaterialModel> Materials { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public ConstructionSiteModel(
        string WaypointSymbol,
        bool IsComplete,
        IReadOnlyList<ConstructionMaterialModel> Materials)
    {
        this.WaypointSymbol = WaypointSymbol;
        this.IsComplete = IsComplete;
        this.Materials = Materials;
    }
}

public sealed record SupplyConstructionActionResult
{
    public required ConstructionSiteModel Construction { get; init; }

    public required CargoModel Cargo { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public SupplyConstructionActionResult(ConstructionSiteModel Construction, CargoModel Cargo)
    {
        this.Construction = Construction;
        this.Cargo = Cargo;
    }
}
