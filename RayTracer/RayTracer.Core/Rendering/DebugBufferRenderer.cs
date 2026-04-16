using System.Numerics;
using System.Runtime.CompilerServices;

namespace RayTracer;

public partial class JobSystem
{
    private sealed class DebugBufferRenderer(JobSystem owner)
    {
        private readonly JobSystem _owner = owner;

        public string GetDebugLegend(DebugViewMode mode) => mode switch
        {
            DebugViewMode.Beauty => "Debug: Beauty | Range: scene-referred color",
            DebugViewMode.SampleCount => $"Debug: Effective Sample Count | Range: 0 - {Math.Max(1u, _owner.MaxObservedSampleCount)}",
            DebugViewMode.Variance => $"Debug: Variance Heatmap | Range: 0.000 - {Math.Max(0.0001, _owner.AverageVariance * 8):0.000}",
            DebugViewMode.HistoryWeight => "Debug: History Weight | 0 = current only, 1 = history dominated",
            DebugViewMode.RejectionMask => "Debug: Rejection Mask | Green = reused, Red = rejected",
            DebugViewMode.ClampHeatmap => $"Debug: Clamp Heatmap | Active: {_owner.ClampedPixelPercent:0.0}%",
            DebugViewMode.Depth => "Debug: Depth | Dark = near, bright = far",
            DebugViewMode.Albedo => "Debug: Albedo | Dark = low reflectance, bright = high reflectance",
            DebugViewMode.Normal => "Debug: Normal | RGB = world normal",
            DebugViewMode.DirectLighting => "Debug: Direct lighting only",
            DebugViewMode.IndirectLighting => "Debug: Indirect lighting only (currently zero in this tracer)",
            DebugViewMode.EmissiveLighting => "Debug: Emissive lighting only (currently zero in this tracer)",
            DebugViewMode.CurrentVsAccumDiff => "Debug: abs(current - accumulated)",
            DebugViewMode.UnfilteredVsFilteredDiff => "Debug: abs(unfiltered - filtered)",
            DebugViewMode.ReprojectedVsCurrentDiff => "Debug: abs(reprojected_history - current)",
            DebugViewMode.HistoryAge => "Debug: History Age | Blue=fresh, Red=stale",
            _ => "Debug: Unknown"
        };

