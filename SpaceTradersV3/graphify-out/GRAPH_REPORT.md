# Graph Report - .  (2026-07-20)

## Corpus Check
- cluster-only mode — file stats not available

## Summary
- 972 nodes · 2145 edges · 67 communities (62 shown, 5 thin omitted)
- Extraction: 98% EXTRACTED · 2% INFERRED · 0% AMBIGUOUS · INFERRED: 44 edges (avg confidence: 0.8)
- Token cost: 0 input · 0 output

## Graph Freshness
- Built from commit: `8005b6a9`
- Run `git rev-parse HEAD` and compare to check if the graph is stale.
- Run `graphify update .` after code changes (no API cost).

## Community Hubs (Navigation)
- Community 0
- Community 1
- Community 2
- Community 3
- Community 4
- Community 5
- Community 6
- Community 7
- Community 8
- Community 9
- Community 10
- Community 11
- Community 12
- Community 13
- Community 14
- Community 15
- Community 16
- Community 17
- Community 18
- Community 19
- Community 20
- Community 21
- Community 22
- Community 23
- Community 24
- Community 25
- Community 26
- Community 27
- Community 28
- Community 29
- Community 30
- Community 31
- Community 32
- Community 33
- Community 34
- Community 35
- Community 36
- Community 37
- Community 38
- Community 39
- Community 40
- Community 41
- Community 42
- Community 43
- Community 44
- Community 45
- Community 46
- Community 47
- Community 48
- Community 49
- Community 50
- Community 51
- Community 52
- Community 53
- Community 54
- Community 55
- Community 56
- Community 57
- Community 58
- Community 59
- Community 60
- Community 61
- Community 62
- Community 63
- Community 64
- Community 65

## God Nodes (most connected - your core abstractions)
1. `SpaceTradersApiClient` - 62 edges
2. `SpaceTradersPortAdapter` - 55 edges
3. `ISpaceTradersApiClient` - 50 edges
4. `ISpaceTradersPort` - 43 edges
5. `SpaceTraders.Application.Interfaces.Repositories` - 23 edges
6. `SpaceTraders.Application.Interfaces` - 21 edges
7. `ShipModel` - 21 edges
8. `Ship` - 18 edges
9. `PagedApiResponse` - 15 edges
10. `ShipCargo` - 15 edges

## Surprising Connections (you probably didn't know these)
- `SpaceTradersPortAdapter` --implements--> `ISpaceTradersPort`  [EXTRACTED]
  SpaceTraders.Infrastructure.SpaceTradersAPI/Adapters/SpaceTradersPortAdapter.cs → SpaceTradersV3.Application/Ports/ISpaceTradersPort.cs
- `ApiAvailabilityState` --implements--> `IApiAvailabilityState`  [EXTRACTED]
  SpaceTraders.Infrastructure.SpaceTradersAPI/Availability/ApiAvailabilityState.cs → SpaceTradersV3.Application/Interfaces/IApiAvailabilityState.cs
- `RateLimitStatus` --implements--> `IRateLimitStatus`  [EXTRACTED]
  SpaceTraders.Infrastructure.SpaceTradersAPI/RateLimiting/RateLimitStatus.cs → SpaceTradersV3.Application/Interfaces/IRateLimitStatus.cs
- `SpaceTradersApiClient` --references--> `IApiEndpointUsageRecorder`  [EXTRACTED]
  SpaceTraders.Infrastructure.SpaceTradersAPI/Clients/SpaceTradersApiClient.cs → SpaceTradersV3.Application/Interfaces/IApiEndpointUsageRecorder.cs
- `PagedApiResponse` --references--> `Meta`  [EXTRACTED]
  SpaceTraders.Infrastructure.SpaceTradersAPI/Models/Common/PagedApiResponse.cs → SpaceTraders.Infrastructure.SpaceTradersAPI/Models/Common/Meta.cs

## Import Cycles
- None detected.

## Communities (67 total, 5 thin omitted)

### Community 0 - "Community 0"
Cohesion: 0.07
Nodes (43): DateTimeOffset, IReadOnlyList, AgentModel, CargoItemModel, CargoModel, ChartActionResult, ConstructionMaterialModel, ConstructionSiteModel (+35 more)

### Community 1 - "Community 1"
Cohesion: 0.07
Nodes (18): AuthMode, HttpClient, HttpMethod, JsonSerializerOptions, CancellationToken, Task, SpaceTradersApiClient, IReadOnlyList (+10 more)

