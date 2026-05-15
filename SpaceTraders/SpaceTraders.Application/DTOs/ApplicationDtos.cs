namespace SpaceTraders.Application.DTOs;

public sealed record AgentDto
{
    public required string Symbol { get; init; }

    public required long Credits { get; init; }

    public required string StartingFaction { get; init; }

    public required int ShipCount { get; init; }

    public string? HeadquartersSymbol { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public AgentDto(string Symbol, long Credits, string StartingFaction, int ShipCount, string? HeadquartersSymbol)
    {
        this.Symbol = Symbol;
        this.Credits = Credits;
        this.StartingFaction = StartingFaction;
        this.ShipCount = ShipCount;
        this.HeadquartersSymbol = HeadquartersSymbol;
    }
}

public sealed record ShipDto
{
    public required string Symbol { get; init; }

    public string? SystemSymbol { get; init; }

    public string? WaypointSymbol { get; init; }

    public string? Status { get; init; }

    public string? FlightMode { get; init; }

    public required int FuelCurrent { get; init; }

    public required int FuelCapacity { get; init; }

    public required int CargoCurrent { get; init; }

    public required int CargoCapacity { get; init; }

    public DateTimeOffset? ArrivesAt { get; init; }

    public required bool IsInTransit { get; init; }

    public required DateTimeOffset LastSyncedAt { get; init; }

    public required string ShipType { get; init; }

    public required IReadOnlyList<string> MountSymbols { get; init; }

    public required bool HasMiningEquipment { get; init; }

    public required bool HasSurveyEquipment { get; init; }

    public required bool HasGasSiphonEquipment { get; init; }

    public required bool HasGasProcessor { get; init; }

    public required bool HasMineralProcessor { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public ShipDto(
        string Symbol,
        string? SystemSymbol,
        string? WaypointSymbol,
        string? Status,
        string? FlightMode,
        int FuelCurrent,
        int FuelCapacity,
        int CargoCurrent,
        int CargoCapacity,
        DateTimeOffset? ArrivesAt,
        bool IsInTransit,
        DateTimeOffset LastSyncedAt,
        string ShipType,
        IReadOnlyList<string> MountSymbols,
        bool HasMiningEquipment,
        bool HasSurveyEquipment,
        bool HasGasSiphonEquipment,
        bool HasGasProcessor,
        bool HasMineralProcessor)
    {
        this.Symbol = Symbol;
        this.SystemSymbol = SystemSymbol;
        this.WaypointSymbol = WaypointSymbol;
        this.Status = Status;
        this.FlightMode = FlightMode;
        this.FuelCurrent = FuelCurrent;
        this.FuelCapacity = FuelCapacity;
        this.CargoCurrent = CargoCurrent;
        this.CargoCapacity = CargoCapacity;
        this.ArrivesAt = ArrivesAt;
        this.IsInTransit = IsInTransit;
        this.LastSyncedAt = LastSyncedAt;
        this.ShipType = ShipType;
        this.MountSymbols = MountSymbols;
        this.HasMiningEquipment = HasMiningEquipment;
        this.HasSurveyEquipment = HasSurveyEquipment;
        this.HasGasSiphonEquipment = HasGasSiphonEquipment;
        this.HasGasProcessor = HasGasProcessor;
        this.HasMineralProcessor = HasMineralProcessor;
    }
}

public sealed record ContractDeliverableDto
{
    public required string TradeSymbol { get; init; }

    public required string DestinationSymbol { get; init; }

    public required int UnitsRequired { get; init; }

    public required int UnitsFulfilled { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public ContractDeliverableDto(string TradeSymbol, string DestinationSymbol, int UnitsRequired, int UnitsFulfilled)
    {
        this.TradeSymbol = TradeSymbol;
        this.DestinationSymbol = DestinationSymbol;
        this.UnitsRequired = UnitsRequired;
        this.UnitsFulfilled = UnitsFulfilled;
    }
}

public sealed record ContractDto
{
    public required string Id { get; init; }

    public required string FactionSymbol { get; init; }

    public required string Type { get; init; }

    public required bool IsAccepted { get; init; }

    public required bool IsFulfilled { get; init; }

