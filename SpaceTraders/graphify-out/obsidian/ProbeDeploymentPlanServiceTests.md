---
source_file: "tests/SpaceTraders.Application.Tests/Automation/ProbeDeploymentPlanServiceTests.cs"
type: "code"
community: "Community 38"
location: "L14"
tags:
  - graphify/code
  - graphify/EXTRACTED
  - community/Community_38
---

# ProbeDeploymentPlanServiceTests

## Connections
- [[.ActivePlan()]] - `method` [EXTRACTED]
- [[.AdvanceAsync_CompletesPlan_WhenAllTargetsDeployed()]] - `method` [EXTRACTED]
- [[.AdvanceAsync_DoesNothing_WhenNoPlanExists()]] - `method` [EXTRACTED]
- [[.AdvanceAsync_MarksWaypointDeployed_AndDispatchesNextTarget()]] - `method` [EXTRACTED]
- [[.AdvanceAsync_SkipsAlreadyDeployedWaypoint()]] - `method` [EXTRACTED]
- [[.Agent()]] - `method` [EXTRACTED]
- [[.CreateService()_1]] - `method` [EXTRACTED]
- [[.Dispatch_AllowsMarketOnlyTarget_WhenAbovePhase1Threshold()]] - `method` [EXTRACTED]
- [[.Dispatch_DefersMarketOnlyTarget_WhenBelowPhase1Threshold()]] - `method` [EXTRACTED]
- [[.Dispatch_DoesNotDoubleDispatch_SameProbe_ToTwoTargets()]] - `method` [EXTRACTED]
- [[.Dispatch_ExcludesProbeAlreadyAtDeployedWaypoint()]] - `method` [EXTRACTED]
- [[.Dispatch_IgnoresInTransitProbe_AndFallsThroughToPurchase()]] - `method` [EXTRACTED]
- [[.Dispatch_PrioritisesShipyardTargets_BeforeMarketOnly()]] - `method` [EXTRACTED]
- [[.Dispatch_PurchasesProbeForMarketOnlyTarget_WhenPhase1ThresholdMetAndNoneAvailable()]] - `method` [EXTRACTED]
- [[.Dispatch_PurchasesProbe_WhenNoneAvailable_AndCanAfford()]] - `method` [EXTRACTED]
- [[.Dispatch_SendsMultipleProbes_WhenMultipleIdleProbesAvailable()]] - `method` [EXTRACTED]
- [[.Dispatch_SkipsPurchase_WhenBudgetDenies()]] - `method` [EXTRACTED]
- [[.Dispatch_SkipsPurchase_WhenNoShipyardKnown()]] - `method` [EXTRACTED]
- [[.EnsureBootstrappedAsync_CreatesPlan_WithCorrectTargets()]] - `method` [EXTRACTED]
- [[.EnsureBootstrappedAsync_DoesNotResume_WhenPlanIsCompleted()]] - `method` [EXTRACTED]
- [[.EnsureBootstrappedAsync_ReconcilesAndDispatches_WhenNewTargetDiscoveredForActivePlan()]] - `method` [EXTRACTED]
- [[.EnsureBootstrappedAsync_ReopensCompletedPlan_WhenNewTargetDiscovered()]] - `method` [EXTRACTED]
- [[.EnsureBootstrappedAsync_ResumesPlan_WhenPlanAlreadyExists()]] - `method` [EXTRACTED]
- [[.EnsureBootstrappedAsync_SkipsBootstrap_WhenAgentNotFound()]] - `method` [EXTRACTED]
- [[.EnsureBootstrappedAsync_SkipsBootstrap_WhenNoWaypointsFound()]] - `method` [EXTRACTED]
- [[.IsDeployCommand()]] - `method` [EXTRACTED]
- [[.Probe()]] - `method` [EXTRACTED]
- [[.PurchaseResult()]] - `method` [EXTRACTED]
- [[.Shipyard()]] - `method` [EXTRACTED]
- [[.Waypoint()_1]] - `method` [EXTRACTED]
- [[DateTimeOffset_96]] - `references` [EXTRACTED]
- [[IAgentRepository]] - `references` [EXTRACTED]
- [[IBudgetPolicy]] - `references` [EXTRACTED]
- [[IMessageBus_3]] - `references` [EXTRACTED]
- [[IProbeDeploymentPlanRepository]] - `references` [EXTRACTED]
- [[IShipRepository]] - `references` [EXTRACTED]
- [[IShipyardRepository]] - `references` [EXTRACTED]
- [[ISpaceTradersPort]] - `references` [EXTRACTED]
- [[IWaypointRepository]] - `references` [EXTRACTED]
- [[ProbeDeploymentPlanServiceTests.cs]] - `contains` [EXTRACTED]

#graphify/code #graphify/EXTRACTED #community/Community_38