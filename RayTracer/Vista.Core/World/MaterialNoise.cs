namespace Vista.World;

/// <summary>
/// The noise the material rules break their boundaries up with (vista-plan §10 M1.12): 2-D gradient noise like
/// <see cref="GradientNoise"/>, but in single precision and hashed with the integrator's 32-bit PCG hash, so the shader
/// evaluates the very same function per hit that the mesh evaluates per vertex (<c>MaterialNoise</c> in
/// <c>VistaMaterials.hlsli</c>; <c>--gpu-selftest</c> checks the two agree). A pure function of the seed and the position.
/// </summary>
public static class MaterialNoise
{
    private const float C = 0.9238795f; // cos 22.5°
    private const float S = 0.3826834f; // sin 22.5°

    // GradientNoise's eight unit gradients, none along an axis.
    private static readonly float[] Gx = [C, S, -S, -C, -C, -S, S, C];
    private static readonly float[] Gy = [S, C, C, S, -S, -C, -C, -S];

    /// <summary>The integrator's PCG hash (<c>PcgHash</c> in <c>VistaIntegrator.hlsl</c>).</summary>
    public static uint Hash(uint v)
    {
        uint state = unchecked(v * 747796405u + 2891336453u);
        uint word = unchecked(((state >> (int)((state >> 28) + 4u)) ^ state) * 277803737u);
        return (word >> 22) ^ word;
    }

    /// <summary>The 32-bit seed of the material noise of a world whose seed is <paramref name="worldSeed"/>.</summary>
    public static uint SeedOf(ulong worldSeed) => (uint)(SeedDerivation.Derive(worldSeed, SeedDerivation.TerrainMaterials) >> 32);

    /// <summary>Noise at <c>(x, y)</c> in lattice units, roughly in [−1, 1], zero at every lattice point.</summary>
    public static float Sample(uint seed, float x, float y)
    {
        float fx = MathF.Floor(x), fy = MathF.Floor(y);
        int ix = (int)fx, iy = (int)fy;
        float dx = x - fx, dy = y - fy;
        float n00 = Corner(seed, ix, iy, dx, dy);
        float n10 = Corner(seed, ix + 1, iy, dx - 1f, dy);
        float n01 = Corner(seed, ix, iy + 1, dx, dy - 1f);
        float n11 = Corner(seed, ix + 1, iy + 1, dx - 1f, dy - 1f);
        float u = Fade(dx), v = Fade(dy);
        float nx0 = n00 + (n10 - n00) * u;
        float nx1 = n01 + (n11 - n01) * u;
        return (nx0 + (nx1 - nx0) * v) * 1.4142135f;
    }

    /// <summary>
    /// The two breakup values the rules read at world <c>(x, z)</c>, m: the noise at <paramref name="wavelength1"/> and at
    /// <paramref name="wavelength2"/>, each from its own stream.
    /// </summary>
    public static (float B1, float B2) Breakup(uint seed, float x, float z, float wavelength1, float wavelength2) =>
        (Sample(seed, x / wavelength1, z / wavelength1), Sample(Hash(seed ^ 0x9E3779B9u), x / wavelength2, z / wavelength2));

    private static float Corner(uint seed, int ix, int iy, float dx, float dy)
    {
        uint g = Hash(seed ^ Hash(unchecked((uint)ix) ^ Hash(unchecked((uint)iy)))) >> 29;
        return Gx[g] * dx + Gy[g] * dy;
    }

    private static float Fade(float t) => t * t * t * (t * (t * 6f - 15f) + 10f);
}