    public DateTimeOffset? Expiration { get; init; }

    public DateTimeOffset? DeadlineToAccept { get; init; }

    public DateTimeOffset? TermsDeadline { get; init; }

    public string? DeliverablesJson { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public ContractDto(
        string Id,
        string FactionSymbol,
        string Type,
        bool IsAccepted,
        bool IsFulfilled,
        DateTimeOffset? Expiration,
        DateTimeOffset? DeadlineToAccept,
        DateTimeOffset? TermsDeadline = null,
        string? DeliverablesJson = null)
    {
        this.Id = Id;
        this.FactionSymbol = FactionSymbol;
        this.Type = Type;
        this.IsAccepted = IsAccepted;
        this.IsFulfilled = IsFulfilled;
        this.Expiration = Expiration;
        this.DeadlineToAccept = DeadlineToAccept;
        this.TermsDeadline = TermsDeadline;
        this.DeliverablesJson = DeliverablesJson;
    }
}

public sealed record TradeOpportunityDto
{
    public required int Id { get; init; }

    public required string TradeSymbol { get; init; }

    public required string BuyWaypoint { get; init; }

    public required string SellWaypoint { get; init; }

    public required int BuyPrice { get; init; }

    public required int SellPrice { get; init; }

    public required int ProfitPerUnit { get; init; }

    public required int DistanceJumps { get; init; }

    public required decimal ProfitPerJump { get; init; }

    public required bool SupportsSupplyChain { get; init; }

    public required int SupplyChainDepth { get; init; }

    public required string BuyType { get; init; }

    public required string SellType { get; init; }

    public required int EffectiveTradeVolume { get; init; }

    public required decimal EstimatedFuelCost { get; init; }

    public required decimal EstimatedTravelTimeMinutes { get; init; }

    public required decimal OpportunityCostPenalty { get; init; }

    public required decimal CooldownPenalty { get; init; }

    public required decimal RateLimitPenalty { get; init; }

    public required decimal RouteScore { get; init; }

    public required DateTimeOffset ComputedAt { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public TradeOpportunityDto(
        int Id,
        string TradeSymbol,
        string BuyWaypoint,
        string SellWaypoint,
        int BuyPrice,
        int SellPrice,
        int ProfitPerUnit,
        int DistanceJumps,
        decimal ProfitPerJump,
        bool SupportsSupplyChain,
        int SupplyChainDepth,
        DateTimeOffset ComputedAt,
        string BuyType = "UNKNOWN",
        string SellType = "UNKNOWN",
        int EffectiveTradeVolume = 0,
        decimal EstimatedFuelCost = 0m,
        decimal EstimatedTravelTimeMinutes = 0m,
        decimal OpportunityCostPenalty = 0m,
        decimal CooldownPenalty = 0m,
        decimal RateLimitPenalty = 0m,
        decimal RouteScore = 0m)
    {
        this.Id = Id;
        this.TradeSymbol = TradeSymbol;
        this.BuyWaypoint = BuyWaypoint;
        this.SellWaypoint = SellWaypoint;
        this.BuyPrice = BuyPrice;
        this.SellPrice = SellPrice;
        this.ProfitPerUnit = ProfitPerUnit;
        this.DistanceJumps = DistanceJumps;
        this.ProfitPerJump = ProfitPerJump;
        this.SupportsSupplyChain = SupportsSupplyChain;
        this.SupplyChainDepth = SupplyChainDepth;
        this.BuyType = BuyType;
        this.SellType = SellType;
        this.EffectiveTradeVolume = EffectiveTradeVolume;
        this.EstimatedFuelCost = EstimatedFuelCost;
        this.EstimatedTravelTimeMinutes = EstimatedTravelTimeMinutes;
        this.OpportunityCostPenalty = OpportunityCostPenalty;
        this.CooldownPenalty = CooldownPenalty;
        this.RateLimitPenalty = RateLimitPenalty;
        this.RouteScore = RouteScore;
        this.ComputedAt = ComputedAt;
    }
}

public sealed record SettingDto
{
    public required string Key { get; init; }

    public required string Value { get; init; }

    public required string Type { get; init; }

