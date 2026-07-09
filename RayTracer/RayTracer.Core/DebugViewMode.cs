namespace RayTracer;

public enum DebugViewMode
{
    Beauty,
    SampleCount,
    Variance,
    HistoryWeight,
    HistoryAge,
    RejectionMask,
    ClampHeatmap,
    Depth,
    Albedo,
    Normal,
    DirectLighting,
    IndirectLighting,
    EmissiveLighting,
    CurrentVsAccumDiff,
    UnfilteredVsFilteredDiff,
    ReprojectedVsCurrentDiff
    ,DirectVariance
    ,IndirectVariance
    ,VarianceSplit
    ,Bounce0
    ,Bounce1
    ,Bounce2Plus
    ,BounceRGB
    ,EdgeDisagreement
    // Added after the original (a09bb7d6 "monitor"): DebugBufferRenderer's
    // diffuse-cache heatmap references this member.
    ,DiffuseCache
}
