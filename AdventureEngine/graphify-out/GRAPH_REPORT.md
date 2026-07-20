# Graph Report - .  (2026-07-20)

## Corpus Check
- cluster-only mode — file stats not available

## Summary
- 354 nodes · 468 edges · 27 communities (25 shown, 2 thin omitted)
- Extraction: 96% EXTRACTED · 4% INFERRED · 0% AMBIGUOUS · INFERRED: 17 edges (avg confidence: 0.73)
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

## God Nodes (most connected - your core abstractions)
1. `GameSessionService` - 22 edges
2. `GameSessionTests` - 14 edges
3. `AdventureEngine.Application.Agents` - 13 edges
4. `NarratorAgent` - 11 edges
5. `GameSession` - 10 edges
6. `AdventureEngine.Tests` - 9 edges
7. `AdventureEngine.Infrastructure` - 8 edges
8. `NarratorContext` - 7 edges
9. `AdventureEngine.Domain` - 7 edges
10. `AdventureEngine.Application` - 6 edges

## Surprising Connections (you probably didn't know these)
- `GameSession` --references--> `SessionStatus`  [EXTRACTED]
  src/AdventureEngine.Domain/GameSession.cs → src/AdventureEngine.Domain/SessionStatus.cs
- `GameSessionService` --references--> `ModerationAgent`  [EXTRACTED]
  src/AdventureEngine.Infrastructure/GameSessionService.cs → src/AdventureEngine.Infrastructure/Agents/ModerationAgent.cs
- `DirectorAgent` --implements--> `IDirectorAgent`  [EXTRACTED]
  src/AdventureEngine.Infrastructure/Agents/DirectorAgent.cs → src/AdventureEngine.Application/Agents/IDirectorAgent.cs
- `GameSession` --references--> `WorldManifest`  [EXTRACTED]
  src/AdventureEngine.Domain/GameSession.cs → src/AdventureEngine.Domain/WorldManifest.cs

## Import Cycles
- None detected.

## Communities (27 total, 2 thin omitted)

### Community 0 - "Community 0"
Cohesion: 0.07
Nodes (26): AdventureEngine.Application.Agents, AgentUsage, INarratorAgent, Action, CancellationToken, IAsyncEnumerable, IReadOnlyList, NarratorResponse (+18 more)

### Community 1 - "Community 1"
Cohesion: 0.11
Nodes (21): Exception, Func, GeneratedRegex, IDocumentSession, IGameSessionService, Regex, GameSessionService, CancellationToken (+13 more)

### Community 2 - "Community 2"
Cohesion: 0.08
Nodes (25): AdventureEngine.Tests, AdventureEngine.Domain, AdventureEngine.Domain.Events, AdventureEngine.Application.Sessions, DateTime, Dictionary, ChapterCompleted, ChapterStarted (+17 more)

### Community 3 - "Community 3"
Cohesion: 0.10
Nodes (22): INarratorAgent, NarratorAgent, Action, AgentUsage, AnthropicClient, CancellationToken, ChapterDefinition, IAsyncEnumerable (+14 more)

### Community 4 - "Community 4"
Cohesion: 0.17
Nodes (11): ChapterCompleted, GameEnded, PlayerActed, SceneEntered, SessionCreated, GameSessionTests, Fact, nextSceneId (+3 more)

### Community 5 - "Community 5"
Cohesion: 0.11
Nodes (14): Result, ModerationResult, ModerationResponse, ModerationResultParser, ModerationAgent, AgentUsage, AnthropicClient, CancellationToken (+6 more)

### Community 6 - "Community 6"
Cohesion: 0.13
Nodes (10): AdventureEngine.Infrastructure.Agents, AdventureEngine.Infrastructure, AdventureEngine.Infrastructure.Anthropic, IConfiguration, IServiceCollection, AnthropicOptions, string, MartenConfiguration (+2 more)

### Community 7 - "Community 7"
Cohesion: 0.15
Nodes (18): Anthropic.SDK (5.10.0), AspNetCore.HealthChecks.NpgSql (9.0.0), coverlet.collector (6.0.4), Marten (8.37.2), Microsoft.Extensions.Options.ConfigurationExtensions (10.0.0), Microsoft.NET.Test.Sdk (17.14.1), xunit (2.9.3), xunit.runner.visualstudio (3.1.4) (+10 more)