    public required string Description { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public SettingDto(string Key, string Value, string Type, string Description)
    {
        this.Key = Key;
        this.Value = Value;
        this.Type = Type;
        this.Description = Description;
    }
}

public sealed record ActivityLogDto
{
    public required long Id { get; init; }

    public required DateTimeOffset Timestamp { get; init; }

    public required string ShipSymbol { get; init; }

    public required string EventType { get; init; }

    public required string Message { get; init; }

    public string? JsonDetails { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public ActivityLogDto(long Id, DateTimeOffset Timestamp, string ShipSymbol, string EventType, string Message, string? JsonDetails)
    {
        this.Id = Id;
        this.Timestamp = Timestamp;
        this.ShipSymbol = ShipSymbol;
        this.EventType = EventType;
        this.Message = Message;
        this.JsonDetails = JsonDetails;
    }
}

public sealed record RateLimitStatusDto
{
    public required int Remaining { get; init; }

    public required int Limit { get; init; }

    public required int BurstRemaining { get; init; }

    public required int BurstLimit { get; init; }

    public required DateTimeOffset ResetAt { get; init; }

    public string? LimitType { get; init; }

    public required int TotalRequests { get; init; }

    public required int ThrottledCount { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public RateLimitStatusDto(
        int Remaining,
        int Limit,
        int BurstRemaining,
        int BurstLimit,
        DateTimeOffset ResetAt,
        string? LimitType,
        int TotalRequests,
        int ThrottledCount)
    {
        this.Remaining = Remaining;
        this.Limit = Limit;
        this.BurstRemaining = BurstRemaining;
        this.BurstLimit = BurstLimit;
        this.ResetAt = ResetAt;
        this.LimitType = LimitType;
        this.TotalRequests = TotalRequests;
        this.ThrottledCount = ThrottledCount;
    }
}

/// <summary>
/// Represents the active assignment for a ship.
/// </summary>
/// <remarks>
/// Baseline fields (required by the scout navigation flow):
/// <list type="bullet">
///   <item><see cref="ShipSymbol"/> – primary key for the assignment.</item>
///   <item><see cref="AssignmentType"/> – e.g. <c>"Scout"</c> in the baseline flow.</item>
///   <item><see cref="DestWaypoint"/> – single destination the ship must reach.</item>
///   <item><see cref="StepIndex"/> – sequence number within the route.</item>
///   <item><see cref="AssignedAt"/> – timestamp set on creation.</item>
///   <item><see cref="CompletedAt"/> – null = active; set = complete.</item>
/// </list>
/// Legacy fields (persisted for compatibility but not used by the baseline scout flow):
/// <see cref="OriginWaypoint"/>, <see cref="CargoSymbol"/>, <see cref="ContractId"/>,
/// <see cref="PurchaseUnitPrice"/>, <see cref="RequiredUnits"/>, <see cref="SupplyCompleted"/>.
/// </remarks>
public sealed record ShipAssignmentDto
{
    public required string ShipSymbol { get; init; }

    public required string AssignmentType { get; init; }

    public string? OriginWaypoint { get; init; }

    public string? DestWaypoint { get; init; }

    public string? CargoSymbol { get; init; }

    public string? ContractId { get; init; }

    public required int StepIndex { get; init; }

    public required DateTimeOffset AssignedAt { get; init; }

    public DateTimeOffset? CompletedAt { get; init; }

    public int PurchaseUnitPrice { get; init; }

    public int RequiredUnits { get; init; }

