using System.Numerics;
using RayTracer;

namespace RayTracer.Tests;

[TestClass]
public class BVHTests
{
    /// <summary>
    /// Verify that the BVH finds the same closest-hit as a linear scan
    /// for a maze scene with many quads.
    /// </summary>
    [TestMethod]
    public void BVH_MatchesLinearScan_ForMazeScene()
    {
        var materials = new MaterialsLookup();
        var maze = new Maze(8, 8, seed: 42);
        var scene = MazeGeometryBuilder.Build(
            maze,
            wallMaterial: materials["00115"],
            floorMaterial: materials["01085"],
            ceilingMaterial: materials["01138"]);

        var bvh = new BVH(scene);

        // Cast rays from the center of cell (0,0) in several directions.
        float cs = MazeGeometryBuilder.CellSize;
        float wh = MazeGeometryBuilder.WallHeight;
        Vector3 origin = new(cs * 0.5f, wh * 0.5f, cs * 0.5f);

        Vector3[] directions =
        [
            Vector3.Normalize(new Vector3(0, 0, 1)),
            Vector3.Normalize(new Vector3(1, 0, 0)),
            Vector3.Normalize(new Vector3(0, 1, 0)),
            Vector3.Normalize(new Vector3(0, -1, 0)),
            Vector3.Normalize(new Vector3(1, 0.2f, 1)),
            Vector3.Normalize(new Vector3(-1, 0.1f, -1)),
        ];

        foreach (var dir in directions)
        {
            var ray = new Ray
            {
                Origin = origin,
                Direction = dir,
                Wavelength = 550,
                Intensity = 1f,
            };

            // Linear scan
            float linearDist = float.MaxValue;
            float linearReflectance = 0f;
            bool linearHit = false;
            foreach (var t in scene)
            {
                var intersect = t.Intersect(ray);
                if (intersect.HasValue)
                {
                    float d = Vector3.Distance(origin, intersect.Value.Location);
                    if (d < linearDist)
                    {
                        linearDist = d;
                        linearReflectance = intersect.Value.Reflectance;
                        linearHit = true;
                    }
                }
            }

            // BVH
            var (bvhReflectance, _, _, _, bvhHit, _, _, _, _, _) = bvh.FindClosest(ray);

            Assert.AreEqual(linearHit, bvhHit, $"Hit mismatch for direction {dir}");
            if (linearHit)
            {
                Assert.AreEqual(linearReflectance, bvhReflectance, 1e-5f,
                    $"Reflectance mismatch for direction {dir}");
            }
        }
    }

    [TestMethod]
    public void AABB_RayIntersects_HittingBox()
    {
        var box = new AABB(new Vector3(1, 1, 1), new Vector3(3, 3, 3));
        var origin = new Vector3(0, 2, 2);
        var dir = Vector3.Normalize(new Vector3(1, 0, 0));
        var invDir = new Vector3(1f / dir.X, 1f / dir.Y, 1f / dir.Z);
        Assert.IsTrue(box.Intersects(origin, invDir, float.MaxValue));
    }

    [TestMethod]
    public void AABB_RayIntersects_MissingBox()
    {
        var box = new AABB(new Vector3(1, 1, 1), new Vector3(3, 3, 3));
        var origin = new Vector3(0, 5, 5); // above and to the side
        var dir = Vector3.Normalize(new Vector3(1, 0, 0));
        var invDir = new Vector3(1f / dir.X, 1f / dir.Y, 1f / dir.Z);
        Assert.IsFalse(box.Intersects(origin, invDir, float.MaxValue));
    }

    [TestMethod]
    public void BVH_MatchesLinearScan_ForAllCoplanarRectangles()
    {
        var material = new MaterialsLookup()["00115"];
        var scene = new List<Tracable>();

        const int size = 20;
        for (int x = 0; x < size; x++)
        {
            for (int z = 0; z < size; z++)
            {
                scene.Add(new TracableRectangle(
                    (new Vector3(x, 0f, z), new Vector3(x + 1f, 0f, z), new Vector3(x, 0f, z + 1f)),
                    material));
            }
        }

        var ray = new Ray
        {
            Origin = new Vector3(size * 0.5f + 0.25f, 3f, size * 0.5f + 0.25f),
            Direction = Vector3.Normalize(new Vector3(0f, -1f, 0f)),
            Wavelength = 550,
            Intensity = 1f
        };

        var bvh = new BVH(scene.ToArray());
        var (linearReflectance, linearT, linearHit) = FindClosestLinear(scene, ray);
        var (bvhReflectance, bvhPoint, _, _, bvhHit, _, _, _, _, _) = bvh.FindClosest(ray);

        Assert.AreEqual(linearHit, bvhHit);
        if (linearHit)
        {
            Assert.AreEqual(linearReflectance, bvhReflectance, 1e-5f);
            Assert.AreEqual(ray.Origin.Y + ray.Direction.Y * linearT, bvhPoint.Y, 1e-4f);
        }
    }

    [TestMethod]
    public void BVH_HandlesZeroVolumeAabbs_AndStillFindsValidHit()
    {
        var material = new MaterialsLookup()["00115"];
        var scene = new List<Tracable>();

        for (int i = 0; i < 200; i++)
            scene.Add(new DegenerateBoundsTracable(new Vector3(i * 0.01f, 0.5f, i * 0.01f)));

        scene.Add(new TracableRectangle(
            (new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0f), new Vector3(0f, 0f, 1f)),
            material));

        var bvh = new BVH(scene.ToArray());
        var ray = new Ray
        {
            Origin = new Vector3(0.5f, 1f, 0.5f),
            Direction = new Vector3(0f, -1f, 0f),
            Wavelength = 550,
            Intensity = 1f
        };

        var (_, hitPoint, _, _, hit, hitPrimitive, _, _, _, _) = bvh.FindClosest(ray);

        Assert.IsTrue(hit, "BVH should still report a hit in the presence of many zero-volume bounds.");
        Assert.IsInstanceOfType<TracableRectangle>(hitPrimitive);
        Assert.AreEqual(0f, hitPoint.Y, 1e-4f);
    }

    private static (float reflectance, float t, bool hit) FindClosestLinear(IEnumerable<Tracable> scene, Ray ray)
    {
        float closestT = float.MaxValue;
        float closestReflectance = 0f;
        bool hit = false;

        foreach (var t in scene)
        {
            var intersect = t.Intersect(ray);
            if (intersect.HasValue && intersect.Value.T < closestT)
            {
                closestT = intersect.Value.T;
                closestReflectance = intersect.Value.Reflectance;
                hit = true;
            }
        }

        return (closestReflectance, closestT, hit);
    }

    private sealed class DegenerateBoundsTracable(Vector3 point) : Tracable
    {
        public AABB Bounds { get; } = new(point, point);

        public HitInfo? Intersect(Ray ray)
            => null;
    }
}
