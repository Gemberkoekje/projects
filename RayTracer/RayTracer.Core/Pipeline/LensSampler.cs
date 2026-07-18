using System;
using System.Numerics;

namespace RayTracer;

/// <summary>
/// Primary-ray generation through a physical lens (plan §4.1). Pure, allocation-free camera-space math:
/// callers supply the image-plane offsets and apply their own rotation/translation afterwards, so the CPU
/// tracer, the GPU C#-reference replica and the HLSL all share one contract.
///
/// <para><b>The pinhole path must stay byte-identical.</b> When <paramref name="apertureRadius"/> is 0 and the
/// lens has no distortion, <see cref="GenerateLocalRay"/> returns exactly the historical
/// <c>(px, py, imgPlaneZ)</c> at a zero origin and — critically — <b>draws no random numbers</b>, so the
/// wavelength/RNG stream downstream is untouched and every golden holds (working convention 2).</para>
///
/// <para><b>Un-normalised on purpose.</b> The returned direction is not normalised: the caller rotates it into
/// world space and normalises there, exactly as the historical ray-gen block did. Normalising here would
/// reorder the float ops and move the goldens.</para>
/// </summary>
public static class LensSampler
{
    // The engine's RNG step, matching TraceCore/SpecularRadiance so the streams stay comparable.
    private const uint RngMul = 747796405u;
    private const uint RngAdd = 2891336453u;

    private static float NextFloat(ref uint rng)
    {
        rng = rng * RngMul + RngAdd;
        return rng / 4294967296f;
    }

    /// <summary>
    /// The lens's RNG seed for one pixel/sample. The lens gets its <b>own</b> stream, distinct from the
    /// wavelength/specular/shadow seeds, so the aperture draw cannot perturb them — which is half of why the
    /// pinhole path stays byte-identical. Shared by the CPU tracer and the GPU reference so the two cannot
    /// drift apart; the HLSL ports this expression verbatim.
    /// </summary>
    public static uint SeedFor(uint pixelHash, long sampleIdx)
        => pixelHash + (uint)(sampleIdx * 2246822519u) + 3266489917u;

    /// <summary>
    /// Brown–Conrady radial distortion applied to an image-plane offset: <c>r' = r·(1 + k1·r² + k2·r⁴)</c>.
    /// Negative k1 bows straight lines outward (barrel — the old-camera look), positive pinches them in
    /// (pincushion). Applied to the ray direction rather than as a post-process, which is the advantage of a
    /// ray tracer: the primary ray genuinely leaves along the distorted angle, so it is correct under
    /// reflection and in the G-buffer instead of being a screen-space warp.
    /// </summary>
    public static void Distort(ref float px, ref float py, float k1, float k2)
    {
        float r2 = px * px + py * py;
        float scale = 1f + k1 * r2 + k2 * r2 * r2;
        px *= scale;
        py *= scale;
    }

    /// <summary>
    /// Samples a point on the aperture. A <paramref name="blades"/> count below 3 gives a perfect disc
    /// (uniform by the <c>r = R·√u</c> mapping); 3 or more samples the inscribed regular polygon via a
    /// triangle fan, so out-of-focus highlights take the iris's polygonal shape.
    /// <para>Always draws exactly two random numbers regardless of shape, so the stream length does not
    /// depend on the blade count.</para>
    /// </summary>
    public static void SampleAperture(
        float radius, int blades, float bladeRotation, ref uint rng, out float lx, out float ly)
    {
        float u1 = NextFloat(ref rng);
        float u2 = NextFloat(ref rng);

        if (blades < 3)
        {
            float r = radius * MathF.Sqrt(u1);
            float theta = 2f * MathF.PI * u2;
            lx = r * MathF.Cos(theta);
            ly = r * MathF.Sin(theta);
            return;
        }

        // Triangle fan over the N-gon: u1 picks the wedge and is rescaled to a barycentric coordinate, so
        // both randoms stay in use and the samples are area-uniform across the polygon.
        float fan = u1 * blades;
        int tri = (int)fan;
        if (tri >= blades)
            tri = blades - 1;
        float a = fan - tri;
        float b = u2;
        if (a + b > 1f)
        {
            a = 1f - a;
            b = 1f - b;
        }

        float step = 2f * MathF.PI / blades;
        float t0 = bladeRotation + tri * step;
        float t1 = t0 + step;
        float v0x = radius * MathF.Cos(t0), v0y = radius * MathF.Sin(t0);
        float v1x = radius * MathF.Cos(t1), v1y = radius * MathF.Sin(t1);
        lx = a * v0x + b * v1x;
        ly = a * v0y + b * v1y;
    }

