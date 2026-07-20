# Graph Report - .  (2026-07-20)

## Corpus Check
- cluster-only mode — file stats not available

## Summary
- 939 nodes · 2514 edges · 53 communities (31 shown, 22 thin omitted)
- Extraction: 94% EXTRACTED · 6% INFERRED · 0% AMBIGUOUS · INFERRED: 155 edges (avg confidence: 0.8)
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

## God Nodes (most connected - your core abstractions)
1. `DraftEngineTests` - 76 edges
2. `DraftSessionState` - 75 edges
3. `DraftEngine` - 71 edges
4. `SetupCalculatorDistributionTests` - 60 edges
5. `TheCuratool.Domain` - 56 edges
6. `GameSession` - 48 edges
7. `SetupCalculatorTests` - 41 edges
8. `SetupCounts` - 36 edges
9. `CharacterDatabase` - 29 edges
10. `ISetupRule` - 27 edges

## Surprising Connections (you probably didn't know these)
- `SetupCalculatorDistributionTests` --references--> `SetupCalculator`  [EXTRACTED]
  tests/TheCuratool.UnitTests/SetupCalculatorDistributionTests.cs → src/TheCuratool.Application/SetupCalculator.cs
- `DraftEngineTests` --references--> `CharacterDatabase`  [EXTRACTED]
  tests/TheCuratool.UnitTests/DraftEngineTests.cs → src/TheCuratool.Domain/CharacterDatabase.cs
- `ScriptParserTests` --references--> `CharacterDatabase`  [EXTRACTED]
  tests/TheCuratool.UnitTests/ScriptParserTests.cs → src/TheCuratool.Domain/CharacterDatabase.cs
- `SetupCalculatorDistributionTests` --references--> `CharacterDatabase`  [EXTRACTED]
  tests/TheCuratool.UnitTests/SetupCalculatorDistributionTests.cs → src/TheCuratool.Domain/CharacterDatabase.cs
- `SetupCalculatorTests` --references--> `CharacterDatabase`  [EXTRACTED]
  tests/TheCuratool.UnitTests/SetupCalculatorTests.cs → src/TheCuratool.Domain/CharacterDatabase.cs

## Import Cycles
- None detected.

## Communities (53 total, 22 thin omitted)

### Community 0 - "Community 0"
Cohesion: 0.09
Nodes (23): IReadOnlySet, PendingRequirement, Random, AbilityOption, Dictionary, Guid, HashSet, IReadOnlyCollection (+15 more)

### Community 2 - "Community 2"
Cohesion: 0.06
Nodes (18): bool, IAsyncDisposable, CancellationToken, Guid, IReadOnlyList, StoredScript, Task, IScriptRepository (+10 more)

### Community 3 - "Community 3"
Cohesion: 0.09
Nodes (39): Created, Exception, Func, IEndpointRouteBuilder, Ok, ProblemDetails, Results, SessionLookupResult (+31 more)

### Community 4 - "Community 4"
Cohesion: 0.12
Nodes (10): d, m, o, SetupCalculationResult, IReadOnlyList, Script, t, Fact (+2 more)

### Community 5 - "Community 5"
Cohesion: 0.09
Nodes (14): DemonSources, HasPlainDraftableDemon, InlineData, NonDemonSources, SessionSetupOptions, HashSet, IEnumerable, IReadOnlyCollection (+6 more)

### Community 6 - "Community 6"
Cohesion: 0.09
Nodes (24): DbContext, DbSet, IDisposable, StoredScript, GameSessionEntity, ModelBuilder, PlayerSlotEntity, ScriptEntity (+16 more)

### Community 7 - "Community 7"
Cohesion: 0.17
Nodes (13): CancellationToken, GameSession, GameSessionEntity, Guid, PlayerSlot, PlayerSlotEntity, ScriptEntity, Task (+5 more)

