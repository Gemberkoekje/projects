# Graph Report - .  (2026-07-20)

## Corpus Check
- cluster-only mode — file stats not available

## Summary
- 567 nodes · 1514 edges · 16 communities (15 shown, 1 thin omitted)
- Extraction: 86% EXTRACTED · 14% INFERRED · 0% AMBIGUOUS · INFERRED: 205 edges (avg confidence: 0.8)
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

## God Nodes (most connected - your core abstractions)
1. `SimulationContext` - 131 edges
2. `ActorState` - 64 edges
3. `Contract` - 46 edges
4. `MainWindow` - 44 edges
5. `Credits` - 41 edges
6. `ByGalacticAccord.Engine.Domain` - 40 edges
7. `Ship` - 34 edges
8. `Window` - 28 edges
9. `ByGalacticAccord.Engine.Simulation` - 25 edges
10. `LocationId` - 24 edges

## Surprising Connections (you probably didn't know these)
- `ActorState` --references--> `ActorRole`  [EXTRACTED]
  src/ByGalacticAccord.Engine/Actors/ActorState.cs → src/ByGalacticAccord.Engine/Actors/ActorRole.cs
- `ActorState` --references--> `Credits`  [EXTRACTED]
  src/ByGalacticAccord.Engine/Actors/ActorState.cs → src/ByGalacticAccord.Engine/Domain/Credits.cs
- `ActorState` --references--> `ActorId`  [EXTRACTED]
  src/ByGalacticAccord.Engine/Actors/ActorState.cs → src/ByGalacticAccord.Engine/Domain/Ids.cs
- `ActorState` --references--> `LocationId`  [EXTRACTED]
  src/ByGalacticAccord.Engine/Actors/ActorState.cs → src/ByGalacticAccord.Engine/Domain/Ids.cs
- `SimulationContext` --references--> `ActorState`  [EXTRACTED]
  src/ByGalacticAccord.Engine/Simulation/SimulationContext.cs → src/ByGalacticAccord.Engine/Actors/ActorState.cs

## Import Cycles
- None detected.

## Communities (16 total, 1 thin omitted)

### Community 0 - "Community 0"
Cohesion: 0.08
Nodes (15): Game, List, LocationId, DangerBand, Route, RoutePlan, IEnumerable, SimulationContext (+7 more)

### Community 1 - "Community 1"
Cohesion: 0.05
Nodes (23): Entry, Cargo, ContractId, Ship, ShipLogEntry, ShipStatus, IReadOnlyList, List (+15 more)

### Community 2 - "Community 2"
Cohesion: 0.07
Nodes (22): Soak, InflationMonitor, Invariants, InvariantViolation, decimal, IReadOnlyList, List, RunStop (+14 more)

### Community 3 - "Community 3"
Cohesion: 0.08
Nodes (20): Candidate, HaulFeasibility, ActorState, Dictionary, IReadOnlyDictionary, IReadOnlyList, List, DecisionEngine (+12 more)

### Community 4 - "Community 4"
Cohesion: 0.09
Nodes (19): ByGalacticAccord.Engine.Economy, ByGalacticAccord.Engine.Events, ByGalacticAccord.Wpf, ByGalacticAccord.Engine.Domain, ByGalacticAccord.Engine.Random, ByGalacticAccord.Cli, ByGalacticAccord.Engine.Simulation, ByGalacticAccord.Engine.Actors (+11 more)

### Community 5 - "Community 5"
Cohesion: 0.09
Nodes (13): IComparable, Message, Ok, ClauseType, ContractClause, PenaltyOnLateDelivery, QualityWarranty, Contract (+5 more)

### Community 6 - "Community 6"
Cohesion: 0.09
Nodes (20): ActorRole, AccordReach, Location, LocationType, IReadOnlyList, List, ShipClasses, ShipClassInfo (+12 more)

### Community 7 - "Community 7"
Cohesion: 0.08
Nodes (19): DispatcherTimer, double, EventArgs, MouseButtonEventArgs, MainWindow, Brush, Color, Dictionary (+11 more)

### Community 8 - "Community 8"
Cohesion: 0.08
Nodes (33): Cargo, Color, Danger, Deadline, Detail, Fee, Good, Progress (+25 more)

### Community 9 - "Community 9"
Cohesion: 0.14
Nodes (17): AskBox, CloseBtn, DealPanel, OfferText, ResultText, RouteText, TellText, TermsText (+9 more)

### Community 10 - "Community 10"
Cohesion: 0.20
Nodes (10): Negotiation, NegotiationOutcome, NegotiationParty, NegotiationSession, NegotiationStep, NegotiationStepKind, decimal, int (+2 more)

### Community 11 - "Community 11"
Cohesion: 0.16
Nodes (9): Actor, Context, ActorId, ReputationContext, ReputationContextKind, ReputationLedger, Dictionary, IReadOnlyDictionary (+1 more)

### Community 12 - "Community 12"
Cohesion: 0.18
Nodes (13): net10.0-windows, coverlet.collector (6.0.4), Microsoft.NET.Test.Sdk (17.14.1), xunit (2.9.3), xunit.runner.visualstudio (3.1.4), ByGalacticAccord.Cli, Microsoft.NET.Sdk, ByGalacticAccord.Engine (+5 more)

### Community 14 - "Community 14"
Cohesion: 0.28
Nodes (6): EventJournal, JournalEntry, IEnumerable, int, IReadOnlyList, List

### Community 15 - "Community 15"
Cohesion: 0.43
Nodes (3): Application, App, StartupEventArgs

## Knowledge Gaps
- **34 isolated node(s):** `Microsoft.NET.Sdk`, `ActorSnapshot`, `Candidate`, `NegotiationStepKind`, `HaulFeasibility` (+29 more)
  These have ≤1 connection - possible missing edges or undocumented components.
- **1 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `SimulationContext` connect `Community 0` to `Community 1`, `Community 2`, `Community 3`, `Community 4`, `Community 5`, `Community 6`, `Community 7`, `Community 9`, `Community 11`, `Community 13`, `Community 14`?**
  _High betweenness centrality (0.430) - this node is a cross-community bridge._
- **Why does `MainWindow` connect `Community 7` to `Community 0`, `Community 1`, `Community 3`, `Community 4`, `Community 8`, `Community 9`?**
  _High betweenness centrality (0.225) - this node is a cross-community bridge._
- **Why does `ByGalacticAccord.Engine.Domain` connect `Community 4` to `Community 0`, `Community 1`, `Community 2`, `Community 3`, `Community 5`, `Community 6`, `Community 10`, `Community 11`?**
  _High betweenness centrality (0.101) - this node is a cross-community bridge._
- **What connects `Microsoft.NET.Sdk`, `ActorSnapshot`, `Candidate` to the rest of the system?**
  _34 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `Community 0` be split into smaller, more focused modules?**
  _Cohesion score 0.07785547785547786 - nodes in this community are weakly interconnected._
- **Should `Community 1` be split into smaller, more focused modules?**
  _Cohesion score 0.05028248587570622 - nodes in this community are weakly interconnected._
- **Should `Community 2` be split into smaller, more focused modules?**
  _Cohesion score 0.07329462989840348 - nodes in this community are weakly interconnected._