### Community 2 - "Community 2"
Cohesion: 0.06
Nodes (65): Agent, DateTimeOffset, IReadOnlyList, BuyCargoResult, CargoItem, Cooldown, Extraction, ExtractionYield (+57 more)

### Community 3 - "Community 3"
Cohesion: 0.07
Nodes (21): CancellationToken, Task, ISpaceTradersApiClient, IReadOnlyList, RegisterResponseData, Agent, PublicAgent, DateTimeOffset (+13 more)

### Community 4 - "Community 4"
Cohesion: 0.10
Nodes (10): Agent, CancellationToken, ConstructionSite, Contract, DateTimeOffset, IReadOnlyList, Ship, Task (+2 more)

### Community 5 - "Community 5"
Cohesion: 0.06
Nodes (31): SpaceTraders.Application.Interfaces.Repositories, CancellationToken, DateTimeOffset, IReadOnlyList, Task, CreditsSampleDto, IAgentCreditsSampleRepository, CancellationToken (+23 more)

### Community 6 - "Community 6"
Cohesion: 0.09
Nodes (22): SpaceTraders.Application.Ports, CancellationToken, Task, IAgentRepository, CancellationToken, DateTimeOffset, Guid, IReadOnlyList (+14 more)

### Community 7 - "Community 7"
Cohesion: 0.11
Nodes (15): List, ResourceProductionGoal, ShipLocalStatus, CancellationToken, ShipGoal, Task, IAssignmentResolver, IShipCapabilityRegistry (+7 more)

### Community 8 - "Community 8"
Cohesion: 0.13
Nodes (15): IReadOnlyCollection, IReadOnlyDictionary, IReadOnlyList, TradeOpportunityDto, ITradeAnalyser, ITradeSymbolNormalizer, MarketSnapshot, TradeGoodSnapshot (+7 more)

### Community 9 - "Community 9"
Cohesion: 0.20
Nodes (11): SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Fleet, SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Common, SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Agents, SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Status, SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Accounts, SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Contracts, SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Shipyards, SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Markets (+3 more)

### Community 10 - "Community 10"
Cohesion: 0.29
Nodes (6): CancellationToken, DateTimeOffset, HttpRequestMessage, HttpResponseMessage, Task, RateLimitResponseHandler

### Community 11 - "Community 11"
Cohesion: 0.20
Nodes (10): ApiMessage, SpaceTraders.Infrastructure.SpaceTradersAPI.Exceptions, ErrorCode, Exception, HttpStatusCode, JsonElement, CancellationToken, HttpResponseMessage (+2 more)

### Community 12 - "Community 12"
Cohesion: 0.29
Nodes (9): LedgerCategory, CancellationToken, DateTimeOffset, Guid, IReadOnlyList, Task, ILedgerRepository, LedgerEntryDto (+1 more)

### Community 13 - "Community 13"
Cohesion: 0.18
Nodes (9): Action, SpaceTraders.Infrastructure.SpaceTradersAPI.Adapters, SpaceTraders.Infrastructure.SpaceTradersAPI.Configuration, SpaceTraders.Infrastructure.SpaceTradersAPI, IConfiguration, IServiceCollection, string, SpaceTradersApiOptions (+1 more)

### Community 14 - "Community 14"
Cohesion: 0.29
Nodes (8): Description, Key, CancellationToken, IReadOnlyList, Task, ISettingsRepository, Type, Value

### Community 15 - "Community 15"
Cohesion: 0.27
Nodes (8): OrchestratorGoalChain, ShipActivitySnapshot, ShipAssignmentSnapshot, CancellationToken, IReadOnlyList, ShipGoalHistoryEntry, Task, IFleetStatusQueryService

### Community 16 - "Community 16"
Cohesion: 0.13
Nodes (12): SpaceTraders.Infrastructure.SpaceTradersAPI.RateLimiting, DelegatingHandler, RateLimiter, CancellationToken, HttpRequestMessage, HttpResponseMessage, Task, RateLimitingHandler (+4 more)

### Community 17 - "Community 17"
Cohesion: 0.33
Nodes (7): CancellationToken, DateTimeOffset, IReadOnlyList, Task, TimeSpan, IWaypointRepository, WaypointCacheModel

### Community 18 - "Community 18"
Cohesion: 0.27
Nodes (7): GoalStatus, IReadOnlySet, CancellationToken, Guid, ShipGoal, Task, IShipGoalRepository

### Community 19 - "Community 19"
Cohesion: 0.17
Nodes (8): SpaceTraders.Infrastructure.SpaceTradersAPI.Notifications, CancellationToken, string, Task, WebhookAlertNotifier, CancellationToken, Task, IAlertNotifier

