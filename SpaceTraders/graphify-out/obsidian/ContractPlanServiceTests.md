---
source_file: "tests/SpaceTraders.Application.Tests/Automation/ContractPlanServiceTests.cs"
type: "code"
community: "Community 0"
location: "L14"
tags:
  - graphify/code
  - graphify/EXTRACTED
  - community/Community_0
---

# ContractPlanServiceTests

## Connections
- [[.AdvanceAsync_CompletesPlanAndAssignment_WhenContractAlreadyFulfilled()]] - `method` [EXTRACTED]
- [[.AdvanceAsync_CompletesPlanAndAssignment_WhenDeliverableIsSatisfied()]] - `method` [EXTRACTED]
- [[.AdvanceAsync_UpdatesAssignmentRequiredUnits_ToRemainingDeliverableUnits()]] - `method` [EXTRACTED]
- [[.AdvanceAsync_UpdatesPlanProgress_WhenDeliverableStillPending()]] - `method` [EXTRACTED]
- [[.EnsureBootstrappedAsync_AcceptsContract_WhenPendingContractNotAccepted()]] - `method` [EXTRACTED]
- [[.EnsureBootstrappedAsync_CreatesActivePlan_WithIdleMinerShip()]] - `method` [EXTRACTED]
- [[.EnsureBootstrappedAsync_CreatesDeferredPlan_WhenDeliverableIsNonMineral()]] - `method` [EXTRACTED]
- [[.EnsureBootstrappedAsync_CreatesPendingBudgetPlan_WhenNoMinerAndCannotPurchase()]] - `method` [EXTRACTED]
- [[.EnsureBootstrappedAsync_DoesNothing_WhenPlanAlreadyExists()]] - `method` [EXTRACTED]
- [[.EnsureBootstrappedAsync_Negotiates_WhenNoContractsExist()]] - `method` [EXTRACTED]
- [[.EnsureBootstrappedAsync_PrefersNearestAsteroid_OverTraitScoredFarTarget()]] - `method` [EXTRACTED]
- [[.EnsureBootstrappedAsync_RefreshesContractsFromApi_OncePerCycle()]] - `method` [EXTRACTED]
- [[.EnsureBootstrappedAsync_ReplacesNonContractAssignment_WhenActivePlanExists()]] - `method` [EXTRACTED]
- [[.EnsureBootstrappedAsync_RequestsPurchase_WhenNoIdleMinerAndFundsAvailable()]] - `method` [EXTRACTED]
- [[.EnsureBootstrappedAsync_RestoresAssignment_WhenActivePlanExistsButAssignmentMissing()]] - `method` [EXTRACTED]
- [[.EnsureBootstrappedAsync_Retries_WhenExistingPlanIsPendingBudget()]] - `method` [EXTRACTED]
- [[.Handle_DeliverableObtainedEvent_AdvancesPlan_WhenEventMatchesActivePlan()]] - `method` [EXTRACTED]
- [[.Handle_DeliverableObtainedEvent_DoesNotAdvance_WhenEventTradeSymbolDiffers()]] - `method` [EXTRACTED]
- [[ContractPlanServiceTests.cs]] - `contains` [EXTRACTED]

#graphify/code #graphify/EXTRACTED #community/Community_0