### Community 8 - "Community 8"
Cohesion: 0.12
Nodes (11): AvailabilityContext, BlockedIfAnyChosenOfTypeConstraint, IReadOnlyCollection, IReadOnlyDictionary, IReadOnlyList, JsonElement, CharacterDatabase, DynamicAbilityScope (+3 more)

### Community 9 - "Community 9"
Cohesion: 0.11
Nodes (5): TheCuratool.Domain, TheCuratool.UnitTests, TheCuratool.Application, PendingRequirement, GameStatus

### Community 10 - "Community 10"
Cohesion: 0.10
Nodes (26): net10.0, .net, Microsoft.NET.Sdk, AspNetCore.HealthChecks.NpgSql (9.0.0), DotNetProjectFile.Analyzers.Sdk (1.12.2), Microsoft.AspNetCore.Components.Web (9.0.0), Microsoft.AspNetCore.Mvc.Testing (9.0.0), Microsoft.EntityFrameworkCore (9.0.0) (+18 more)

### Community 11 - "Community 11"
Cohesion: 0.08
Nodes (24): route:/draft, route:/draft/{SessionId:guid}, AddCuratedRole, BeginCurate, ConfirmCurated, FormatChosenCharacter, FormatOfferOption, GetCurateSecondarySelectorPlaceholder (+16 more)

### Community 12 - "Community 12"
Cohesion: 0.18
Nodes (8): JsonDocument, JsonElement, List, ScriptParser, ScriptParseResult, Stream, Fact, ScriptParserTests

### Community 13 - "Community 13"
Cohesion: 0.30
Nodes (4): TestFixture, Fact, Task, DraftSessionStateTests

### Community 14 - "Community 14"
Cohesion: 0.14
Nodes (8): TheCuratool.Infrastructure, TheCuratool.Infrastructure.Data, TheCuratool.Infrastructure.Repositories, TheCuratool.Application.Abstractions.Repositories, TheCuratool.Web, IConfiguration, IServiceCollection, ServiceCollectionExtensions

### Community 15 - "Community 15"
Cohesion: 0.21
Nodes (7): ISetupRule, IReadOnlyCollection, IReadOnlyDictionary, IReadOnlyList, JsonElement, LoricDatabase, LoricDefinition

### Community 16 - "Community 16"
Cohesion: 0.13
Nodes (8): IEnumerable, IEnumerable, LoricSetupRule, IEnumerable, MinionSwapSetupRule, IEnumerable, ReplaceDemonOutsiderDisplayRule, SetupContext

### Community 17 - "Community 17"
Cohesion: 0.16
Nodes (11): TheCuratool.Infrastructure.Entities, DateTimeOffset, Guid, ICollection, GameSessionEntity, Guid, PlayerSlotEntity, DateTimeOffset (+3 more)

### Community 18 - "Community 18"
Cohesion: 0.29
Nodes (8): CuratoolWebApplicationFactory, HttpClient, IClassFixture, JsonSerializerOptions, Fact, Guid, Task, Phase8ApiTests

### Community 19 - "Community 19"
Cohesion: 0.14
Nodes (13): Microsoft.AspNetCore.Components, Microsoft.AspNetCore.Components.Forms, Microsoft.AspNetCore.Components.Routing, Microsoft.AspNetCore.Components.Web, System, System.Collections.Generic, System.IO, System.Linq (+5 more)

### Community 20 - "Community 20"
Cohesion: 0.15
Nodes (12): InputNumber, route:/setup, OnLegionCountChanged, OnLegionGameChanged, OnLoricChanged, OnUseAtheistChanged, OnUseMarionetteChanged, DraftSessionState (+4 more)

### Community 21 - "Community 21"
Cohesion: 0.15
Nodes (12): Microsoft.AspNetCore.Components, Microsoft.AspNetCore.Components.Forms, Microsoft.AspNetCore.Components.Web, System, System.Collections.Generic, System.IO, System.Linq, TheCuratool.Application (+4 more)