### Community 20 - "Community 20"
Cohesion: 0.36
Nodes (6): CancellationToken, DateTimeOffset, IReadOnlyList, Task, IShipTaskRecordRepository, ShipTaskRecordDto

### Community 21 - "Community 21"
Cohesion: 0.15
Nodes (7): SpaceTraders.Application.Interfaces, Guid, IActiveRunIdProvider, IDashboardNotifier, ILeaderElection, DateTimeOffset, IRateLimitStatus

### Community 22 - "Community 22"
Cohesion: 0.20
Nodes (5): CancellationToken, HttpRequestMessage, HttpResponseMessage, Task, IApiAvailabilityState

### Community 23 - "Community 23"
Cohesion: 0.29
Nodes (6): ActivityLogDto, CancellationToken, DateTimeOffset, IReadOnlyList, Task, IActivityLogRepository

### Community 24 - "Community 24"
Cohesion: 0.38
Nodes (5): ContractDto, CancellationToken, IReadOnlyList, Task, IContractRepository

### Community 25 - "Community 25"
Cohesion: 0.31
Nodes (6): FleetGoal, CancellationToken, Guid, IReadOnlyList, Task, IFleetGoalRepository

### Community 26 - "Community 26"
Cohesion: 0.36
Nodes (6): CancellationToken, DateTimeOffset, IReadOnlyList, Task, ISystemRepository, SystemCacheModel

### Community 27 - "Community 27"
Cohesion: 0.39
Nodes (5): ShipAssignmentDto, CancellationToken, IReadOnlyList, Task, IShipAssignmentRepository

### Community 28 - "Community 28"
Cohesion: 0.25
Nodes (5): Credits, DateTimeOffset, IReadOnlyList, ICreditHistoryService, Timestamp

### Community 29 - "Community 29"
Cohesion: 0.25
Nodes (3): SpaceTraders.Infrastructure.SpaceTradersAPI.Availability, int, ApiAvailabilityState

### Community 30 - "Community 30"
Cohesion: 0.43
Nodes (7): IReadOnlyList, Announcement, ServerResetInfo, ServerStatus, StatusHealth, StatusLink, StatusStats

### Community 31 - "Community 31"
Cohesion: 0.39
Nodes (7): DateTimeOffset, IReadOnlyList, Waypoint, WaypointChart, WaypointModifier, WaypointOrbital, WaypointTrait

### Community 32 - "Community 32"
Cohesion: 0.50
Nodes (5): CancellationToken, DateTimeOffset, Guid, Task, IShipEventScheduler

### Community 33 - "Community 33"
Cohesion: 0.43
Nodes (4): CancellationToken, IReadOnlyList, Task, ISurveyRepository

### Community 34 - "Community 34"
Cohesion: 0.33
Nodes (6): SpaceTradersV3.Infrastructure.SpaceTradersAPI, net10.0, Microsoft.NET.Sdk, SpaceTradersV3.Application, net10.0, Microsoft.NET.Sdk

### Community 36 - "Community 36"
Cohesion: 0.33
Nodes (5): dependencies, net10.0, net10.0/linux-x64, net10.0/win-x64, version

### Community 37 - "Community 37"
Cohesion: 0.40
Nodes (3): SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Systems, IReadOnlyList, JumpGate

### Community 38 - "Community 38"
Cohesion: 0.60
Nodes (4): IReadOnlyList, Market, TradeGood, TradeGoodSymbol

### Community 39 - "Community 39"
Cohesion: 0.40
Nodes (4): dependencies, net10.0/linux-x64, net10.0/win-x64, version

### Community 40 - "Community 40"
Cohesion: 0.40
Nodes (5): contentHash, requested, resolved, type, AsyncFixer

### Community 41 - "Community 41"
Cohesion: 0.40
Nodes (5): contentHash, requested, resolved, type, DotNetProjectFile.Analyzers

### Community 42 - "Community 42"
Cohesion: 0.40
Nodes (5): contentHash, requested, resolved, type, IDisposableAnalyzers

### Community 43 - "Community 43"
Cohesion: 0.40
Nodes (5): contentHash, requested, resolved, type, Microsoft.AspNetCore.Components.Analyzers

### Community 44 - "Community 44"
Cohesion: 0.40
Nodes (5): contentHash, requested, resolved, type, Microsoft.CodeAnalysis.Analyzers