    public bool SupplyCompleted { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public ShipAssignmentDto(
        string ShipSymbol,
        string AssignmentType,
        string? OriginWaypoint,
        string? DestWaypoint,
        string? CargoSymbol,
        string? ContractId,
        int StepIndex,
        DateTimeOffset AssignedAt,
        DateTimeOffset? CompletedAt,
        int PurchaseUnitPrice = 0,
        int RequiredUnits = 0,
        bool SupplyCompleted = false)
    {
        this.ShipSymbol = ShipSymbol;
        this.AssignmentType = AssignmentType;
        this.OriginWaypoint = OriginWaypoint;
        this.DestWaypoint = DestWaypoint;
        this.CargoSymbol = CargoSymbol;
        this.ContractId = ContractId;
        this.StepIndex = StepIndex;
        this.AssignedAt = AssignedAt;
        this.CompletedAt = CompletedAt;
        this.PurchaseUnitPrice = PurchaseUnitPrice;
        this.RequiredUnits = RequiredUnits;
        this.SupplyCompleted = SupplyCompleted;
    }

    /// <summary>
    /// Creates a baseline scout assignment with only the required fields populated.
    /// Legacy fields (<see cref="OriginWaypoint"/>, <see cref="CargoSymbol"/>, <see cref="ContractId"/>,
    /// <see cref="PurchaseUnitPrice"/>, <see cref="RequiredUnits"/>, <see cref="SupplyCompleted"/>) are left at defaults.
    /// </summary>
    public static ShipAssignmentDto CreateScout(
        string shipSymbol,
        string destinationWaypoint,
        int stepIndex,
        DateTimeOffset assignedAt) =>
        new(
            ShipSymbol: shipSymbol,
            AssignmentType: "Scout",
            OriginWaypoint: null,
            DestWaypoint: destinationWaypoint,
            CargoSymbol: null,
            ContractId: null,
            StepIndex: stepIndex,
            AssignedAt: assignedAt,
            CompletedAt: null);
}

/// <summary>Efficiency KPIs computed for a single run.</summary>
public sealed record RunKpisDto
{
    /// <summary>Credits earned per wall-clock hour.</summary>
    public double? CreditsPerHour { get; init; }

    /// <summary>Credits earned per active ship per hour.</summary>
    public double? CreditsPerShipPerHour { get; init; }

    /// <summary>
    /// Credits earned divided by total cumulative API calls recorded for this agent.
    /// This is a lifetime-rate approximation because per-run API call counts are not stored.
    /// </summary>
    public double? CreditsPerApiCall { get; init; }

    /// <summary>Percentage of ship-time spent in the Idle task (0–100).</summary>
    public double? IdlePercent { get; init; }

    /// <summary>Absolute fuel spend divided by total income (0–1 ratio).</summary>
    public double? FuelCostPerCreditEarned { get; init; }
}


/// <summary>
/// A single completed or blocked goal entry in the per-ship goal history log.
/// </summary>
/// <remarks>Phase 17e: introduced as part of the fleet dashboard implementation.</remarks>
public sealed record ShipGoalHistoryEntry
{
    public required Guid Id { get; init; }

    public required string ShipSymbol { get; init; }

    public required string GoalKind { get; init; }

    public required Guid GoalId { get; init; }

    /// <summary>'Completed' or 'Blocked'.</summary>
    public required string Outcome { get; init; }

    /// <summary>Blocking reason when <see cref="Outcome"/> is 'Blocked'; <c>null</c> otherwise.</summary>
    public string? Reason { get; init; }

    public required DateTimeOffset StartedAt { get; init; }

    public required DateTimeOffset EndedAt { get; init; }
}

public sealed record ShipyardShipDto
{
    public required string Type { get; init; }

    public string? Name { get; init; }

    public string? Description { get; init; }

    public string? Supply { get; init; }

    public string? Activity { get; init; }

    public required long PurchasePrice { get; init; }
}

public sealed record ShipyardWaypointDto
{
    public required string WaypointSymbol { get; init; }

    public required string SystemSymbol { get; init; }

    public required string[] ShipTypes { get; init; }

    public required ShipyardShipDto[] Ships { get; init; }
}

public sealed record ShipyardFreshnessDto
{
    public required string WaypointSymbol { get; init; }

    public required string SystemSymbol { get; init; }

    public required DateTimeOffset LastObservedAt { get; init; }

    public required int AgeMinutes { get; init; }
}

public sealed record ShipDiagnosticsDto
{
    public required string Symbol { get; init; }

    public required string ShipType { get; init; }

    public string? SystemSymbol { get; init; }

    public string? WaypointSymbol { get; init; }

    public string? Status { get; init; }

    public string? FlightMode { get; init; }

    public required int FuelCurrent { get; init; }

    public required int FuelCapacity { get; init; }

    public required int CargoCurrent { get; init; }

    public required int CargoCapacity { get; init; }

