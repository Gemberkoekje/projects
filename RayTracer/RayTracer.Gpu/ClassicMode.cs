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
