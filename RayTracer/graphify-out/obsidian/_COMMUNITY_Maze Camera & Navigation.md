---
type: community
cohesion: 0.06
members: 68
---

# Maze Camera & Navigation

**Cohesion:** 0.06 - loosely connected
**Members:** 68 nodes

## Members
- [[.Advance()_4]] - code - RayTracer.Maze.Core/MazeNavigator.cs
- [[.Advance_MovesNavigatorToNextCell()]] - code - RayTracer.Tests/MazeNavigatorTests.cs
- [[.AssertVec3Near()]] - code - RayTracer.Tests/CameraControllerTests.cs
- [[.BeginNextAction()]] - code - RayTracer.Maze.Core/CameraController.cs
- [[.BeginStill()]] - code - RayTracer.Maze.Core/CameraController.cs
- [[.BeginTurn()]] - code - RayTracer.Maze.Core/CameraController.cs
- [[.CellCenter()]] - code - RayTracer.Maze.Core/CameraController.cs
- [[.HeadingToQuaternion()]] - code - RayTracer.Maze.Core/CameraController.cs
- [[.HeadingToQuaternion_ForwardDirectionMatchesExpected()]] - code - RayTracer.Tests/CameraControllerTests.cs
- [[.HeadingToQuaternion_South_IsIdentity()]] - code - RayTracer.Tests/CameraControllerTests.cs
- [[.MultipleUpdates_CameraMovesAwayFromStart()]] - code - RayTracer.Tests/CameraControllerTests.cs
- [[.PeekNext()]] - code - RayTracer.Maze.Core/MazeNavigator.cs
- [[.PeekNext_DoesNotMutateState()]] - code - RayTracer.Tests/MazeNavigatorTests.cs
- [[.PeekNext_NextCellIsAdjacentOrSame()]] - code - RayTracer.Tests/MazeNavigatorTests.cs
- [[.RapidAlternatingUpdates_KeepNavigatorWithinMazeBounds()]] - code - RayTracer.Tests/CameraControllerTests.cs
- [[.Reverse()]] - code - RayTracer.Maze.Core/MazeNavigator.cs
- [[.Reverse_East_ReturnsWest()]] - code - RayTracer.Tests/MazeNavigatorTests.cs
- [[.Reverse_North_ReturnsSouth()]] - code - RayTracer.Tests/MazeNavigatorTests.cs
- [[.SmoothStep()_1]] - code - RayTracer.Maze.Core/CameraController.cs
- [[.ToOffset()]] - code - RayTracer.Maze.Core/MazeNavigator.cs
- [[.ToOffset_MapsCorrectly()]] - code - RayTracer.Tests/MazeNavigatorTests.cs
- [[.ToWall()]] - code - RayTracer.Maze.Core/MazeNavigator.cs
- [[.ToWall_MapsCorrectly()]] - code - RayTracer.Tests/MazeNavigatorTests.cs
- [[.TryBeginImmediateTurn()]] - code - RayTracer.Maze.Core/CameraController.cs
- [[.TurnLeft()]] - code - RayTracer.Maze.Core/MazeNavigator.cs
- [[.TurnLeft_North_ReturnsWest()]] - code - RayTracer.Tests/MazeNavigatorTests.cs
- [[.TurnRight()]] - code - RayTracer.Maze.Core/MazeNavigator.cs
- [[.TurnRight_East_ReturnsSouth()]] - code - RayTracer.Tests/MazeNavigatorTests.cs
- [[.TurnRight_North_ReturnsEast()]] - code - RayTracer.Tests/MazeNavigatorTests.cs
- [[.TurnRight_South_ReturnsWest()]] - code - RayTracer.Tests/MazeNavigatorTests.cs
- [[.TurnRight_West_ReturnsNorth()]] - code - RayTracer.Tests/MazeNavigatorTests.cs
- [[.Update()]] - code - RayTracer.Core/Pipeline/ICameraDriver.cs
- [[.Update()_1]] - code - RayTracer.Maze.Core/CameraController.cs
- [[.Update_DoesNotSetDirty_DuringStill()]] - code - RayTracer.Tests/CameraControllerTests.cs
- [[.Update_DuringStill_WhenFacingWall_TurnsImmediatelyWithoutWaiting()]] - code - RayTracer.Tests/CameraControllerTests.cs
- [[.Update_LargeTime_SnapsToTarget()]] - code - RayTracer.Tests/CameraControllerTests.cs
- [[.Update_StartsTurnImmediately_WhenStillAndFacingWall()]] - code - RayTracer.Tests/CameraControllerTests.cs
- [[.Update_TurnCompletes_AndHeadingUpdates()]] - code - RayTracer.Tests/CameraControllerTests.cs
- [[.Update_ZeroDeltaTime_DoesNotMoveCameraPosition()]] - code - RayTracer.Tests/CameraControllerTests.cs
- [[.WalkDoesNotGetStuck()]] - code - RayTracer.Tests/MazeNavigatorTests.cs
- [[.Walk_ReachesGoalCell_SoRegenerationFires()]] - code - RayTracer.Tests/CameraControllerTests.cs
- [[CameraController]] - code - RayTracer.Maze.Core/CameraController.cs
- [[CameraController.cs]] - code - RayTracer.Maze.Core/CameraController.cs
- [[CameraControllerTests]] - code - RayTracer.Tests/CameraControllerTests.cs
- [[DataRow_1]] - code
- [[DataRow_8]] - code
- [[Direction]] - code - RayTracer.Maze.Core/MazeNavigator.cs
- [[ICameraDriver]] - code - RayTracer.Core/Pipeline/ICameraDriver.cs
- [[ICameraDriver.cs]] - code - RayTracer.Core/Pipeline/ICameraDriver.cs
- [[MazeNavigator]] - code - RayTracer.Maze.Core/MazeNavigator.cs
- [[MazeNavigator.cs]] - code - RayTracer.Maze.Core/MazeNavigator.cs
- [[MazeNavigatorTests]] - code - RayTracer.Tests/MazeNavigatorTests.cs
- [[Quaternion_10]] - code
- [[State]] - code - RayTracer.Maze.Core/CameraController.cs
- [[State_1]] - code
- [[TestMethod_16]] - code
- [[TestMethod_38]] - code
- [[Vector3_52]] - code
- [[Vector3_62]] - code
- [[X]] - code
- [[Y]] - code
- [[dx]] - code
- [[dy]] - code
- [[float_38]] - code
- [[int_28]] - code
- [[nextHeading]] - code
- [[nextX]] - code
- [[nextY]] - code

