using System;
using System.Numerics;

namespace RayTracer;

/// <summary>
/// A CPU replica of the Phase 3 (temporal pipeline) GPU resolve pass: the
/// bilateral spatial filter, previous-frame reprojection, bilinear history
/// fetch with validity weighting, disocclusion / normal rejection, and the
/// neighborhood-clamped temporal blend. <c>ResolvePhase3.hlsl</c> is a
/// line-for-line port of the methods here.
///
/// The unit tests pin this replica to the <b>actual</b> CPU renderer's
/// <c>JobSystem.TaaResolver</c> / <c>JobSystem.ResolveFilteredXYZ</c> output
/// over real per-pixel accumulation and G-buffer state, so shader/CPU temporal
/// parity is testable in plain C# where the GPU cannot run in CI.
///
/// Every method mirrors its CPU counterpart bit-for-bit and uses the same
/// <see cref="System.Numerics"/> primitives (<see cref="Vector3.Transform(Vector3, Quaternion)"/>,
/// <see cref="Quaternion.Inverse"/>) so the C# side matches by construction; the
/// HLSL port is what the dev-box self-test validates on hardware.
/// </summary>
public static class Phase3Reference
{
    /// <summary>Edge-preserving sharpness of the bilateral filter
    /// (mirrors <c>JobSystem.BilateralSharpness</c>).</summary>
    public const float BilateralSharpness = 25f;

    /// <summary>Minimum bilinear validity weight for history to be considered
    /// (mirrors the <c>validWeight &gt; 0.25f</c> gate in <c>TaaResolver</c>).</summary>
    public const float MinValidWeight = 0.25f;

    /// <summary>Reprojection error threshold (squared world distance) when the
    /// camera is moving; looser when stationary (mirrors <c>TaaResolver</c>).</summary>
    public const float ReprojThresholdMoving = 0.0625f;
    public const float ReprojThresholdStill = 0.25f;

    /// <summary>Minimum normal agreement (cosine) for history to be accepted
    /// (mirrors the <c>ndot &lt; 0.90f</c> rejection in <c>TaaResolver</c>).</summary>
    public const float NormalRejectCos = 0.90f;

    /// <summary>
    /// The bilateral (or box) spatial filter applied to the accumulation buffer,
    /// mirroring <c>JobSystem.ResolveFilteredXYZ</c>. <paramref name="radius"/> is
    /// the <b>effective</b> radius the CPU uses (<c>IsMoving ? FilterRadius : 0</c>);
    /// a radius of 0 returns the unfiltered accumulated mean.
    /// </summary>
    public static Vector3 FilteredXYZ(
        ReadOnlySpan<Vector3> accum, int width, int height, int x, int y, int radius, bool edgeAware)
    {
        if (radius <= 0)
            return accum[y * width + x];

        int yMin = Math.Max(y - radius, 0);
        int yMax = Math.Min(y + radius, height - 1);
        int xMin = Math.Max(x - radius, 0);
        int xMax = Math.Min(x + radius, width - 1);

        if (edgeAware)
        {
            Vector3 centerXyz = accum[y * width + x];
            Vector3 xyz = Vector3.Zero;
            float totalWeight = 0f;
            for (int ny = yMin; ny <= yMax; ny++)
            {
                int rowOff = ny * width;
                for (int nx = xMin; nx <= xMax; nx++)
                {
                    Vector3 neighbor = accum[rowOff + nx];
                    float lumDiff = centerXyz.Y - neighbor.Y;
                    float w = MathF.Exp(-lumDiff * lumDiff * BilateralSharpness);
                    xyz += neighbor * w;
                    totalWeight += w;
                }
            }

            return totalWeight > 0f ? xyz / totalWeight : centerXyz;
        }

        Vector3 sum = Vector3.Zero;
        int count = 0;
        for (int ny = yMin; ny <= yMax; ny++)
        {
            int rowOff = ny * width;
            for (int nx = xMin; nx <= xMax; nx++)
            {
                sum += accum[rowOff + nx];
                count++;
            }
        }

        return sum / count;
    }

    /// <summary>
    /// Projects a world-space hit point into the previous frame's pixel grid,
    /// mirroring <c>TaaResolver.TryProjectToPrevPixel</c>. Returns false (and
    /// <paramref name="px"/>/<paramref name="py"/> = 0) when the point is behind
    /// the previous camera or falls outside the frame.
    /// </summary>
    public static bool TryProjectToPrevPixel(
        Vector3 worldPoint, Vector3 prevCamPos, Quaternion prevCamRot,
        float tanHalfFov, float aspectTanHalfFov, int width, int height,
        out float px, out float py)
    {
        Quaternion invPrevRot = Quaternion.Inverse(prevCamRot);
        Vector3 local = Vector3.Transform(worldPoint - prevCamPos, invPrevRot);
        if (local.Z <= 1e-4f)
        {
            px = py = 0f;
            return false;
        }

        float ndcX = local.X / (local.Z * aspectTanHalfFov);
        float ndcY = local.Y / (local.Z * tanHalfFov);

        float fx = ((ndcX + 1f) * 0.5f) * width - 0.5f;
        float fy = ((1f - ndcY) * 0.5f) * height - 0.5f;

        px = fx;
        py = fy;
        return px >= 0f && px < width && py >= 0f && py < height;
    }

