/**
 * TypeScript types matching the SpaceTraders API DTOs.
 * These mirror the C# record types in SpaceTraders.Application.DTOs and
 * related namespaces so callers get type-safe responses from apiFetch.
 */

export interface AgentDto {
  symbol: string
  credits: number
  startingFaction: string
  shipCount: number
  headquartersSymbol: string | null
}

export interface ShipDto {
  symbol: string
  systemSymbol: string | null
  waypointSymbol: string | null
  status: string | null
  flightMode: string | null
  fuelCurrent: number
  fuelCapacity: number
  cargoCurrent: number
  cargoCapacity: number
  arrivesAt: string | null
  isInTransit: boolean
  lastSyncedAt: string
}

export interface ContractDeliverableDto {
  tradeSymbol: string
  destinationSymbol: string
  unitsRequired: number
  unitsFulfilled: number
}

export interface ContractDto {
  id: string
  factionSymbol: string
  type: string
  isAccepted: boolean
  isFulfilled: boolean
  expiration: string | null
  deadlineToAccept: string | null
  termsDeadline: string | null
  deliverablesJson: string | null
}

export interface TradeOpportunityDto {
  id: number
  tradeSymbol: string
  buyWaypoint: string
  sellWaypoint: string
  buyPrice: number
  sellPrice: number
  profitPerUnit: number
  distanceJumps: number
  profitPerJump: number
  supportsSupplyChain: boolean
  supplyChainDepth: number
  buyType: string
  sellType: string
  effectiveTradeVolume: number
  estimatedFuelCost: number
  estimatedTravelTimeMinutes: number
  opportunityCostPenalty: number
  cooldownPenalty: number
  rateLimitPenalty: number
  routeScore: number
  computedAt: string
}

export interface RateLimitStatusDto {
  remaining: number
  limit: number
  burstRemaining: number
  burstLimit: number
  resetAt: string
  limitType: string | null
  totalRequests: number
  throttledCount: number
}

export interface SystemAlertsDto {
  apiUnavailable: boolean
  tokenResetMismatch: boolean
  cacheDivergence: boolean
  automationDisabled: boolean
  contractDeadlinesApproaching: boolean
  resetUpcoming: boolean
  nextReset: string | null
}

export interface ActivityLogDto {
  id: number
  timestamp: string
  shipSymbol: string
  eventType: string
  message: string
  jsonDetails: string | null
}

export interface SettingDto {
  key: string
  value: string
  type: string
  description: string
}

export interface RunSummaryDto {
  id: string
  name: string
  strategyLabel: string
  startedAt: string
  endedAt: string | null
  startingCredits: number
  endingCredits: number | null
}

export interface ScheduledRunDto {
  id: string
  name: string
  strategyLabel: string
  scheduledSettingsJson: string | null
  activatesAt: string | null
  activatesOnNextRestart: boolean
  createdAt: string
}

export interface ShipTaskRecordDto {
  id: number
  shipSymbol: string
  startedAt: string
  endedAt: string | null
  taskKind: string
  targetWaypoint: string | null
  payloadJson: string | null
}

export interface LedgerSummaryDto {
  category: string
  totalAmount: number
  entryCount: number
}

export interface ShipStatsResponse {
  ship: ShipDto
  ledger: unknown[]
  summary: LedgerSummaryDto[]
}

export interface AutomationHealthDto {
  automationEnabled: boolean
  isLeader: boolean
}

export interface ApiEndpointUsageDto {
  httpMethod: string
  endpoint: string
  calls: number
  lastCalledAt: string
}

export interface CreditSampleDto {
  recordedAt: string
  credits: number
}

export interface LedgerEntryDto {
  id: number
  occurredAt: string
  shipSymbol: string
  runId: string | null
  category: string
  amount: number
  goodSymbol: string | null
  unitPrice: number | null
  units: number | null
  waypointSymbol: string | null
}

export interface RunCreditHighlightDto {
  id: number
  runId: string
  occurredAt: string
  credits: number
  deltaCredits: number
  eventKind: string
  label: string | null
}

export interface MarketPriceSampleDto {
  observedAt: string
  waypointSymbol: string
  goodSymbol: string
  purchasePrice: number
  sellPrice: number
  supply: string | null
  activity: string | null
  tradeVolume: number
}

export interface TradeGoodSnapshotDto {
  symbol: string
  type: string
  purchasePrice: number
  sellPrice: number
  tradeVolume: number
  supply: string
  activity: string
}

export interface MarketWaypointDto {
  waypointSymbol: string
  systemSymbol: string
  imports: string[]
  exports: string[]
  exchange: string[]
  tradeGoods: TradeGoodSnapshotDto[]
}

export interface RunDetailDto {
  run: RunSummaryDto
  creditHighlights: RunCreditHighlightDto[]
  ledgerSummary: LedgerSummaryDto[]
}

export interface RunCompareDto {
  runA: RunDetailDto
  runB: RunDetailDto
}

export interface RunKpisDto {
  creditsPerHour: number | null
  creditsPerShipPerHour: number | null
  creditsPerApiCall: number | null
  idlePercent: number | null
  fuelCostPerCreditEarned: number | null
}

export interface SystemDto {
  symbol: string
  sectorSymbol: string
  type: string
  x: number
  y: number
  lastObservedAt: string
  isVisited: boolean
}

export interface JumpConnectionDto {
  fromSystem: string
  toSystem: string
}

export interface WaypointTraitDto {
  symbol: string
  name: string
  description: string
}

export interface StartupSnapshotListItemDto {
  id: number
  capturedAt: string
  isInitialSnapshot: boolean
}

export interface ContractRoiDto {
  contractId: string
  totalPayout: number
  estimatedFuelCost: number
  netRoi: number
  isProfitable: boolean
}

export interface TradeRouteDto {
  good: string
  buyWaypoint: string
  sellWaypoint: string
  totalUnits: number
  totalProfit: number
  runDurationHours: number
  profitPerHour: number
}

export interface MarketFreshnessDto {
  waypointSymbol: string
  systemSymbol: string
  lastObservedAt: string
  ageMinutes: number
}

export interface AnomalyDto {
  creditGrowthAnomaly: boolean
  recentCreditRatePerHour: number
  avgCreditRatePerHour: number
  priceAnomalies: unknown[]
}

export interface WaypointDto {
  symbol: string
  systemSymbol: string
  type: string
  x: number
  y: number
  traitsJson: string | null
  orbitalsJson: string | null
  parentSymbol: string | null
  hasMarket: boolean
  hasShipyard: boolean
  isUnderConstruction: boolean
  lastObservedAt: string
}

export interface SystemMapResponseDto {
  system: SystemDto
  waypoints: WaypointDto[]
}
