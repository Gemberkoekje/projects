using System.Numerics;

namespace RayTracer;

public partial class JobSystem
{
    private sealed class DisplayResolver(JobSystem owner)
    {
        private readonly JobSystem _owner = owner;

        public void Render(int stride, byte[] buffer, int y, int x)
        {
            Vector3 xyz = _owner.ResolveFilteredXYZ(y, x);
            var linearColor = JobSystem.ToSRGB(xyz);

            var color = new Vector3(
                LinearToSRGB(linearColor.X),
                LinearToSRGB(linearColor.Y),
                LinearToSRGB(linearColor.Z));

            int i = y * stride + x * 4;
            buffer[i + 0] = (byte)Math.Clamp(color.Z * 255f, 0, 255);
            buffer[i + 1] = (byte)Math.Clamp(color.Y * 255f, 0, 255);
            buffer[i + 2] = (byte)Math.Clamp(color.X * 255f, 0, 255);
            buffer[i + 3] = 255;
        }

        private const float InvGamma = 1.0f / 2.4f;

        private static float LinearToSRGB(float linear)
        {
            if (linear <= 0.0031308f)
                return 12.92f * linear;
            return 1.055f * MathF.Pow(linear, InvGamma) - 0.055f;
        }
    }
}