    /// <summary>
    /// Resolves one pixel of the temporal pipeline: spatial filter, reproject,
    /// bilinear history fetch, disocclusion/normal rejection, neighborhood clamp,
    /// and temporal blend. Mirrors the per-pixel body of
    /// <c>TaaResolver.ResolveDisplayBufferWithTaa</c> and returns the resolved
    /// XYZ that the CPU writes to its TAA-next buffer.
    /// </summary>
    /// <param name="useTaa">The CPU's <c>EnableTaa &amp;&amp; TemporalBlendAlpha &gt; 0 &amp;&amp; hasPrevCamera</c>.</param>
    /// <param name="isMoving">Whether the camera moved this frame (enables the
    /// spatial filter, the ×10 alpha boost, and the tighter reproj threshold).</param>
    public static Vector3 ResolvePixel(
        ReadOnlySpan<Vector3> accum,
        ReadOnlySpan<Vector3> hitPoint,
        ReadOnlySpan<Vector3> normal,
        ReadOnlySpan<bool> lastHit,
        int width, int height, int x, int y,
        ReadOnlySpan<Vector3> historyXyz,
        ReadOnlySpan<Vector3> historyHitPoint,
        ReadOnlySpan<bool> historyValid,
        Vector3 prevCamPos, Quaternion prevCamRot,
        float tanHalfFov, float aspectTanHalfFov,
        bool useTaa, bool isMoving, int filterRadius, bool edgeAware, float temporalBlendAlpha,
        out float historyWeight, out bool rejected)
    {
        int radius = isMoving ? filterRadius : 0;
        int ix = y * width + x;

        float alpha = Math.Clamp(temporalBlendAlpha, 0.01f, 1f);
        if (isMoving)
            alpha = Math.Clamp(alpha * 10f, 0.01f, 1f);

        Vector3 current = FilteredXYZ(accum, width, height, x, y, radius, edgeAware);
        Vector3 resolved = current;
        historyWeight = 0f;
        rejected = false;

        bool currentHit = lastHit[ix];
        Vector3 currentHitPoint = hitPoint[ix];

        if (useTaa && currentHit &&
            TryProjectToPrevPixel(currentHitPoint, prevCamPos, prevCamRot,
                tanHalfFov, aspectTanHalfFov, width, height, out float pxf, out float pyf))
        {
            int ix0 = (int)MathF.Floor(pxf);
            int iy0 = (int)MathF.Floor(pyf);
            float wx = pxf - ix0;
            float wy = pyf - iy0;

            int x0 = Math.Clamp(ix0, 0, width - 1);
            int y0 = Math.Clamp(iy0, 0, height - 1);
            int x1 = Math.Clamp(ix0 + 1, 0, width - 1);
            int y1 = Math.Clamp(iy0 + 1, 0, height - 1);

            int i00 = y0 * width + x0;
            int i10 = y0 * width + x1;
            int i01 = y1 * width + x0;
            int i11 = y1 * width + x1;

            float w00 = (1f - wx) * (1f - wy);
            float w10 = wx * (1f - wy);
            float w01 = (1f - wx) * wy;
            float w11 = wx * wy;

            float validWeight =
                (historyValid[i00] ? w00 : 0f) + (historyValid[i10] ? w10 : 0f) +
                (historyValid[i01] ? w01 : 0f) + (historyValid[i11] ? w11 : 0f);

            if (validWeight > MinValidWeight)
            {
                Vector3 history = historyXyz[i00] * w00 + historyXyz[i10] * w10 +
                                  historyXyz[i01] * w01 + historyXyz[i11] * w11;
                Vector3 historyHit = historyHitPoint[i00] * w00 + historyHitPoint[i10] * w10 +
                                     historyHitPoint[i01] * w01 + historyHitPoint[i11] * w11;

                float reprojErr = Vector3.DistanceSquared(historyHit, currentHitPoint);
                float reprojThreshold = isMoving ? ReprojThresholdMoving : ReprojThresholdStill;
                bool accept = reprojErr < reprojThreshold;

                Vector3 histNormal = normal[i00] * w00 + normal[i10] * w10 +
                                     normal[i01] * w01 + normal[i11] * w11;
                Vector3 currNormal = normal[ix];
                if (accept && histNormal != Vector3.Zero && currNormal != Vector3.Zero)
                {
                    float ndot = Math.Clamp(
                        Vector3.Dot(Vector3.Normalize(histNormal), Vector3.Normalize(currNormal)), -1f, 1f);
                    if (ndot < NormalRejectCos)
                        accept = false;
                }

                if (accept)
                {
                    ComputeNeighborhoodMinMax(accum, width, height, x, y, radius, edgeAware,
                        out Vector3 nMin, out Vector3 nMax);
                    history = Vector3.Clamp(history, nMin, nMax);
                    resolved = Vector3.Lerp(history, current, alpha);
                    historyWeight = 1f - alpha;
                }
                else
                {
                    rejected = true;
                }
            }
            else
            {
                rejected = true;
            }
        }
        else if (useTaa && currentHit)
        {
            rejected = true;
        }

        return resolved;
    }

    /// <summary>3×3 min/max of the spatially filtered accumulation, mirroring
    /// <c>TaaResolver.ComputeNeighborhoodMinMax</c>.</summary>
    private static void ComputeNeighborhoodMinMax(
        ReadOnlySpan<Vector3> accum, int width, int height, int x, int y, int radius, bool edgeAware,
        out Vector3 nMin, out Vector3 nMax)
    {
        nMin = new Vector3(float.MaxValue);
        nMax = new Vector3(float.MinValue);
        int yMin = Math.Max(y - 1, 0);
        int yMax = Math.Min(y + 1, height - 1);
        int xMin = Math.Max(x - 1, 0);
        int xMax = Math.Min(x + 1, width - 1);
        for (int ny = yMin; ny <= yMax; ny++)
        {
            for (int nx = xMin; nx <= xMax; nx++)
            {
                Vector3 v = FilteredXYZ(accum, width, height, nx, ny, radius, edgeAware);
                nMin = Vector3.Min(nMin, v);
                nMax = Vector3.Max(nMax, v);
            }
        }
    }
}