### Community 45 - "Community 45"
Cohesion: 0.40
Nodes (5): contentHash, requested, resolved, type, Microsoft.CodeAnalysis.NetAnalyzers

### Community 46 - "Community 46"
Cohesion: 0.40
Nodes (5): Qowaiv.Analyzers.CSharp, contentHash, requested, resolved, type

### Community 47 - "Community 47"
Cohesion: 0.40
Nodes (5): SerilogAnalyzer, contentHash, requested, resolved, type

### Community 48 - "Community 48"
Cohesion: 0.40
Nodes (5): SonarAnalyzer.CSharp, contentHash, requested, resolved, type

### Community 49 - "Community 49"
Cohesion: 0.40
Nodes (5): StyleCop.Analyzers, contentHash, requested, resolved, type

### Community 50 - "Community 50"
Cohesion: 0.40
Nodes (3): CancellationToken, Task, IRunLifecycleManager

### Community 51 - "Community 51"
Cohesion: 0.40
Nodes (5): contentHash, requested, resolved, type, AsyncFixer

### Community 52 - "Community 52"
Cohesion: 0.40
Nodes (5): contentHash, requested, resolved, type, DotNetProjectFile.Analyzers

### Community 53 - "Community 53"
Cohesion: 0.40
Nodes (5): contentHash, requested, resolved, type, IDisposableAnalyzers

### Community 54 - "Community 54"
Cohesion: 0.40
Nodes (5): contentHash, requested, resolved, type, Microsoft.AspNetCore.Components.Analyzers

### Community 55 - "Community 55"
Cohesion: 0.40
Nodes (5): contentHash, requested, resolved, type, Microsoft.CodeAnalysis.Analyzers

### Community 56 - "Community 56"
Cohesion: 0.40
Nodes (5): contentHash, requested, resolved, type, Microsoft.CodeAnalysis.NetAnalyzers

### Community 57 - "Community 57"
Cohesion: 0.40
Nodes (5): Qowaiv.Analyzers.CSharp, contentHash, requested, resolved, type

### Community 58 - "Community 58"
Cohesion: 0.40
Nodes (5): SerilogAnalyzer, contentHash, requested, resolved, type

### Community 59 - "Community 59"
Cohesion: 0.40
Nodes (5): SonarAnalyzer.CSharp, contentHash, requested, resolved, type

### Community 60 - "Community 60"
Cohesion: 0.40
Nodes (5): StyleCop.Analyzers, contentHash, requested, resolved, type

### Community 62 - "Community 62"
Cohesion: 0.67
Nodes (3): net10.0, spacetradersv3.application, type

## Knowledge Gaps
- **97 isolated node(s):** `AuthMode`, `SpaceTraders.Infrastructure.SpaceTradersAPI`, `ApiResponse`, `net10.0`, `Microsoft.NET.Sdk` (+92 more)
  These have ≤1 connection - possible missing edges or undocumented components.
- **5 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `SpaceTraders.Application.Interfaces.Repositories` connect `Community 5` to `Community 33`, `Community 6`, `Community 8`, `Community 12`, `Community 17`, `Community 18`, `Community 19`, `Community 20`, `Community 23`, `Community 24`, `Community 25`, `Community 26`, `Community 27`?**
  _High betweenness centrality (0.271) - this node is a cross-community bridge._
- **Why does `SpaceTraders.Application.Interfaces` connect `Community 21` to `Community 1`, `Community 7`, `Community 8`, `Community 9`, `Community 13`, `Community 15`, `Community 16`, `Community 50`, `Community 19`, `Community 22`, `Community 28`, `Community 29`?**
  _High betweenness centrality (0.252) - this node is a cross-community bridge._
- **Why does `SpaceTraders.Application.Ports` connect `Community 6` to `Community 0`, `Community 33`, `Community 5`, `Community 7`, `Community 8`, `Community 9`, `Community 13`?**
  _High betweenness centrality (0.194) - this node is a cross-community bridge._
- **What connects `AuthMode`, `SpaceTraders.Infrastructure.SpaceTradersAPI`, `ApiResponse` to the rest of the system?**
  _97 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `Community 0` be split into smaller, more focused modules?**
  _Cohesion score 0.06974789915966387 - nodes in this community are weakly interconnected._
- **Should `Community 1` be split into smaller, more focused modules?**
  _Cohesion score 0.06801093643198906 - nodes in this community are weakly interconnected._
- **Should `Community 2` be split into smaller, more focused modules?**
  _Cohesion score 0.06398390342052314 - nodes in this community are weakly interconnected._