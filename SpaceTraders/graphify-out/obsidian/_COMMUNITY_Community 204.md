---
type: community
cohesion: 0.45
members: 11
---

# Community 204

**Cohesion:** 0.45 - moderately connected
**Members:** 11 nodes

## Members
- [[.ClearDomainEvents_RemovesAllEvents()]] - code - tests/SpaceTraders.Domain.Tests/Aggregates/ShipTests.cs
- [[.CompleteAssignment_WithAssignment_RaisesCompletedEvent()]] - code - tests/SpaceTraders.Domain.Tests/Aggregates/ShipTests.cs
- [[.CompleteAssignment_WithNoAssignment_DoesNotRaiseEvent()]] - code - tests/SpaceTraders.Domain.Tests/Aggregates/ShipTests.cs
- [[.CreateShip()]] - code - tests/SpaceTraders.Domain.Tests/Aggregates/ShipTests.cs
- [[.UpdateFuel_AboveThreshold_DoesNotRaiseEvent()]] - code - tests/SpaceTraders.Domain.Tests/Aggregates/ShipTests.cs
- [[.UpdateFuel_AtExactlyThreshold_DoesNotRaiseEvent()]] - code - tests/SpaceTraders.Domain.Tests/Aggregates/ShipTests.cs
- [[.UpdateFuel_BelowThreshold_RaisesShipFuelLowEvent()]] - code - tests/SpaceTraders.Domain.Tests/Aggregates/ShipTests.cs
- [[.UpdateNav_FromInTransit_RaisesShipArrivedAtWaypointEvent()]] - code - tests/SpaceTraders.Domain.Tests/Aggregates/ShipTests.cs
- [[.UpdateNav_NotFromInTransit_DoesNotRaiseArrivedEvent()]] - code - tests/SpaceTraders.Domain.Tests/Aggregates/ShipTests.cs
- [[Fact_33]] - code
- [[ShipTests]] - code - tests/SpaceTraders.Domain.Tests/Aggregates/ShipTests.cs

## Live Query (requires Dataview plugin)

```dataview
TABLE source_file, type FROM #community/Community_204
SORT file.name ASC
```

## Connections to other communities
- 1 edge to [[_COMMUNITY_Community 54]]
- 1 edge to [[_COMMUNITY_Community 84]]
- 1 edge to [[_COMMUNITY_Community 160]]

## Top bridge nodes
- [[.CreateShip()]] - degree 11, connects to 2 communities
- [[ShipTests]] - degree 10, connects to 1 community