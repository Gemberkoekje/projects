using Vista.Rendering;
using Vista.Scene;
using Vista.World;

namespace Vista.Composition;

/// <summary>
/// What a pixel of a <see cref="CompositionBuffer"/> shows, in the few kinds the composition scores read (vista-plan §10 M1.14): the
/// sky, or the ground's material folded into rock, scree, meadow, forest and snow.
/// </summary>
public enum GroundClass : byte
{
    /// <summary>Not traced.</summary>
    None,
    /// <summary>No terrain: the ray left for the sky.</summary>
    Sky,
    /// <summary>Bare rock, hard or soft.</summary>
    Rock,
    /// <summary>Scree.</summary>
    Scree,
    /// <summary>Soil and alpine grass.</summary>
    Meadow,
    /// <summary>Forest.</summary>
    Forest,
    /// <summary>The season's snow, dry or wet, and a glacier's firn and ice: the ground the near-field limit lets through (M1.13).</summary>
    Snow,
}

/// <summary>The <see cref="GroundClass"/> of the renderer's material ids and of the material rules' weights.</summary>
public static class GroundClasses
{
    /// <summary>The class of a value of the renderer's material-id output (<see cref="Aov.MaterialId"/>; −1 for the sky).</summary>
    public static GroundClass OfMaterialId(float id) => (int)MathF.Round(id) switch
    {
        < 0 => GroundClass.Sky,
        MaterialCatalog.RockHardId or MaterialCatalog.RockSoftId => GroundClass.Rock,
        MaterialCatalog.ScreeId => GroundClass.Scree,
        MaterialCatalog.SoilId or MaterialCatalog.GrassId => GroundClass.Meadow,
        MaterialCatalog.ForestId => GroundClass.Forest,
        MaterialCatalog.SnowId or MaterialCatalog.WetSnowId or MaterialCatalog.FirnId or MaterialCatalog.IceId => GroundClass.Snow,
        _ => GroundClass.Rock,
    };

    /// <summary>
    /// The class that weighs most in <paramref name="w"/>: rock, scree, meadow (soil and grass), forest or snow (the season's snow, firn
    /// and ice together), the rock winning a tie.
    /// </summary>
    public static GroundClass OfWeights(in MaterialWeights w)
    {
        (GroundClass Class, float Weight) best = (GroundClass.Rock, w.Rock);
        void Take(GroundClass c, float weight)
        {
            if (weight > best.Weight)
                best = (c, weight);
        }
        Take(GroundClass.Scree, w.Scree);
        Take(GroundClass.Meadow, w.Soil + w.Grass);
        Take(GroundClass.Forest, w.Forest);
        Take(GroundClass.Snow, w.Snow + w.Firn + w.Ice);
        return best.Class;
    }
}