### Community 8 - "Community 8"
Cohesion: 0.11
Nodes (17): DataAnnotationsValidator, EditForm, InputTextArea, NavigationManager, route:/, ClearNameAsync, HandleNameKeyDownAsync, LoadPastSessionsAsync (+9 more)

### Community 9 - "Community 9"
Cohesion: 0.13
Nodes (15): ASPNETCORE_ENVIRONMENT, applicationUrl, commandName, dotnetRunMessages, environmentVariables, launchBrowser, applicationUrl, commandName (+7 more)

### Community 10 - "Community 10"
Cohesion: 0.14
Nodes (12): AdventureEngine.Application.Sessions, AdventureEngine.Domain, AdventureEngine.Web, AdventureEngine.Web.Components, AdventureEngine.Web.Components.Layout, Microsoft.AspNetCore.Components.Forms, Microsoft.AspNetCore.Components.Routing, Microsoft.AspNetCore.Components.Web (+4 more)

### Community 11 - "Community 11"
Cohesion: 0.15
Nodes (8): JsonResponseParser, CancellationToken, Task, Usage, World, WorldManifest, WorldManifestTests, Fact

### Community 12 - "Community 12"
Cohesion: 0.17
Nodes (10): IDirectorAgent, CancellationToken, Task, Usage, World, WorldManifest, DirectorAgent, AnthropicClient (+2 more)

### Community 13 - "Community 13"
Cohesion: 0.35
Nodes (6): IGameSessionService, CancellationToken, GameSession, Guid, IReadOnlyList, Task

### Community 14 - "Community 14"
Cohesion: 0.20
Nodes (9): INpcAgent, NpcAgent, AgentUsage, AnthropicClient, CancellationToken, Dialogue, NpcContext, Task (+1 more)

### Community 15 - "Community 15"
Cohesion: 0.22
Nodes (8): GameTerminal, route:/play/{SessionId:guid}, Dispose, HandleKeyDownAsync, OnInitializedAsync, IGameSessionService, PageTitle, SubmitActionAsync

### Community 16 - "Community 16"
Cohesion: 0.32
Nodes (6): handleReconnectStateChanged(), reconnectModal, resumeButton, retry(), retryButton, retryWhenDocumentBecomesVisible()

### Community 17 - "Community 17"
Cohesion: 0.33
Nodes (5): HeadOutlet, ImportMap, ReconnectModal, ResourcePreloader, Routes

### Community 18 - "Community 18"
Cohesion: 0.40
Nodes (4): FocusOnNavigate, Found, Router, RouteView

### Community 19 - "Community 19"
Cohesion: 0.40
Nodes (4): Microsoft.AspNetCore.Components, OnAfterRenderAsync, IJSRuntime, Microsoft.JSInterop

### Community 20 - "Community 20"
Cohesion: 0.50
Nodes (3): OnInitialized, PageTitle, System.Diagnostics

## Knowledge Gaps
- **87 isolated node(s):** `Microsoft.NET.Sdk`, `ModerationResponse`, `Microsoft.NET.Sdk`, `ChapterStarted`, `SceneEntered` (+82 more)
  These have ≤1 connection - possible missing edges or undocumented components.
- **2 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `AdventureEngine.Application.Agents` connect `Community 0` to `Community 2`, `Community 5`, `Community 6`, `Community 11`, `Community 12`?**
  _High betweenness centrality (0.240) - this node is a cross-community bridge._
- **Why does `GameSessionService` connect `Community 1` to `Community 5`, `Community 6`?**
  _High betweenness centrality (0.124) - this node is a cross-community bridge._
- **What connects `Microsoft.NET.Sdk`, `ModerationResponse`, `Microsoft.NET.Sdk` to the rest of the system?**
  _87 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `Community 0` be split into smaller, more focused modules?**
  _Cohesion score 0.06951871657754011 - nodes in this community are weakly interconnected._
- **Should `Community 1` be split into smaller, more focused modules?**
  _Cohesion score 0.10695187165775401 - nodes in this community are weakly interconnected._
- **Should `Community 2` be split into smaller, more focused modules?**
  _Cohesion score 0.07765151515151515 - nodes in this community are weakly interconnected._
- **Should `Community 3` be split into smaller, more focused modules?**
  _Cohesion score 0.10052910052910052 - nodes in this community are weakly interconnected._