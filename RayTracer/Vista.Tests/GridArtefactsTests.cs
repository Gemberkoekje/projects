using Vista.World;

namespace Vista.Tests;

/// <summary>
/// The grid's own patterns (vista-plan §10 M1a.1): the alignment of the relief with the grid's eight directions and the share of the
/// field that ripples at a few cells, on synthetic fields whose patterns are known.
/// </summary>
[TestClass]
public sealed class GridArtefactsTests
{
    private const int Size = 257;
    private const double Cell = 8.0;

    // Relief that runs every way: waves in random directions with wavelengths from 3 to 60 cells, each as high as it is long / 20.
    private static float[] Isotropic(int seed)
    {
        var random = new Random(seed);
        const int waves = 1500;
        var kx = new double[waves];
        var kz = new double[waves];
        var amplitude = new double[waves];
        var phase = new double[waves];
        for (int w = 0; w < waves; w++)
        {
            double cells = 3.0 * Math.Pow(20.0, random.NextDouble());
            double angle = Math.PI * random.NextDouble();
            kx[w] = 2.0 * Math.PI * Math.Cos(angle) / cells;
            kz[w] = 2.0 * Math.PI * Math.Sin(angle) / cells;
            amplitude[w] = cells * Cell / 20.0 / Math.Sqrt(waves);
            phase[w] = 2.0 * Math.PI * random.NextDouble();
        }
        var heights = new float[Size * Size];
        Parallel.For(0, Size, j =>
        {
            for (int i = 0; i < Size; i++)
            {
                double h = 0.0;
                for (int w = 0; w < waves; w++)
                    h += amplitude[w] * Math.Sin(kx[w] * i + kz[w] * j + phase[w]);
                heights[j * Size + i] = (float)h;
            }
        });
        return heights;
    }

    // A slope combed by straight grooves `cells` apart, running at `degrees` from the grid's rows, 4 m deep.
    private static float[] Grooves(double degrees, double cells)
    {
        double a = degrees * Math.PI / 180.0;
        var heights = new float[Size * Size];
        for (int j = 0; j < Size; j++)
        {
            for (int i = 0; i < Size; i++)
            {
                double across = -i * Math.Sin(a) + j * Math.Cos(a);
                heights[j * Size + i] = (float)(0.6 * Cell * i + 4.0 * Math.Sin(2.0 * Math.PI * across / cells));
            }
        }
        return heights;
    }

    [TestMethod]
    public void ReliefThatRunsEveryWay_SitsNearTheIsotropicShare_AndDoesNotRipple()
    {
        GridArtefacts s = GridArtefacts.Measure(new HeightField(Size, Cell, Isotropic(7)));
        Assert.AreEqual(Cell, s.SpacingMetres);
        Assert.AreEqual((long)(Size - 2) * (Size - 2), s.Cells);
        Assert.AreEqual(4.0 * 6.0 / 180.0, GridArtefacts.IsotropicShare, 1e-12);
        Assert.IsTrue(s.AlignedShare > 0.10 && s.AlignedShare < 0.19, $"isotropic relief: {s.AlignedShare:0.0%} along the grid");
        Assert.AreEqual((long)Math.Pow((Size - GridArtefacts.WindowCells) / GridArtefacts.WindowStride + 1, 2), s.Windows);
        Assert.IsTrue(s.RippleShare < 0.02, $"isotropic relief: {s.RippleShare:0.0%} of the windows ripple");
    }

    [TestMethod]
    public void GroovesAlongTheGrid_AreAligned_TurnedOnesAreNot_BothRipple()
    {
        GridArtefacts rows = GridArtefacts.Measure(new HeightField(Size, Cell, Grooves(0.0, 3.3)));
        Assert.IsTrue(rows.AlignedShare > 0.95, $"grooves along the rows: {rows.AlignedShare:0.0%}");
        Assert.IsTrue(rows.RippleShare > 0.9, $"grooves 3.3 cells apart: {rows.RippleShare:0.0%} ripple");
        GridArtefacts diagonal = GridArtefacts.Measure(new HeightField(Size, Cell, Grooves(45.0, 3.3)));
        Assert.IsTrue(diagonal.AlignedShare > 0.95, $"grooves along the diagonals: {diagonal.AlignedShare:0.0%}");
        GridArtefacts turned = GridArtefacts.Measure(new HeightField(Size, Cell, Grooves(20.0, 3.3)));
        Assert.IsTrue(turned.AlignedShare < 0.05, $"grooves at 20°: {turned.AlignedShare:0.0%}");
        Assert.IsTrue(turned.RippleShare > 0.9, $"grooves at 20°: {turned.RippleShare:0.0%} ripple");
        GridArtefacts wide = GridArtefacts.Measure(new HeightField(Size, Cell, Grooves(20.0, 12.0)));
        Assert.IsTrue(wide.RippleShare < 0.05, $"grooves 12 cells apart are not a ripple of a few cells: {wide.RippleShare:0.0%}");
    }

    [TestMethod]
    public void AWashboardPatch_RipplesItsShare_AndTheFilterFindsIt()
    {
        float[] heights = Isotropic(11);
        float[] board = Grooves(35.0, 2.6);
        bool InPatch(int c) => c % Size < Size / 2 && c / Size < Size / 2;
        for (int c = 0; c < heights.Length; c++)
        {
            if (InPatch(c))
                heights[c] += board[c] - (float)(0.6 * Cell * (c % Size));
        }
        var field = new HeightField(Size, Cell, heights);
        GridArtefacts all = GridArtefacts.Measure(field);
        Assert.IsTrue(all.RippleShare > 0.15 && all.RippleShare < 0.35, $"a quarter washboard: {all.RippleShare:0.0%} of the windows");
        GridArtefacts patch = GridArtefacts.Measure(field, InPatch);
        Assert.IsTrue(patch.RippleShare > 0.85, $"inside the patch: {patch.RippleShare:0.0%}");
        Assert.IsTrue(patch.Windows < all.Windows / 3, $"{patch.Windows} of {all.Windows} windows");
        GridArtefacts none = GridArtefacts.Measure(field, _ => false);
        Assert.AreEqual(0L, none.Cells);
        Assert.AreEqual(0L, none.Windows);
        Assert.IsTrue(double.IsNaN(none.AlignedShare) && double.IsNaN(none.RippleShare));
    }
}
