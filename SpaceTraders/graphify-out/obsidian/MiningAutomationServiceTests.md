---
source_file: "tests/SpaceTraders.Application.Tests/Automation/MiningAutomationServiceTests.cs"
type: "code"
community: "Community 66"
location: "L15"
tags:
  - graphify/code
  - graphify/EXTRACTED
  - community/Community_66
---

# MiningAutomationServiceTests

## Connections
- [[.CreateService()]] - `method` [EXTRACTED]
- [[.EnsureBootstrappedAsync_AssignsIdleMiner_ForScarceMineralExchange()]] - `method` [EXTRACTED]
- [[.EnsureBootstrappedAsync_AssignsIdleMiner_ForScarceMineralImport()]] - `method` [EXTRACTED]
- [[.EnsureBootstrappedAsync_AssignsMiner_WhenExistingGoalIsCompleted()]] - `method` [EXTRACTED]
- [[.EnsureBootstrappedAsync_ClearsStaleMiningGoal_WhenOpportunityNoLongerScarce()]] - `method` [EXTRACTED]
- [[.EnsureBootstrappedAsync_DoesNotPurchase_WhenDroneCapReached()]] - `method` [EXTRACTED]
- [[.EnsureBootstrappedAsync_DoesNotPurchase_WhenIdleMinerExists()]] - `method` [EXTRACTED]
- [[.EnsureBootstrappedAsync_DoesNothing_WhenMarketIsNotScarceDemandMineral()]] - `method` [EXTRACTED]
- [[.EnsureBootstrappedAsync_PersistsAssignedQueue_WhenMinerIsAssigned()]] - `method` [EXTRACTED]
- [[.EnsureBootstrappedAsync_PersistsPendingQueue_WhenNoMinerCanBeAssigned()]] - `method` [EXTRACTED]
- [[.EnsureBootstrappedAsync_PurchasesMiner_WhenNoIdleMinerAndBudgetAllows()]] - `method` [EXTRACTED]
- [[.EnsureBootstrappedAsync_ReassignsShipAfterClearingStaleGoal_WhenNewScarceOpportunityExists()]] - `method` [EXTRACTED]
- [[.EnsureBootstrappedAsync_TransitionsPersistedPendingQueue_ToAssigned_WhenMinerBecomesAvailable()]] - `method` [EXTRACTED]
- [[.EnsureBootstrappedAsync_UsesClosestAsteroidToSellWaypoint_WhenMultipleMatchingSourcesExist()]] - `method` [EXTRACTED]
- [[.Good()]] - `method` [EXTRACTED]
- [[.HandleAgentCreditsChangedEvent_TransitionsPersistedPendingQueue_ToAssigned_WhenMinerBecomesAvailable()]] - `method` [EXTRACTED]
- [[.HandleShipBecameIdleEvent_TransitionsPersistedPendingQueue_ToAssigned_WhenMinerBecomesAvailable()]] - `method` [EXTRACTED]
- [[.Miner()]] - `method` [EXTRACTED]
- [[.MiningShipyard()]] - `method` [EXTRACTED]
- [[.Snapshot()]] - `method` [EXTRACTED]
- [[.Waypoint()]] - `method` [EXTRACTED]
- [[DateTimeOffset_95]] - `references` [EXTRACTED]
- [[IAgentRepository]] - `references` [EXTRACTED]
- [[IMarketRepository]] - `references` [EXTRACTED]
- [[IPlanRepository]] - `references` [EXTRACTED]
- [[ISettingsRepository]] - `references` [EXTRACTED]
- [[IShipGoalRepository]] - `references` [EXTRACTED]
- [[IShipPurchaseService]] - `references` [EXTRACTED]
- [[IShipRepository]] - `references` [EXTRACTED]
- [[IShipyardRepository]] - `references` [EXTRACTED]
- [[IWaypointRepository]] - `references` [EXTRACTED]
- [[MiningAutomationServiceTests.cs]] - `contains` [EXTRACTED]

#graphify/code #graphify/EXTRACTED #community/Community_66