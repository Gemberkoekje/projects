using RayTracer;

namespace RayTracer.Gpu;

/// <summary>
/// Shared knobs for the <see cref="RenderStyle.Classic"/> look — the recreation of the
/// original Windows <i>3D Maze</i> screensaver. Classic renders the same spectral tracer
/// <b>unlit</b> (fullbright albedo via <c>LightingMode.None</c>, volumetrics off), which
/// is the spectral equivalent of the original's flat texture-mapped corridors; the only
/// other departure is a wider field of view to match the original's feel.
/// </summary>
internal static class ClassicMode
{
    /// <summary>Field of view for the classic look (~90°, wider than the Enhanced 60°).</summary>
    internal const float Fov = MathF.PI * 0.5f;

    /// <summary>
    /// Depth-cue strength (plan §1.4/§2.3): the exponential display-space darkening rate applied
    /// per world unit of primary-hit distance when the opt-in Classic depth cue is on. Tuned on
    /// the RTX 3070 (0.15) so corridors fade convincingly with depth without crushing the far end
    /// to black. A deliberately non-physical tone curve, so it lives in the resolve pass and only
    /// applies in unlit Classic mode.
    /// </summary>
    internal const float DepthCueStrength = 0.15f;

    /// <summary>
    /// Target effective vertical resolution for the §1.5 retro pixelation look. The pixel-block
    /// edge is chosen so the chunkiness stays roughly constant regardless of the window height
    /// (a fixed pixel block would look coarser on small windows and finer on large ones).
    /// </summary>
    internal const int RetroTargetLines = 240;

    /// <summary>Retro pixelation block edge (in pixels) for a given render height; ≥ 2 when on.</summary>
    internal static int RetroBlockFor(int height) =>
        Math.Max(2, (int)MathF.Round(height / (float)RetroTargetLines));

    /// <summary>Returns <paramref name="c"/> with the classic wide field of view.</summary>
    internal static Camera WithClassicFov(Camera c) => new()
    {
        Position = c.Position,
        Rotation = c.Rotation,
        Fov = Fov,
        Aspect = c.Aspect,
        ImgPlaneZ = c.ImgPlaneZ,
    };
}
