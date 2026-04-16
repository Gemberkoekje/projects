using System.Numerics;
using System.Runtime.CompilerServices;

namespace RayTracer;

public partial class JobSystem
{
    /// <summary>
    /// Controls how aggressively the bilateral filter preserves edges.
    /// Higher = more edge-preserving.  25 works well for normalised
    /// XYZ Y values in the 0-1 range.
    /// </summary>
    private const float BilateralSharpness = 25f;

    /// <summary>Exponent used in the sRGB gamma transfer function (1/2.4).</summary>
    private const float InvGamma = 1.0f / 2.4f;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Vector3 ResolveFilteredXYZ(int y, int x)
    {
        int radius = IsMoving ? FilterRadius : 0;
        if (radius <= 0)
            return _buffers.AccumXYZ[y * Width + x];

        int yMin = Math.Max(y - radius, 0);
        int yMax = Math.Min(y + radius, Height - 1);
        int xMin = Math.Max(x - radius, 0);
        int xMax = Math.Min(x + radius, Width - 1);

        if (EdgeAwareFilter)
        {
            Vector3 centerXyz = _buffers.AccumXYZ[y * Width + x];
            Vector3 xyz = Vector3.Zero;
            float totalWeight = 0f;
            for (int ny = yMin; ny <= yMax; ny++)
            {
                int rowOff = ny * Width;
                for (int nx = xMin; nx <= xMax; nx++)
                {
                    Vector3 neighbor = _buffers.AccumXYZ[rowOff + nx];
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
            int rowOff = ny * Width;
            for (int nx = xMin; nx <= xMax; nx++)
            {
                sum += _buffers.AccumXYZ[rowOff + nx];
                count++;
            }
        }

        return sum / count;
    }

    public static readonly Matrix3x3 TosRGBMatrix = new(
         3.2406f, -1.5372f, -0.4986f,
         -0.9689f, 1.8758f, 0.0415f,
         0.0557f, -0.2040f, 1.0570f
         );

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 ToSRGB(Vector3 xyz)
    {
        return xyz * TosRGBMatrix;
    }
}