        public void RenderDebugModeToBuffer(DebugViewMode mode, byte[] targetBuffer, int targetStride)
        {
            for (int y = 0; y < _owner.Height; y++)
            {
                int row = y * _owner.Width;
                for (int x = 0; x < _owner.Width; x++)
                {
                    int ix = row + x;
                    float edgeDisagreement = 0f;
                    if (mode == DebugViewMode.EdgeDisagreement)
                    {
                        float colorDiff = MathF.Max(_owner.DiffCurrentVsAccum[ix], MathF.Max(_owner.DiffUnfilteredVsFiltered[ix], _owner.DiffReprojectedVsCurrent[ix]));

                        float depth = _owner.DepthDistance[ix];
                        float maxDepthDelta = 0f;
                        if (x > 0) maxDepthDelta = MathF.Max(maxDepthDelta, MathF.Abs(depth - _owner.DepthDistance[row + x - 1]));
                        if (x + 1 < _owner.Width) maxDepthDelta = MathF.Max(maxDepthDelta, MathF.Abs(depth - _owner.DepthDistance[row + x + 1]));
                        if (y > 0) maxDepthDelta = MathF.Max(maxDepthDelta, MathF.Abs(depth - _owner.DepthDistance[(y - 1) * _owner.Width + x]));
                        if (y + 1 < _owner.Height) maxDepthDelta = MathF.Max(maxDepthDelta, MathF.Abs(depth - _owner.DepthDistance[(y + 1) * _owner.Width + x]));
                        float depthDiff = maxDepthDelta * 0.05f;

                        Vector3 n = _owner.NormalWorld[ix];
                        float maxNormalDiff = 0f;
                        if (n != Vector3.Zero)
                        {
                            if (x > 0)
                            {
                                var nn = _owner.NormalWorld[row + x - 1];
                                if (nn != Vector3.Zero)
                            maxNormalDiff = MathF.Max(maxNormalDiff, 1f - Math.Clamp(Vector3.Dot(Vector3.Normalize(n), Vector3.Normalize(nn)), -1f, 1f));
                            }
                            if (x + 1 < _owner.Width)
                            {
                                var nn = _owner.NormalWorld[row + x + 1];
                                if (nn != Vector3.Zero)
                                    maxNormalDiff = MathF.Max(maxNormalDiff, 1f - Math.Clamp(Vector3.Dot(Vector3.Normalize(n), Vector3.Normalize(nn)), -1f, 1f));
                            }
                            if (y > 0)
                            {
                                var nn = _owner.NormalWorld[(y - 1) * _owner.Width + x];
                                if (nn != Vector3.Zero)
                                    maxNormalDiff = MathF.Max(maxNormalDiff, 1f - Math.Clamp(Vector3.Dot(Vector3.Normalize(n), Vector3.Normalize(nn)), -1f, 1f));
                            }
                            if (y + 1 < _owner.Height)
                            {
                                var nn = _owner.NormalWorld[(y + 1) * _owner.Width + x];
                                if (nn != Vector3.Zero)
                                    maxNormalDiff = MathF.Max(maxNormalDiff, 1f - Math.Clamp(Vector3.Dot(Vector3.Normalize(n), Vector3.Normalize(nn)), -1f, 1f));
                            }
                        }

                        edgeDisagreement = MathF.Max(colorDiff, MathF.Max(maxNormalDiff * 0.9f, depthDiff * 0.6f));
                    }
                    Vector3 rgb = mode switch
                    {
                        DebugViewMode.Beauty => ColorFromXyz(_owner.ResolveFilteredXYZ(y, x)),
                        DebugViewMode.SampleCount => PaletteSampleCount(_owner.SampleCount[ix], Math.Max(1u, _owner.MaxObservedSampleCount)),
                        DebugViewMode.Variance => PaletteVariance(_owner.LumaVariance[ix], (float)Math.Max(0.0001, _owner.AverageVariance * 8)),
                        DebugViewMode.HistoryWeight => PaletteHistoryWeight(_owner.HistoryWeight[ix]),
                        DebugViewMode.RejectionMask => _owner.HistoryRejected[ix] > 0 ? new Vector3(1f, 0.1f, 0.1f) : new Vector3(0.1f, 0.9f, 0.1f),
                        DebugViewMode.ClampHeatmap => PaletteClamp(_owner.ClampAmount[ix]),
                        DebugViewMode.Depth => PaletteDepth(_owner.DepthDistance[ix]),
                        DebugViewMode.HistoryAge =>
                            PaletteDifference(MathF.Min(1f, (_owner.FrameIndex - _owner.LastUpdatedFrame[ix]) / 120f)),
                        DebugViewMode.Albedo => PaletteAlbedo(_owner.AlbedoScalar[ix]),
                        DebugViewMode.Normal => PaletteNormal(_owner.NormalWorld[ix]),
                        DebugViewMode.DirectLighting => ColorFromXyz(_owner.DirectLightingXYZ[ix]),
                        DebugViewMode.IndirectLighting => ColorFromXyz(_owner.IndirectLightingXYZ[ix]),
                        DebugViewMode.EmissiveLighting => ColorFromXyz(_owner.EmissiveLightingXYZ[ix]),
                        DebugViewMode.CurrentVsAccumDiff => PaletteDifference(_owner.DiffCurrentVsAccum[ix]),
                        DebugViewMode.DirectVariance => PaletteVariance(_owner.LumaDirectVariance[ix], (float)Math.Max(0.0001, _owner.AverageVariance * 8)),
                        DebugViewMode.IndirectVariance => PaletteVariance(_owner.LumaIndirectVariance[ix], (float)Math.Max(0.0001, _owner.AverageVariance * 8)),
                        DebugViewMode.VarianceSplit => new Vector3(
                        Math.Clamp(_owner.LumaIndirectVariance[ix] / (float)Math.Max(1e-6, _owner.AverageVariance * 8), 0f, 1f),
                        Math.Clamp(_owner.LumaDirectVariance[ix] / (float)Math.Max(1e-6, _owner.AverageVariance * 8), 0f, 1f),
                        Math.Clamp(_owner.LumaVariance[ix] / (float)Math.Max(1e-6, _owner.AverageVariance * 8), 0f, 1f)),
                        DebugViewMode.UnfilteredVsFilteredDiff => PaletteDifference(_owner.DiffUnfilteredVsFiltered[ix]),
                        DebugViewMode.ReprojectedVsCurrentDiff => PaletteDifference(_owner.DiffReprojectedVsCurrent[ix]),
                        DebugViewMode.Bounce0 => ColorFromXyz(_owner.Bounce0XYZ[ix]),
                        DebugViewMode.Bounce1 => ColorFromXyz(_owner.Bounce1XYZ[ix]),
                        DebugViewMode.Bounce2Plus => ColorFromXyz(_owner.Bounce2PlusXYZ[ix]),
                        DebugViewMode.BounceRGB => new Vector3(
                            Math.Clamp(_owner.Bounce2PlusXYZ[ix].Y / (float)Math.Max(1e-6, _owner.AverageVariance * 8), 0f, 1f),
                            Math.Clamp(_owner.Bounce1XYZ[ix].Y / (float)Math.Max(1e-6, _owner.AverageVariance * 8), 0f, 1f),
                            Math.Clamp(_owner.Bounce0XYZ[ix].Y / (float)Math.Max(1e-6, _owner.AverageVariance * 8), 0f, 1f)
                        ),
                        DebugViewMode.EdgeDisagreement => PaletteDifference(edgeDisagreement),
                        _ => Vector3.Zero
                    };

                    int i = y * targetStride + x * 4;
                    targetBuffer[i + 0] = (byte)Math.Clamp(rgb.Z * 255f, 0, 255);
                    targetBuffer[i + 1] = (byte)Math.Clamp(rgb.Y * 255f, 0, 255);
                    targetBuffer[i + 2] = (byte)Math.Clamp(rgb.X * 255f, 0, 255);
                    targetBuffer[i + 3] = 255;
                }
            }
        }