    public required bool IsInTransit { get; init; }

    public DateTimeOffset? ArrivesAt { get; init; }

    public required DateTimeOffset LastSyncedAt { get; init; }

    public required IReadOnlyList<string> MountSymbols { get; init; }

    public required bool HasMiningEquipment { get; init; }

    public required bool HasSurveyEquipment { get; init; }

    public required bool HasGasSiphonEquipment { get; init; }

    public required bool HasGasProcessor { get; init; }

    public required bool HasMineralProcessor { get; init; }

    public required bool HasCargoSpace { get; init; }

    public required bool HasFuelCapacity { get; init; }

    public required bool IsOccupied { get; init; }

    public string? ActiveAssignmentType { get; init; }

    public string? ActiveAssignmentContractId { get; init; }

    public string? ActiveAssignmentSourceWaypoint { get; init; }

    public string? ActiveAssignmentDestinationWaypoint { get; init; }

    public DateTimeOffset? ActiveAssignmentAssignedAt { get; init; }

    public int? ActiveAssignmentStepIndex { get; init; }

    public required bool IsEligibleForContractMinerSelection { get; init; }

    public required string ContractMinerEligibilityReason { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public ShipDiagnosticsDto(
        string Symbol,
        string ShipType,
        string? SystemSymbol,
        string? WaypointSymbol,
        string? Status,
        string? FlightMode,
        int FuelCurrent,
        int FuelCapacity,
        int CargoCurrent,
        int CargoCapacity,
        bool IsInTransit,
        DateTimeOffset? ArrivesAt,
        DateTimeOffset LastSyncedAt,
        IReadOnlyList<string> MountSymbols,
        bool HasMiningEquipment,
        bool HasSurveyEquipment,
        bool HasGasSiphonEquipment,
        bool HasGasProcessor,
        bool HasMineralProcessor,
        bool HasCargoSpace,
        bool HasFuelCapacity,
        bool IsOccupied,
        string? ActiveAssignmentType,
        string? ActiveAssignmentContractId,
        string? ActiveAssignmentSourceWaypoint,
        string? ActiveAssignmentDestinationWaypoint,
        DateTimeOffset? ActiveAssignmentAssignedAt,
        int? ActiveAssignmentStepIndex,
        bool IsEligibleForContractMinerSelection,
        string ContractMinerEligibilityReason)
    {
        this.Symbol = Symbol;
        this.ShipType = ShipType;
        this.SystemSymbol = SystemSymbol;
        this.WaypointSymbol = WaypointSymbol;
        this.Status = Status;
        this.FlightMode = FlightMode;
        this.FuelCurrent = FuelCurrent;
        this.FuelCapacity = FuelCapacity;
        this.CargoCurrent = CargoCurrent;
        this.CargoCapacity = CargoCapacity;
        this.IsInTransit = IsInTransit;
        this.ArrivesAt = ArrivesAt;
        this.LastSyncedAt = LastSyncedAt;
        this.MountSymbols = MountSymbols;
        this.HasMiningEquipment = HasMiningEquipment;
        this.HasSurveyEquipment = HasSurveyEquipment;
        this.HasGasSiphonEquipment = HasGasSiphonEquipment;
        this.HasGasProcessor = HasGasProcessor;
        this.HasMineralProcessor = HasMineralProcessor;
        this.HasCargoSpace = HasCargoSpace;
        this.HasFuelCapacity = HasFuelCapacity;
        this.IsOccupied = IsOccupied;
        this.ActiveAssignmentType = ActiveAssignmentType;
        this.ActiveAssignmentContractId = ActiveAssignmentContractId;
        this.ActiveAssignmentSourceWaypoint = ActiveAssignmentSourceWaypoint;
        this.ActiveAssignmentDestinationWaypoint = ActiveAssignmentDestinationWaypoint;
        this.ActiveAssignmentAssignedAt = ActiveAssignmentAssignedAt;
        this.ActiveAssignmentStepIndex = ActiveAssignmentStepIndex;
        this.IsEligibleForContractMinerSelection = IsEligibleForContractMinerSelection;
        this.ContractMinerEligibilityReason = ContractMinerEligibilityReason;
    }
}
