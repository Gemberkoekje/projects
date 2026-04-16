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
            WritePixel(buffer.AsSpan(y * stride + x * 4, 4), xyz);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void WritePixel(Span<byte> dest, Vector3 xyz)
        {
            var linearColor = JobSystem.ToSRGB(xyz);
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float LinearToSRGB(float linear)
        {
            if (linear <= 0.0031308f)
                return 12.92f * linear;
            return 1.055f * MathF.Pow(linear, InvGamma) - 0.055f;
        }
    }
}
