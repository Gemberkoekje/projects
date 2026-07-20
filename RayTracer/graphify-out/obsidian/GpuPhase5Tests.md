---
source_file: "RayTracer.Tests/GpuPhase5Tests.cs"
type: "code"
community: "Phase5 Reference (Color/Tonemap)"
location: "L17"
tags:
  - graphify/code
  - graphify/EXTRACTED
  - community/Phase5_Reference_Color/Tonemap
---

# GpuPhase5Tests

## Connections
- [[.AssertVecClose()]] - `method` [EXTRACTED]
- [[.ColorFromXyz_MatchesResolveToSRGB()]] - `method` [EXTRACTED]
- [[.Colorize_RoutesEachModeToItsPalette()]] - `method` [EXTRACTED]
- [[.LumaVariance_IsZeroUntilSecondSampleThenM2OverNMinus1()]] - `method` [EXTRACTED]
- [[.PaletteAlbedo_IsClampedGray()]] - `method` [EXTRACTED]
- [[.PaletteClamp_ZeroIsBlackAndSaturatesToWhite()]] - `method` [EXTRACTED]
- [[.PaletteDepth_NearIsDarkFarSaturatesToWhite()]] - `method` [EXTRACTED]
- [[.PaletteHistoryWeight_SpansDarkToRed()]] - `method` [EXTRACTED]
- [[.PaletteNormal_EncodesUnitVectorAndZeroesEmpty()]] - `method` [EXTRACTED]
- [[.PaletteRejection_GreenReusedRedRejected()]] - `method` [EXTRACTED]
- [[.PaletteSampleCount_SpansPurpleToWhite()]] - `method` [EXTRACTED]
- [[.PaletteVariance_SpansDarkBlueToRed()]] - `method` [EXTRACTED]
- [[.ParseMode_IsCaseInsensitiveWithBeautyFallback()]] - `method` [EXTRACTED]
- [[.Reduce_ComputesFrameAggregates()]] - `method` [EXTRACTED]
- [[.Reduce_EmptyIsZero()]] - `method` [EXTRACTED]
- [[.VarianceViewNorm_MatchesCpuLegendRange()]] - `method` [EXTRACTED]
- [[GpuPhase5Tests.cs]] - `contains` [EXTRACTED]

#graphify/code #graphify/EXTRACTED #community/Phase5_Reference_Color/Tonemap