/// <summary>
/// A camera's view as the composition scores read it (vista-plan §4.8, §10 M1.14): per pixel, how far the ray went, the surface's
/// normal, what it hit (<see cref="GroundClass"/>) and how high that ground stands above its valley floor. The auto camera traces one
/// at low resolution for each candidate on the CPU (<see cref="TerrainSight"/>), and reads the finalists' from the renderer's own
/// outputs (<see cref="FromRender"/>). Rows run from the top of the frame down.
/// </summary>
public sealed class CompositionBuffer
{
    /// <summary>An empty buffer of <paramref name="width"/>×<paramref name="height"/> pixels: every pixel sky.</summary>
    public CompositionBuffer(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 4);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 4);
        Width = width;
        Height = height;
        Depth = new float[width * height];
        Normal = new float[width * height * 3];
        Class = new GroundClass[width * height];
        Rise = new float[width * height];
        Array.Fill(Depth, float.PositiveInfinity);
        Array.Fill(Class, GroundClass.Sky);
        Array.Fill(Rise, float.NaN);
    }

    /// <summary>Pixels across.</summary>
    public int Width { get; }

    /// <summary>Pixels down.</summary>
    public int Height { get; }

    /// <summary>The distance along each pixel's ray to the ground, m; +∞ for the sky.</summary>
    public float[] Depth { get; }

    /// <summary>The ground's normal in world axes, three floats a pixel; zero for the sky.</summary>
    public float[] Normal { get; }

    /// <summary>What each pixel shows.</summary>
    public GroundClass[] Class { get; }

    /// <summary>How high the ground stands above its valley floor (<see cref="PoolMap"/>'s floor), m; NaN where unknown and for the sky.</summary>
    public float[] Rise { get; }

    /// <summary>True when pixel (<paramref name="x"/>, <paramref name="y"/>) shows the ground.</summary>
    public bool IsGround(int x, int y) => Class[y * Width + x] is not (GroundClass.Sky or GroundClass.None);

    /// <summary>
    /// The buffer of a render (vista-plan §10 M1.14's finalists): its depth, normal and material-id outputs, and each ground pixel's
    /// rise above the valley floor worked out from where its ray met the ground (<paramref name="camera"/>'s ray through the pixel's
    /// centre; the renderer's depth is its sample 0's, inside the pixel) against <paramref name="floors"/>, when given, in the frame bent
    /// onto a ground sphere of <paramref name="groundRadius"/>.
    /// </summary>
    public static CompositionBuffer FromRender(RenderOutput output, CameraSpec camera, int frameWidth, int frameHeight, PoolMap? floors,
        double groundRadius)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(camera);
        if (!output.Aovs.TryGetValue(Aov.Depth, out float[]? depth) || !output.Aovs.TryGetValue(Aov.MaterialId, out float[]? ids)
            || !output.Aovs.TryGetValue(Aov.Normal, out float[]? normals))
            throw new ArgumentException("The render has no depth, normal and material-id outputs.", nameof(output));
        var buffer = new CompositionBuffer(output.Width, output.Height);
        var rays = new CameraRays(camera, frameWidth, frameHeight);
        for (int y = 0; y < output.Height; y++)
        {
            for (int x = 0; x < output.Width; x++)
            {
                int p = y * output.Width + x;
                GroundClass c = GroundClasses.OfMaterialId(ids[p]);
                if (c == GroundClass.Sky || !float.IsFinite(depth[p]))
                    continue;
                buffer.Depth[p] = depth[p];
                buffer.Class[p] = c;
                buffer.Normal[p * 3] = normals[p * 3];
                buffer.Normal[p * 3 + 1] = normals[p * 3 + 1];
                buffer.Normal[p * 3 + 2] = normals[p * 3 + 2];
                if (floors is not null)
                {
                    Vector3D d = rays.Direction(x, y);
                    double s = depth[p];
                    double wx = camera.Position.X + d.X * s, wz = camera.Position.Z + d.Z * s;
                    // The renderer's frame is bent: a point s along the ground sinks by s²/2R, which its height above the sphere puts back.
                    double horizontal2 = (d.X * d.X + d.Z * d.Z) * s * s;
                    double wy = camera.Position.Y + d.Y * s + horizontal2 / (2.0 * groundRadius);
                    buffer.Rise[p] = (float)(wy - FloorAt(floors, wx, wz));
                }
            }
        }
        return buffer;
    }

    /// <summary>
    /// The valley floor under world (<paramref name="x"/>, <paramref name="z"/>), m above the world's height 0: the finest of
    /// <paramref name="floors"/>' levels that holds the point, bilinear; NaN beyond them all.
    /// </summary>
    public static double FloorAt(PoolMap floors, double x, double z)
    {
        ArgumentNullException.ThrowIfNull(floors);
        foreach (PoolLevel level in floors.Levels)
        {
            double u = (x - level.OriginX) / level.CellMetres, v = (z - level.OriginZ) / level.CellMetres;
            if (!(u >= 0.0 && v >= 0.0 && u <= level.Size - 1 && v <= level.Size - 1))
                continue;
            int i = Math.Min((int)u, level.Size - 2), j = Math.Min((int)v, level.Size - 2);
            double fu = u - i, fv = v - j;
            int n = level.Size;
            double bottom = level.Floor[j * n + i] + (level.Floor[j * n + i + 1] - level.Floor[j * n + i]) * fu;
            double top = level.Floor[(j + 1) * n + i] + (level.Floor[(j + 1) * n + i + 1] - level.Floor[(j + 1) * n + i]) * fu;
            return bottom + (top - bottom) * fv;
        }
        return double.NaN;
    }
}

/// <summary>
/// A pinhole camera's rays through pixel centres, as the integrator makes them (<c>VistaIntegrator.hlsl</c>'s camera ray with the
/// pixel's jitter at its centre): <c>forward + px·right + py·up</c>, normalised.
/// </summary>
public readonly struct CameraRays
{
    private readonly Vector3D _forward, _right, _up;
    private readonly double _tanHalf, _aspect;
    private readonly int _width, _height;

    /// <summary>The rays of <paramref name="camera"/> on a <paramref name="width"/>×<paramref name="height"/> frame.</summary>
    public CameraRays(CameraSpec camera, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(camera);
        (_forward, _right, _up) = (camera.Forward(), camera.Right(), camera.Up());
        _tanHalf = Math.Tan(0.5 * camera.VerticalFovRadians());
        _aspect = (double)width / height;
        (_width, _height) = (width, height);
    }

    /// <summary>The unit direction through the centre of pixel (<paramref name="x"/>, <paramref name="y"/>), row 0 at the top.</summary>
    public Vector3D Direction(double x, double y)
    {
        double px = (2.0 * ((x + 0.5) / _width) - 1.0) * _tanHalf * _aspect;
        double py = (1.0 - 2.0 * ((y + 0.5) / _height)) * _tanHalf;
        return (_forward + _right * px + _up * py).Normalized();
    }

    /// <summary>
    /// Where direction <paramref name="d"/> lands on the frame, in pixels (row 0 at the top), or null behind the camera. Fractional:
    /// pixel (x, y)'s centre is at (x + 0.5, y + 0.5).
    /// </summary>
    public (double X, double Y)? Project(Vector3D d)
    {
        double f = Vector3D.Dot(d, _forward);
        if (!(f > 1e-9))
            return null;
        double px = Vector3D.Dot(d, _right) / f, py = Vector3D.Dot(d, _up) / f;
        return ((px / (_tanHalf * _aspect) + 1.0) * 0.5 * _width, (1.0 - py / _tanHalf) * 0.5 * _height);
    }
}
