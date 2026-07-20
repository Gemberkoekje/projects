---
type: community
cohesion: 0.08
members: 55
---

# Lens Sampling & Camera Optics

**Cohesion:** 0.08 - loosely connected
**Members:** 55 nodes

## Members
- [[.ApertureRadiusWorld()]] - code - RayTracer.Core/Rendering/LensOptions.cs
- [[.ApertureRadius_FollowsTheFNumberDefinition()]] - code - RayTracer.Tests/LensTests.cs
- [[.ApertureSamples_StayWithinTheApertureAndSpread()]] - code - RayTracer.Tests/LensTests.cs
- [[.ApertureScale_IsSyntheticAndDoesNotChangeFraming()]] - code - RayTracer.Tests/LensTests.cs
- [[.ApplyCatEye()]] - code - RayTracer.Core/Pipeline/LensSampler.cs
- [[.BarrelDistortion_BendsEdgeRaysTowardTheAxis()]] - code - RayTracer.Tests/LensTests.cs
- [[.BladedIris_ProducesAPolygonNotADisc()]] - code - RayTracer.Tests/LensTests.cs
- [[.Box()]] - code - RayTracer.Tests/LensTests.cs
- [[.BuildJobSystem()_2]] - code - RayTracer.Tests/LensTests.cs
- [[.CatEye_LeavesTheTangentialAxisAlone()]] - code - RayTracer.Tests/LensTests.cs
- [[.CatEye_SquashesMoreTowardTheCorners()]] - code - RayTracer.Tests/LensTests.cs
- [[.CatEye_SquashesOffAxisBokehButLeavesTheAxisRound()]] - code - RayTracer.Tests/LensTests.cs
- [[.Distort()]] - code - RayTracer.Core/Pipeline/LensSampler.cs
- [[.Distortion_GrowsWithRadius()]] - code - RayTracer.Tests/LensTests.cs
- [[.Distortion_LeavesTheOpticalAxisFixed()]] - code - RayTracer.Tests/LensTests.cs
- [[.EffectiveFocalLengthMm()]] - code - RayTracer.Core/Rendering/LensOptions.cs
- [[.EveryApertureSample_ConvergesOnTheFocusPoint()]] - code - RayTracer.Tests/LensTests.cs
- [[.Flat()_1]] - code - RayTracer.Tests/LensTests.cs
- [[.FromModel()]] - code - RayTracer.Core/Rendering/LensOptions.cs
- [[.GenerateLocalRay()]] - code - RayTracer.Core/Pipeline/LensSampler.cs
- [[.ImpliedFocalLength_RoundTripsWithTheCameraFov()]] - code - RayTracer.Tests/LensTests.cs
- [[.LensWithoutZoom_DoesNotReframeTheShot()]] - code - RayTracer.Tests/LensTests.cs
- [[.NextFloat()_1]] - code - RayTracer.Core/Pipeline/LensSampler.cs
- [[.PhonePortrait()]] - code - RayTracer.Core/Rendering/LensOptions.cs
- [[.PhonePortrait_DefocusesFarMoreThanThePhysicalPhone()]] - code - RayTracer.Tests/LensTests.cs
- [[.Phone_HasFarLessDefocusThanCine_AtTheSameFraming()]] - code - RayTracer.Tests/LensTests.cs
- [[.PincushionDistortion_BendsEdgeRaysAwayFromTheAxis()]] - code - RayTracer.Tests/LensTests.cs
- [[.Pinhole_DrawsNoRandomNumbers()]] - code - RayTracer.Tests/LensTests.cs
- [[.Pinhole_RenderIsBitIdenticalToNoLens()]] - code - RayTracer.Tests/LensTests.cs
- [[.Pinhole_ReproducesHistoricalRayExactly()]] - code - RayTracer.Tests/LensTests.cs
- [[.RenderScene()]] - code - RayTracer.Tests/LensTests.cs
- [[.ResolveLens()]] - code - RayTracer.Maze/ConfigForm.cs
- [[.SampleAperture()]] - code - RayTracer.Core/Pipeline/LensSampler.cs
- [[.SimpleCamera_VignettesHarderThanThePhone()]] - code - RayTracer.Tests/LensTests.cs
- [[.TestCamera()_1]] - code - RayTracer.Tests/LensTests.cs
- [[.UnsetFStop_IsAPinholeAndSkipsApertureSampling()]] - code - RayTracer.Tests/LensTests.cs
- [[.UnsetLensOptions_FallsBackToPinhole()]] - code - RayTracer.Tests/LensTests.cs
- [[.VignetteFactor()]] - code - RayTracer.Core/Pipeline/LensSampler.cs
- [[.Vignette_AtStrengthOne_IsThePhysicalCosFourthLaw()]] - code - RayTracer.Tests/LensTests.cs
- [[.Vignette_DarkensCornersButNotTheCentre()]] - code - RayTracer.Tests/LensTests.cs
- [[.Vignette_IsExactlyOffAtZeroStrength()]] - code - RayTracer.Tests/LensTests.cs
- [[.Vignette_StrengthAboveOne_ExaggeratesTheFalloff()]] - code - RayTracer.Tests/LensTests.cs
- [[.Zoom_NarrowsTheFieldOfView()]] - code - RayTracer.Tests/LensTests.cs
- [[LensModel]] - code - RayTracer.Core/Rendering/LensOptions.cs
- [[LensOptions]] - code - RayTracer.Core/Rendering/LensOptions.cs
- [[LensOptions.cs]] - code - RayTracer.Core/Rendering/LensOptions.cs
- [[LensSampler]] - code - RayTracer.Core/Pipeline/LensSampler.cs
- [[LensSampler.cs]] - code - RayTracer.Core/Pipeline/LensSampler.cs
- [[LensTests]] - code - RayTracer.Tests/LensTests.cs
- [[TestMethod_35]] - code
- [[Vector3_36]] - code
- [[Vector3_76]] - code
- [[float_24]] - code
- [[float_53]] - code
- [[uint_5]] - code

## Live Query (requires Dataview plugin)

```dataview
TABLE source_file, type FROM #community/Lens_Sampling__Camera_Optics
SORT file.name ASC
```

## Connections to other communities
- 8 edges to [[_COMMUNITY_GPU Scene Primitives & Phase1]]
- 5 edges to [[_COMMUNITY_Maze Modes & Volumetrics]]
- 3 edges to [[_COMMUNITY_RayTracer Test Suite]]
- 3 edges to [[_COMMUNITY_Maze Program & Bitmap Output]]
- 2 edges to [[_COMMUNITY_CPU Reference Renderer & Phases]]
- 2 edges to [[_COMMUNITY_Community 84]]
- 2 edges to [[_COMMUNITY_Community 145]]
- 2 edges to [[_COMMUNITY_Community 44]]
- 1 edge to [[_COMMUNITY_Community 38]]
- 1 edge to [[_COMMUNITY_Community 61]]
- 1 edge to [[_COMMUNITY_Community 57]]
- 1 edge to [[_COMMUNITY_Community 97]]
- 1 edge to [[_COMMUNITY_Accumulation & Frame Diagnostics]]
- 1 edge to [[_COMMUNITY_DXR Acceleration Structures]]

## Top bridge nodes
- [[LensOptions]] - degree 16, connects to 7 communities
- [[.FromModel()]] - degree 20, connects to 2 communities
- [[.GenerateLocalRay()]] - degree 12, connects to 2 communities
- [[.VignetteFactor()]] - degree 8, connects to 2 communities
- [[.BuildJobSystem()_2]] - degree 7, connects to 2 communities