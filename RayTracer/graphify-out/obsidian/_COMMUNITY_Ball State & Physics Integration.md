---
type: community
cohesion: 0.11
members: 39
---

# Ball State & Physics Integration

**Cohesion:** 0.11 - loosely connected
**Members:** 39 nodes

## Members
- [[.AtRest()]] - code - Pinball.Core/Physics/BallState.cs
- [[.Ball_RollsDownAndDrainsThroughTheCentre_StayingContained()]] - code - Pinball.Tests/PinballIntegrationTests.cs
- [[.BallisticArc_ConservesMechanicalEnergy()]] - code - Pinball.Tests/PhysicsValidationTests.cs
- [[.Ccd_FastBallDoesNotTunnelThroughWall()]] - code - Pinball.Tests/PhysicsValidationTests.cs
- [[.ContactEnergy_NeverIncreasesAcrossBouncesAndRolling()]] - code - Pinball.Tests/PhysicsRobustnessTests.cs
- [[.Determinism_SameSeedReproducesBitIdentically()]] - code - Pinball.Tests/PhysicsValidationTests.cs
- [[.Drag_TerminalVelocityAndNegligibleAtPinballSpeeds()]] - code - Pinball.Tests/PhysicsValidationTests.cs
- [[.ExternalAcceleration()]] - code - Pinball.Core/Physics/Forces.cs
- [[.Finite()]] - code - Pinball.Tests/PinballIntegrationTests.cs
- [[.FlatPlayfield_TiltedGravity_RollsAtFiveSevenths()]] - code - Pinball.Tests/PhysicsRobustnessTests.cs
- [[.Flip_KnocksADescendingBallBackUpTable()]] - code - Pinball.Tests/PinballIntegrationTests.cs
- [[.FrictionlessIncline_ReachesAnalyticSpeed()]] - code - Pinball.Tests/PhysicsValidationTests.cs
- [[.InclinedPlayfield()]] - code - Pinball.Core/Physics/Colliders/PlaneCollider.cs
- [[.IsDrained()]] - code - Pinball.Core/Table/PinballTable.cs
- [[.Magnus_LateralCurveMatchesFrozenPrediction()]] - code - Pinball.Tests/PhysicsValidationTests.cs
- [[.MechanicalEnergy()]] - code - Pinball.Core/Physics/BallState.cs
- [[.NewBall()]] - code - Pinball.Core/Table/PinballTable.cs
- [[.PlayfieldPoint()]] - code - Pinball.Core/Table/PinballTable.cs
- [[.RestingBall_DoesNotDriftOrCreep()]] - code - Pinball.Tests/PhysicsRobustnessTests.cs
- [[.Restitution_ApexHeightsDecayByESquared()]] - code - Pinball.Tests/PhysicsValidationTests.cs
- [[.RollingOnIncline_SteadyAccelerationIsFiveSevenths()]] - code - Pinball.Tests/PhysicsValidationTests.cs
- [[.Run()_2]] - code - Pinball.Tests/PhysicsRobustnessTests.cs
- [[.Run()_3]] - code - Pinball.Tests/PhysicsValidationTests.cs
- [[.SkidToRoll_CatchesAtFiveSeventhsOfInitialSpeed()]] - code - Pinball.Tests/PhysicsValidationTests.cs
- [[.Substep()]] - code - Pinball.Core/Physics/PhysicsWorld.cs
- [[.TableSimulation_IsDeterministic()]] - code - Pinball.Tests/PinballIntegrationTests.cs
- [[.TightLane_FastBallStaysContainedAndGainsNoEnergy()]] - code - Pinball.Tests/PhysicsRobustnessTests.cs
- [[BallState]] - code - Pinball.Core/Physics/BallState.cs
- [[BallState.cs]] - code - Pinball.Core/Physics/BallState.cs
- [[DataRow]] - code
- [[PhysicsRobustnessTests]] - code - Pinball.Tests/PhysicsRobustnessTests.cs
- [[PhysicsValidationTests]] - code - Pinball.Tests/PhysicsValidationTests.cs
- [[PinballIntegrationTests]] - code - Pinball.Tests/PinballIntegrationTests.cs
- [[TestMethod_6]] - code
- [[TestMethod_7]] - code
- [[TestMethod_9]] - code
- [[double_22]] - code
- [[double_23]] - code
- [[double_25]] - code

## Live Query (requires Dataview plugin)

```dataview
TABLE source_file, type FROM #community/Ball_State__Physics_Integration
SORT file.name ASC
```

## Connections to other communities
- 12 edges to [[_COMMUNITY_Physics Actuators & Active Zones]]
- 8 edges to [[_COMMUNITY_Pinball Colliders & AABB]]
- 7 edges to [[_COMMUNITY_Community 39]]
- 7 edges to [[_COMMUNITY_Community 74]]
- 5 edges to [[_COMMUNITY_Community 42]]
- 5 edges to [[_COMMUNITY_Community 40]]
- 3 edges to [[_COMMUNITY_Community 72]]
- 2 edges to [[_COMMUNITY_Pinball App Rendering Loop]]
- 1 edge to [[_COMMUNITY_Data-Driven Table Definition]]
- 1 edge to [[_COMMUNITY_Community 135]]
- 1 edge to [[_COMMUNITY_Community 124]]

## Top bridge nodes
- [[.Substep()]] - degree 26, connects to 5 communities
- [[BallState]] - degree 10, connects to 4 communities
- [[.ExternalAcceleration()]] - degree 8, connects to 4 communities
- [[.PlayfieldPoint()]] - degree 6, connects to 4 communities
- [[.AtRest()]] - degree 15, connects to 3 communities