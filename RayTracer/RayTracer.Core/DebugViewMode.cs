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
}