    /// <summary>
    /// Squashes an aperture sample into the "cat's eye" a real lens barrel cuts off-axis (plan §4.4). The exact
    /// shape is the vesica formed by intersecting the aperture disc with the barrel's exit pupil offset along
    /// the field direction; sampling that exactly needs rejection, which would make the RNG stream
    /// pixel-dependent and unportable to HLSL. Instead this squashes the sample along the <i>radial</i>
    /// direction (leaving the tangential axis alone), which reproduces the characteristic eye pointing at the
    /// frame centre and its stronger squash toward the corners, at a fixed two randoms per sample.
    /// </summary>
    /// <param name="fieldRadius">The pixel's normalised distance from the frame centre (0 at the axis, 1 at the corner).</param>
    public static void ApplyCatEye(ref float lx, ref float ly, float fieldX, float fieldY, float fieldRadius)
    {
        // The field direction has to be normalised by its own length — fieldRadius is the *normalised*
        // off-axis distance that sets how hard the squash is, and is not this vector's magnitude.
        float fieldLen = MathF.Sqrt(fieldX * fieldX + fieldY * fieldY);
        if (fieldLen <= 1e-6f || fieldRadius <= 1e-6f)
            return;

        float ux = fieldX / fieldLen;
        float uy = fieldY / fieldLen;
        float squash = MathF.Max(0.25f, 1f - 0.75f * MathF.Min(fieldRadius, 1f));

        // Split into radial + tangential and shrink only the radial part.
        float radial = lx * ux + ly * uy;
        float tanX = lx - radial * ux;
        float tanY = ly - radial * uy;
        radial *= squash;
        lx = tanX + radial * ux;
        ly = tanY + radial * uy;
    }

    /// <summary>
    /// The lens's illumination falloff toward the corners, as a multiplier on the sample's radiance.
    /// <paramref name="strength"/> is an exponent on the physical <c>cos⁴θ</c> natural falloff: <c>0</c> is off
    /// (the historical flat response), <c>1</c> is physically correct, and above 1 exaggerates it the way a
    /// cheap lens with a deep barrel does. Returns exactly 1 when strength is 0.
    /// </summary>
    public static float VignetteFactor(float px, float py, float imgPlaneZ, float strength)
    {
        if (strength <= 0f)
            return 1f;
        float z2 = imgPlaneZ * imgPlaneZ;
        float cos2 = z2 / (px * px + py * py + z2);   // cos²θ off the optical axis
        float cos4 = cos2 * cos2;
        return MathF.Abs(strength - 1f) < 1e-6f ? cos4 : MathF.Pow(cos4, strength);
    }

    /// <summary>
    /// Builds the camera-space primary ray for an image-plane offset. <paramref name="apertureRadius"/> comes
    /// from <see cref="LensOptions.ApertureRadiusWorld"/>; at 0 this is the historical pinhole and no randoms
    /// are drawn.
    /// </summary>
    /// <param name="px">Horizontal image-plane offset (NDC × aspect × tan(fov/2)).</param>
    /// <param name="py">Vertical image-plane offset (NDC × tan(fov/2)).</param>
    /// <param name="localOrigin">Camera-space ray origin — the sampled point on the aperture, or the exact
    /// zero vector for a pinhole.</param>
    /// <param name="localDir">Camera-space, <b>un-normalised</b> ray direction (see the type remarks).</param>
    public static void GenerateLocalRay(
        float px, float py, float imgPlaneZ,
        in LensOptions lens, float apertureRadius,
        ref uint rng,
        out Vector3 localOrigin, out Vector3 localDir)
    {
        if (lens.HasDistortion)
            Distort(ref px, ref py, lens.DistortionK1, lens.DistortionK2);

        localDir = new Vector3(px, py, imgPlaneZ);

        if (apertureRadius <= 0f)
        {
            // Pinhole: identical to the historical block, and no RNG consumed.
            localOrigin = Vector3.Zero;
            return;
        }

        SampleAperture(apertureRadius, lens.ApertureBlades, lens.BladeRotation, ref rng, out float lx, out float ly);

        if (lens.CatEyeVignette)
        {
            float fieldRadius = MathF.Sqrt(px * px + py * py) / MathF.Max(MathF.Abs(imgPlaneZ), 1e-6f);
            ApplyCatEye(ref lx, ref ly, px, py, fieldRadius);
        }

        // Thin lens: the point this pixel's chief ray crosses the focus plane stays sharp for every point on
        // the aperture. Scaling by focusDistance/|imgPlaneZ| puts the focus plane FocusDistance metres along
        // the viewing axis, whichever way the axis points.
        float ft = lens.FocusDistance / MathF.Max(MathF.Abs(imgPlaneZ), 1e-6f);
        Vector3 focusPoint = localDir * ft;

        localOrigin = new Vector3(lx, ly, 0f);
        localDir = focusPoint - localOrigin;
    }
}