        public PixelDebugInfo GetPixelDebugInfo(int x, int y)
        {
            if (x < 0 || x >= _owner.Width || y < 0 || y >= _owner.Height)
                return default;

            int ix = y * _owner.Width + x;
            Vector3 filtered = _owner.ResolveFilteredXYZ(y, x);
            return new PixelDebugInfo(
                _owner.AccumXYZ[ix],
                filtered,
                _owner.DiffCurrentVsAccum[ix],
                _owner.ClampAmount[ix],
                _owner.ClampHitFrame[ix],
                _owner.SampleCount[ix],
                _owner.HistoryWeight[ix],
                _owner.HistoryRejected[ix],
                _owner.DepthDistance[ix],
                _owner.AlbedoScalar[ix],
                _owner.LastUpdatedFrame[ix],
                _owner.NormalWorld[ix],
                _owner.DirectLightingXYZ[ix],
                _owner.IndirectLightingXYZ[ix],
                _owner.DiffUnfilteredVsFiltered[ix],
                _owner.DiffReprojectedVsCurrent[ix],
                _owner.Bounce0XYZ[ix],
                _owner.Bounce1XYZ[ix],
                _owner.Bounce2PlusXYZ[ix]);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector3 ColorFromXyz(Vector3 xyz)
        {
            var linearColor = ToSRGB(xyz);
            return new Vector3(
                LinearToSRGBStatic(linearColor.X),
                LinearToSRGBStatic(linearColor.Y),
                LinearToSRGBStatic(linearColor.Z));
        }

        private const float InvGamma = 1.0f / 2.4f;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float LinearToSRGBStatic(float linear)
        {
            if (linear <= 0.0031308f)
                return 12.92f * linear;
            return 1.055f * MathF.Pow(linear, InvGamma) - 0.055f;
        }

        private static Vector3 PaletteSampleCount(uint sampleCount, uint maxSampleCount)
        {
            float t = maxSampleCount > 0 ? Math.Clamp(sampleCount / (float)maxSampleCount, 0f, 1f) : 0f;
            return MultiStop(t,
                new Vector3(0.20f, 0.00f, 0.35f),
                new Vector3(0.00f, 0.70f, 1.00f),
                new Vector3(0.10f, 0.90f, 0.20f),
                new Vector3(1.00f, 0.95f, 0.20f),
                new Vector3(1.00f, 1.00f, 1.00f));
        }

        private static Vector3 PaletteVariance(float variance, float maxVariance)
        {
            float t = maxVariance > 0f ? Math.Clamp(variance / maxVariance, 0f, 1f) : 0f;
            return MultiStop(t,
                new Vector3(0.00f, 0.00f, 0.20f),
                new Vector3(0.00f, 0.20f, 0.90f),
                new Vector3(0.00f, 0.80f, 0.30f),
                new Vector3(1.00f, 0.90f, 0.10f),
                new Vector3(1.00f, 0.20f, 0.10f));
        }

        private static Vector3 PaletteHistoryWeight(float weight)
        {
            float t = Math.Clamp(weight, 0f, 1f);
            return MultiStop(t,
                new Vector3(0.10f, 0.10f, 0.20f),
                new Vector3(0.20f, 0.80f, 1.00f),
                new Vector3(1.00f, 0.95f, 0.20f),
                new Vector3(1.00f, 0.50f, 0.10f),
                new Vector3(1.00f, 0.10f, 0.10f));
        }

        private static Vector3 PaletteClamp(float amount)
        {
            float t = 1f - MathF.Exp(-amount * 0.5f);
            return MultiStop(t,
                new Vector3(0.00f, 0.00f, 0.00f),
                new Vector3(1.00f, 0.50f, 0.10f),
                new Vector3(1.00f, 0.15f, 0.10f),
                new Vector3(1.00f, 0.80f, 0.70f),
                new Vector3(1.00f, 1.00f, 1.00f));
        }

        private static Vector3 PaletteDepth(float depth)
        {
            float t = 1f - MathF.Exp(-depth * 0.08f);
            return MultiStop(t,
                new Vector3(0.02f, 0.02f, 0.05f),
                new Vector3(0.08f, 0.15f, 0.45f),
                new Vector3(0.10f, 0.60f, 0.80f),
                new Vector3(0.90f, 0.90f, 0.45f),
                new Vector3(1.00f, 1.00f, 1.00f));
        }

        private static Vector3 PaletteDifference(float diff)
        {
            float t = 1f - MathF.Exp(-diff * 6f);
            return MultiStop(t,
                new Vector3(0.00f, 0.00f, 0.00f),
                new Vector3(0.10f, 0.15f, 0.50f),
                new Vector3(0.20f, 0.80f, 0.70f),
                new Vector3(0.95f, 0.90f, 0.20f),
                new Vector3(1.00f, 0.20f, 0.15f));
        }

        private static Vector3 PaletteAlbedo(float albedo)
        {
            float v = Math.Clamp(albedo, 0f, 1f);
            return new Vector3(v, v, v);
        }

        private static Vector3 PaletteNormal(Vector3 normal)
        {
            if (normal == Vector3.Zero)
                return Vector3.Zero;
            Vector3 n = Vector3.Normalize(normal);
            return n * 0.5f + new Vector3(0.5f, 0.5f, 0.5f);
        }

        private static Vector3 MultiStop(float t, Vector3 c0, Vector3 c1, Vector3 c2, Vector3 c3, Vector3 c4)
        {
            if (t <= 0.25f) return Vector3.Lerp(c0, c1, t / 0.25f);
            if (t <= 0.5f) return Vector3.Lerp(c1, c2, (t - 0.25f) / 0.25f);
            if (t <= 0.75f) return Vector3.Lerp(c2, c3, (t - 0.5f) / 0.25f);
            return Vector3.Lerp(c3, c4, (t - 0.75f) / 0.25f);
        }
    }
}
