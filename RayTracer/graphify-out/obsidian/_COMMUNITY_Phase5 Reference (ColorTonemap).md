---
type: community
cohesion: 0.16
members: 37
---

# Phase5 Reference (Color/Tonemap)

**Cohesion:** 0.16 - loosely connected
**Members:** 37 nodes

## Members
- [[.AssertVecClose()]] - code - RayTracer.Tests/GpuPhase5Tests.cs
- [[.ColorFromXyz()]] - code - RayTracer.Core/Gpu/Phase5Reference.cs
- [[.ColorFromXyz_MatchesResolveToSRGB()]] - code - RayTracer.Tests/GpuPhase5Tests.cs
- [[.Colorize()]] - code - RayTracer.Core/Gpu/Phase5Reference.cs
- [[.Colorize_RoutesEachModeToItsPalette()]] - code - RayTracer.Tests/GpuPhase5Tests.cs
- [[.LumaVariance()]] - code - RayTracer.Core/Gpu/Phase5Reference.cs
- [[.LumaVariance_IsZeroUntilSecondSampleThenM2OverNMinus1()]] - code - RayTracer.Tests/GpuPhase5Tests.cs
- [[.MultiStop()]] - code - RayTracer.Core/Gpu/Phase5Reference.cs
- [[.PaletteAlbedo()]] - code - RayTracer.Core/Gpu/Phase5Reference.cs
- [[.PaletteAlbedo_IsClampedGray()]] - code - RayTracer.Tests/GpuPhase5Tests.cs
- [[.PaletteClamp()]] - code - RayTracer.Core/Gpu/Phase5Reference.cs
- [[.PaletteClamp_ZeroIsBlackAndSaturatesToWhite()]] - code - RayTracer.Tests/GpuPhase5Tests.cs
- [[.PaletteDepth()]] - code - RayTracer.Core/Gpu/Phase5Reference.cs
- [[.PaletteDepth_NearIsDarkFarSaturatesToWhite()]] - code - RayTracer.Tests/GpuPhase5Tests.cs
- [[.PaletteHistoryWeight()]] - code - RayTracer.Core/Gpu/Phase5Reference.cs
- [[.PaletteHistoryWeight_SpansDarkToRed()]] - code - RayTracer.Tests/GpuPhase5Tests.cs
- [[.PaletteNormal()]] - code - RayTracer.Core/Gpu/Phase5Reference.cs
- [[.PaletteNormal_EncodesUnitVectorAndZeroesEmpty()]] - code - RayTracer.Tests/GpuPhase5Tests.cs
- [[.PaletteRejection()]] - code - RayTracer.Core/Gpu/Phase5Reference.cs
- [[.PaletteRejection_GreenReusedRedRejected()]] - code - RayTracer.Tests/GpuPhase5Tests.cs
- [[.PaletteSampleCount()]] - code - RayTracer.Core/Gpu/Phase5Reference.cs
- [[.PaletteSampleCount_SpansPurpleToWhite()]] - code - RayTracer.Tests/GpuPhase5Tests.cs
- [[.PaletteVariance()]] - code - RayTracer.Core/Gpu/Phase5Reference.cs
- [[.PaletteVariance_SpansDarkBlueToRed()]] - code - RayTracer.Tests/GpuPhase5Tests.cs
- [[.ParseMode_IsCaseInsensitiveWithBeautyFallback()]] - code - RayTracer.Tests/GpuPhase5Tests.cs
- [[.Reduce()]] - code - RayTracer.Core/Gpu/Phase5Reference.cs
- [[.Reduce_ComputesFrameAggregates()]] - code - RayTracer.Tests/GpuPhase5Tests.cs
- [[.Reduce_EmptyIsZero()]] - code - RayTracer.Tests/GpuPhase5Tests.cs
- [[.VarianceViewNorm_MatchesCpuLegendRange()]] - code - RayTracer.Tests/GpuPhase5Tests.cs
- [[GpuPhase5Tests]] - code - RayTracer.Tests/GpuPhase5Tests.cs
- [[Phase5Reference]] - code - RayTracer.Core/Gpu/Phase5Reference.cs
- [[Phase5Reference.cs]] - code - RayTracer.Core/Gpu/Phase5Reference.cs
- [[Phase5Stats]] - code - RayTracer.Core/Gpu/Phase5Reference.cs
- [[ReadOnlySpan_3]] - code
- [[TestMethod_31]] - code
- [[Vector3_24]] - code
- [[Vector3_74]] - code

## Live Query (requires Dataview plugin)

```dataview
TABLE source_file, type FROM #community/Phase5_Reference_Color/Tonemap
SORT file.name ASC
```

## Connections to other communities
- 4 edges to [[_COMMUNITY_Maze Modes & Volumetrics]]
- 2 edges to [[_COMMUNITY_RayTracer Test Suite]]
- 2 edges to [[_COMMUNITY_Community 70]]
- 2 edges to [[_COMMUNITY_Community 145]]
- 1 edge to [[_COMMUNITY_GPU Phase5 Renderer (D3D12)]]
- 1 edge to [[_COMMUNITY_DXR Acceleration Structures]]

## Top bridge nodes
- [[Phase5Reference]] - degree 16, connects to 2 communities
- [[Phase5Reference.cs]] - degree 4, connects to 2 communities
- [[Phase5Stats]] - degree 4, connects to 2 communities
- [[GpuPhase5Tests]] - degree 17, connects to 1 community
- [[.Colorize()]] - degree 13, connects to 1 community