namespace RayTracer;

/// <summary>
/// Engine-owned world-space scale for the volumetric biome-density grid
/// (<see cref="SmokeMode.Biome"/>). The integrator must not reach into scene/content
/// classes for world scale, so this constant replaces the former
/// <c>MazeGeometryBuilder.CellSize</c> reference inside <see cref="JobSystem"/>'s
/// <c>GetDensityBiome</c> and <see cref="Phase4Reference"/> — the first of the three
/// <c>RayTracer.Core</c> maze leaks (pinball-plan §7.1, Milestone E).
///
/// <para><b>Byte-identical.</b> The value is deliberately identical to the maze's
/// historical cell size (2 m), so the no-effect render path stays bit-for-bit
/// unchanged: every golden that depended on <c>CellSize * biomeSizeCells</c> now reads
/// <c>WorldUnitsPerCell * biomeSizeCells</c> with the same operands and the same result.</para>
///
/// <para><b>Constant today, promotable later.</b> When an app needs a per-scene world
/// scale this can be promoted to a runtime parameter (a field on
/// <see cref="VolumetricOptions"/> / <see cref="RealismOptions"/>) threaded through the
/// integrator — the plan sanctions either shape. Pinball, whose playfield is authored
/// directly in metres, simply keeps this default.</para>
/// </summary>
public static class SceneScale
{
    /// <summary>
    /// World units (metres) spanned by one biome-density grid cell. 1 world unit = 1 metre.
    /// </summary>
    public const float WorldUnitsPerCell = 2f;
}