### Community 22 - "Community 22"
Cohesion: 0.17
Nodes (8): TheCuratool.Web.Api, IWebHostBuilder, EmptySession, SessionLookupResult, Program, string, CuratoolWebApplicationFactory, WebApplicationFactory

### Community 23 - "Community 23"
Cohesion: 0.18
Nodes (10): route:/summary, route:/summary/{SessionId:guid}, FormatChosenCharacter, GetHiddenRoleLabel, OnParametersSetAsync, DraftSessionState, IAvailabilityConstraint, ISetupRule (+2 more)

### Community 24 - "Community 24"
Cohesion: 0.22
Nodes (8): InputFile, route:/, HandleFileSelected, LoadScript, OnInitializedAsync, OnStoredScriptChanged, DraftSessionState, NavigationManager

### Community 25 - "Community 25"
Cohesion: 0.25
Nodes (7): FocusOnNavigate, Found, LayoutView, Router, RouteView, Microsoft.AspNetCore.Components.Routing, TheCuratool.Web.Components.Layout

### Community 26 - "Community 26"
Cohesion: 0.33
Nodes (3): TheCuratool.Infrastructure.Migrations, ModelBuilder, AddSpecialCaseFields3

### Community 27 - "Community 27"
Cohesion: 0.53
Nodes (5): IReadOnlyList, ChosenChoice, PlayerChoice, UnchosenChoice, UnchosenChoice

### Community 29 - "Community 29"
Cohesion: 0.40
Nodes (3): ModelSnapshot, ModelBuilder, CuratoolDbContextModelSnapshot

### Community 30 - "Community 30"
Cohesion: 0.50
Nodes (3): Migration, MigrationBuilder, InitialCreate

### Community 35 - "Community 35"
Cohesion: 0.50
Nodes (3): HeadOutlet, Routes, NavigationManager

## Knowledge Gaps
- **114 isolated node(s):** `net10.0`, `DotNetProjectFile.Analyzers.Sdk (1.12.2)`, `Microsoft.NET.Sdk`, `PendingRequirement`, `Microsoft.NET.Sdk` (+109 more)
  These have ≤1 connection - possible missing edges or undocumented components.
- **22 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `DraftSessionState` connect `Community 2` to `Community 0`, `Community 3`, `Community 4`, `Community 5`, `Community 6`, `Community 8`, `Community 12`, `Community 14`, `Community 15`?**
  _High betweenness centrality (0.159) - this node is a cross-community bridge._
- **Why does `TheCuratool.Domain` connect `Community 9` to `Community 3`, `Community 8`, `Community 14`, `Community 15`, `Community 16`, `Community 22`, `Community 27`, `Community 36`, `Community 37`, `Community 38`, `Community 39`, `Community 40`, `Community 41`, `Community 42`, `Community 43`, `Community 44`, `Community 45`, `Community 46`, `Community 51`?**
  _High betweenness centrality (0.141) - this node is a cross-community bridge._
- **Why does `TheCuratool.Infrastructure.Data` connect `Community 14` to `Community 6`, `Community 47`, `Community 48`, `Community 49`, `Community 22`, `Community 26`, `Community 29`?**
  _High betweenness centrality (0.107) - this node is a cross-community bridge._
- **What connects `net10.0`, `DotNetProjectFile.Analyzers.Sdk (1.12.2)`, `Microsoft.NET.Sdk` to the rest of the system?**
  _114 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `Community 0` be split into smaller, more focused modules?**
  _Cohesion score 0.09090909090909091 - nodes in this community are weakly interconnected._
- **Should `Community 1` be split into smaller, more focused modules?**
  _Cohesion score 0.11368421052631579 - nodes in this community are weakly interconnected._
- **Should `Community 2` be split into smaller, more focused modules?**
  _Cohesion score 0.06409130816505706 - nodes in this community are weakly interconnected._