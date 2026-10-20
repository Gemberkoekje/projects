using Vista.World;

namespace Vista.Tests;

/// <summary>Height field sampling (vista-plan §10 M0). The mesh itself is the view's since M1.9 (<see cref="ViewMesherTests"/>).</summary>
[TestClass]
public sealed class TerrainMeshTests
{
    // h = a·x + b·z + c: a tilted plane, for which bilinear sampling is exact.
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
    public void HeightField_RejectsMismatchedData() =>
        Assert.ThrowsExactly<ArgumentException>(() => new HeightField(4, 1.0, new float[15]));
}