## Live Query (requires Dataview plugin)

```dataview
TABLE source_file, type FROM #community/Maze_Camera__Navigation
SORT file.name ASC
```

## Connections to other communities
- 7 edges to [[_COMMUNITY_Maze Modes & Volumetrics]]
- 5 edges to [[_COMMUNITY_RayTracer Test Suite]]
- 2 edges to [[_COMMUNITY_Community 57]]
- 2 edges to [[_COMMUNITY_Community 51]]
- 2 edges to [[_COMMUNITY_Community 64]]
- 2 edges to [[_COMMUNITY_Community 149]]
- 2 edges to [[_COMMUNITY_Community 35]]
- 1 edge to [[_COMMUNITY_Community 59]]
- 1 edge to [[_COMMUNITY_Maze Geometry Packing]]
- 1 edge to [[_COMMUNITY_Community 37]]
- 1 edge to [[_COMMUNITY_Community 56]]
- 1 edge to [[_COMMUNITY_Community 85]]
- 1 edge to [[_COMMUNITY_Community 139]]
- 1 edge to [[_COMMUNITY_Community 89]]
- 1 edge to [[_COMMUNITY_Optics & Thin-Film Shading]]
- 1 edge to [[_COMMUNITY_Community 120]]
- 1 edge to [[_COMMUNITY_Community 69]]
- 1 edge to [[_COMMUNITY_Community 65]]

## Top bridge nodes
- [[.HeadingToQuaternion()]] - degree 22, connects to 10 communities
- [[.Update()_1]] - degree 22, connects to 4 communities
- [[CameraController]] - degree 21, connects to 2 communities
- [[.PeekNext()]] - degree 17, connects to 1 community
- [[MazeNavigatorTests]] - degree 14, connects to 1 community