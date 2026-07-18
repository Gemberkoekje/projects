using System.Numerics;
using System.Runtime.CompilerServices;

namespace RayTracer;

public partial class JobSystem
{
    private sealed class DisplayResolver(JobSystem owner)
    {
        private readonly JobSystem _owner = owner;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Render(int stride, byte[] buffer, int y, int x)
        {
            Vector3 xyz = _owner.ResolveFilteredXYZ(y, x);
            WritePixel(buffer.AsSpan(y * stride + x * 4, 4), xyz, _owner.Realism.ToneMapping, _owner.Realism.Exposure);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void WritePixel(Span<byte> dest, Vector3 xyz)
            => WritePixel(dest, xyz, ToneMapping.None, 1f);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void WritePixel(Span<byte> dest, Vector3 xyz, ToneMapping toneMapping, float exposure)
        {
            var linearColor = ApplyToneMap(JobSystem.ToSRGB(xyz), toneMapping, exposure);
            var color = new Vector3(
                LinearToSRGB(linearColor.X),
                LinearToSRGB(linearColor.Y),
                LinearToSRGB(linearColor.Z));

            dest[0] = (byte)Math.Clamp(color.Z * 255f, 0, 255);
            dest[1] = (byte)Math.Clamp(color.Y * 255f, 0, 255);
            dest[2] = (byte)Math.Clamp(color.X * 255f, 0, 255);
            dest[3] = 255;
        }

        private const float InvGamma = 1.0f / 2.4f;

        /// <summary>
        /// Realism finding §14: an optional display-space tone map applied to the linear-sRGB colour
        /// before the sRGB transfer. <see cref="ToneMapping.None"/> is the historical straight clip
        /// (exposure ignored), so the default is a no-op; the other operators apply an exposure scale
        /// then a global roll-off that tames highlight hue-shift near bright lights. Mirrors the GPU
        /// ApplyToneMap in ResolvePhase6.hlsl.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector3 ApplyToneMap(Vector3 c, ToneMapping toneMapping, float exposure)
        {
            if (toneMapping == ToneMapping.None)
                return c;

            c *= exposure;
            return toneMapping switch
            {
                ToneMapping.Reinhard => new Vector3(c.X / (1f + c.X), c.Y / (1f + c.Y), c.Z / (1f + c.Z)),
                ToneMapping.Filmic => new Vector3(AcesFilmic(c.X), AcesFilmic(c.Y), AcesFilmic(c.Z)),
                _ => c, // Exposure-only
            };
        }

        // Narkowicz's cheap ACES filmic curve, clamped to [0,1].
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float AcesFilmic(float x)
        {
            const float a = 2.51f, b = 0.03f, c = 2.43f, d = 0.59f, e = 0.14f;
            return Math.Clamp(x * (a * x + b) / (x * (c * x + d) + e), 0f, 1f);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float LinearToSRGB(float linear)
        {
            if (linear <= 0.0031308f)
                return 12.92f * linear;
            return 1.055f * MathF.Pow(linear, InvGamma) - 0.055f;
        }
    }
}
