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
                    float d = Vector3.Distance(origin, intersect.Value.location);
                    if (d < linearDist)
                    {
                        linearDist = d;
                        linearReflectance = intersect.Value.reflectance;
                        linearHit = true;
                    }
                }
            }

            // BVH
            var (bvhReflectance, _, _, _, bvhHit, _) = bvh.FindClosest(ray);

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
}
