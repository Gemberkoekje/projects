namespace Vista.World;

/// <summary>
/// 2-D gradient (Perlin-style) noise as a pure function of <c>(seed, x, y)</c> (vista-plan §4.1): no
/// permutation table and no state, so any position evaluates the same whichever camera asks for it.
/// Only <c>+ − × ÷</c> and <see cref="Math.Floor(double)"/> are used — no transcendental functions, whose
/// last bits may differ between runtimes — so values are bit-reproducible across machines.
/// </summary>
public static class GradientNoise
{
    private const double C = 0.92387953251128675613; // cos 22.5°
    private const double S = 0.38268343236508977173; // sin 22.5°

    // Eight unit gradients at 22.5° + k·45°. None is axis-aligned: along a lattice line only one gradient
    // component contributes, and an axis-aligned gradient would contribute nothing there, flattening the
    // noise into a straight line that ridged terrain turns into a ruler-straight crest.
    private static readonly double[] Gx = [C, S, -S, -C, -C, -S, S, C];
    private static readonly double[] Gy = [S, C, C, S, -S, -C, -C, -S];

    /// <summary>Noise at <c>(x, y)</c> in lattice units, roughly in [−1, 1], zero at every lattice point.</summary>
    public static double Sample(ulong seed, double x, double y)
    {
        double fx = Math.Floor(x);
        double fy = Math.Floor(y);
        long ix = (long)fx;
        long iy = (long)fy;
        double dx = x - fx;
        double dy = y - fy;

        double n00 = Corner(seed, ix, iy, dx, dy);
        double n10 = Corner(seed, ix + 1, iy, dx - 1.0, dy);
        double n01 = Corner(seed, ix, iy + 1, dx, dy - 1.0);
        double n11 = Corner(seed, ix + 1, iy + 1, dx - 1.0, dy - 1.0);

        double u = Fade(dx);
        double v = Fade(dy);
        double nx0 = n00 + (n10 - n00) * u;
        double nx1 = n01 + (n11 - n01) * u;
        // Unit gradients bound 2-D Perlin noise by √2/2; scale by √2 to fill roughly [−1, 1].
        return (nx0 + (nx1 - nx0) * v) * 1.41421356237309504880;
    }

    private static double Corner(ulong seed, long ix, long iy, double dx, double dy)
    {
        int g = (int)(StableHash.Lattice(seed, ix, iy) >> 61); // top three bits → one of eight gradients
        return Gx[g] * dx + Gy[g] * dy;
    }

    // Perlin's quintic fade 6t⁵ − 15t⁴ + 10t³: C2-continuous, so terrain normals have no lattice creases.
    private static double Fade(double t) => t * t * t * (t * (t * 6.0 - 15.0) + 10.0);
}
