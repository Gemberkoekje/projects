---
source_file: "tests/SpaceTraders.Application.Tests/Goals/ShipGoalExecutorServiceTests.cs"
type: "code"
community: "Community 110"
location: "L16"
tags:
  - graphify/code
  - graphify/EXTRACTED
  - community/Community_110
---

# ShipGoalExecutorServiceTests

## Connections
- [[.CreateService()_4]] - `method` [EXTRACTED]
- [[.ExecuteAsync_WhenActiveGoalIsNotScout_ReturnsNull()]] - `method` [EXTRACTED]
- [[.ExecuteAsync_WhenActiveGoalIsTradeBetweenMarkets_DispatchesToExecutor()]] - `method` [EXTRACTED]
- [[.ExecuteAsync_WhenFuelIsNotFull_ReturnsNull()]] - `method` [EXTRACTED]
- [[.ExecuteAsync_WhenGoalExecutes_DoesNotPublishOrScheduleOrMutateState()]] - `method` [EXTRACTED]
- [[.ExecuteAsync_WhenNoExecutorCanHandleScoutGoal_ReturnsNull()]] - `method` [EXTRACTED]
- [[.ExecuteAsync_WhenScoutGoalAndExecutorExists_ReturnsExecutorResult()]] - `method` [EXTRACTED]
- [[.ExecuteAsync_WhenShipNotFound_ReturnsNull()]] - `method` [EXTRACTED]
- [[IMarketRepository]] - `references` [EXTRACTED]
- [[IMessageBus_5]] - `references` [EXTRACTED]
- [[INavigationPlanningService]] - `references` [EXTRACTED]
- [[IScoutAllMarketplacesPlanService]] - `references` [EXTRACTED]
- [[IShipAssignmentRepository]] - `references` [EXTRACTED]
- [[IShipCapabilityRegistry]] - `references` [EXTRACTED]
- [[IShipEventScheduler]] - `references` [EXTRACTED]
- [[IShipGoalExecutor_1]] - `references` [EXTRACTED]
- [[IShipGoalHistoryRepository]] - `references` [EXTRACTED]
- [[IShipGoalRepository]] - `references` [EXTRACTED]
- [[IShipRepository]] - `references` [EXTRACTED]
- [[ISurveyRepository]] - `references` [EXTRACTED]
- [[IWaypointRepository]] - `references` [EXTRACTED]
- [[ShipGoalExecutorServiceTests.cs]] - `contains` [EXTRACTED]
- [[ShipModel_1]] - `references` [EXTRACTED]

#graphify/code #graphify/EXTRACTED #community/Community_110