using Vista.World;

namespace Vista.Tests;

/// <summary>Height field sampling and the height field → mesh step (vista-plan §10 M0).</summary>
[TestClass]
public sealed class TerrainMeshTests
{
    // h = a·x + b·z + c: a tilted plane, for which bilinear sampling and central differences are exact.
    private static HeightField Plane(int size, double cell, double a, double b, double c)
    {
        var heights = new float[size * size];
        double origin = -0.5 * (size - 1) * cell;
        for (int j = 0; j < size; j++)
        {
            for (int i = 0; i < size; i++)
                heights[j * size + i] = (float)(a * (origin + i * cell) + b * (origin + j * cell) + c);
        }
        return new HeightField(size, cell, heights);
    }

    [TestMethod]
    public void HeightField_IsCentredOnTheOrigin()
    {
        HeightField field = Plane(5, 10.0, 0, 0, 0);
        Assert.AreEqual(40.0, field.Extent);
        Assert.AreEqual(-20.0, field.XOf(0));
        Assert.AreEqual(20.0, field.XOf(4));
        Assert.AreEqual(-20.0, field.ZOf(0));
    }

    [TestMethod]
    public void HeightAt_IsExactOnAPlaneAndClampsOutside()
    {
        HeightField field = Plane(9, 4.0, 0.5, -0.25, 100.0);
        Assert.AreEqual(100.0, field.HeightAt(0, 0), 1e-4);
        Assert.AreEqual(100.0 + 0.5 * 3.3 - 0.25 * -7.1, field.HeightAt(3.3, -7.1), 1e-4);
        Assert.AreEqual(field[8, 0], field.HeightAt(1000.0, -1000.0), 1e-4); // clamped to the south-east corner
    }

    [TestMethod]
    public void Mesh_HasOneVertexPerSampleAndTwoTrianglesPerCell()
    {
        TerrainMesh mesh = HeightFieldMesher.Build(Plane(17, 2.0, 0, 0, 0));
        Assert.AreEqual(17 * 17, mesh.VertexCount);
        Assert.AreEqual(16 * 16 * 2, mesh.TriangleCount);
        Assert.AreEqual(17L * 17 * 24 + 16L * 16 * 6 * 4, mesh.ByteSize);
        Assert.IsTrue(mesh.Indices.All(k => k < mesh.VertexCount));
    }

    [TestMethod]
    public void Mesh_PositionsSitOnTheHeightField()
    {
        HeightField field = TerrainGenerator.Generate(new Recipes.Recipe { SetPieces = [Recipes.SetPiece.AlpineRange] }, 3, new WorldGrid(33, 25.0));
        TerrainMesh mesh = HeightFieldMesher.Build(field);
        for (int j = 0; j < field.Size; j++)
        {
            for (int i = 0; i < field.Size; i++)
            {
                int v = (j * field.Size + i) * 3;
                Assert.AreEqual((float)field.XOf(i), mesh.Positions[v]);
                Assert.AreEqual(field[i, j], mesh.Positions[v + 1]);
                Assert.AreEqual((float)field.ZOf(j), mesh.Positions[v + 2]);
            }
        }
    }

    [TestMethod]
    public void Mesh_EveryTriangleFacesUp()
    {
        // The documented winding: (v1 − v0) × (v2 − v0) points to +Y, on rough terrain too.
        HeightField field = TerrainGenerator.Generate(new Recipes.Recipe { SetPieces = [Recipes.SetPiece.AlpineRange] }, 5, new WorldGrid(33, 25.0));
        TerrainMesh mesh = HeightFieldMesher.Build(field);
        for (int t = 0; t < mesh.TriangleCount; t++)
        {
            Scene.Vector3D p0 = Vertex(mesh, mesh.Indices[t * 3]);
            Scene.Vector3D p1 = Vertex(mesh, mesh.Indices[t * 3 + 1]);
            Scene.Vector3D p2 = Vertex(mesh, mesh.Indices[t * 3 + 2]);
            Assert.IsTrue(Scene.Vector3D.Cross(p1 - p0, p2 - p0).Y > 0.0, $"triangle {t} faces down");
        }
    }

    [TestMethod]
    public void Mesh_NormalsAreUnitAndExactOnAPlane()
    {
        const double a = 0.3, b = -0.7;
        TerrainMesh mesh = HeightFieldMesher.Build(Plane(9, 5.0, a, b, 20.0));
        Scene.Vector3D expected = new Scene.Vector3D(-a, 1.0, -b).Normalized();
        for (int v = 0; v < mesh.VertexCount; v++)
        {
            Assert.AreEqual(expected.X, mesh.Normals[v * 3], 1e-5);
            Assert.AreEqual(expected.Y, mesh.Normals[v * 3 + 1], 1e-5);
            Assert.AreEqual(expected.Z, mesh.Normals[v * 3 + 2], 1e-5);
        }
    }

    [TestMethod]
    public void Mesh_IsDeterministic()
    {
        HeightField field = TerrainGenerator.Generate(new Recipes.Recipe { SetPieces = [Recipes.SetPiece.RollingHills] }, 8, new WorldGrid(33, 25.0));
        TerrainMesh a = HeightFieldMesher.Build(field);
        TerrainMesh b = HeightFieldMesher.Build(field);
        CollectionAssert.AreEqual(a.Positions, b.Positions);
        CollectionAssert.AreEqual(a.Normals, b.Normals);
        CollectionAssert.AreEqual(a.Indices, b.Indices);
    }

    [TestMethod]
    public void HeightField_RejectsMismatchedData() =>
        Assert.ThrowsExactly<ArgumentException>(() => new HeightField(4, 1.0, new float[15]));

    private static Scene.Vector3D Vertex(TerrainMesh mesh, uint index) =>
        new(mesh.Positions[index * 3], mesh.Positions[index * 3 + 1], mesh.Positions[index * 3 + 2]);
}
