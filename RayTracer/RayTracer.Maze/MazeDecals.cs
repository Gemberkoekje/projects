namespace RayTracer.Gpu;

/// <summary>
/// The maze app's decal atlas builder, passed to every <see cref="Phase6Renderer"/> as its
/// required <c>decalAtlas</c> constructor argument. Kept in one place so every render path feeds
/// the backend the identical prop art (pinball-plan §7.2) — the backend stays free of maze content
/// and there is no process-global mutable state to set in the right order. Deterministic, so the
/// golden regression images stay bit-identical.
/// </summary>
internal static class MazeDecals
{
    /// <summary>
    /// Builds the classic prop atlas (GL logo, wall signs, rat billboard) at the given layer size.
    /// </summary>
    internal static readonly Func<int, float[]> Atlas = static size => PropTextures.Build(size).Pixels;
}
