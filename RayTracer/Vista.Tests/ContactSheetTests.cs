using Vista.Output;

namespace Vista.Tests;

/// <summary>Contact-sheet layout and compositing (vista-plan §7.4).</summary>
[TestClass]
public sealed class ContactSheetTests
{
    [TestMethod]
    [DataRow(1, 1)]
    [DataRow(2, 2)]
    [DataRow(3, 3)]
    [DataRow(4, 2)]
    [DataRow(5, 3)]
    [DataRow(9, 3)]
    [DataRow(10, 4)]
    public void DefaultColumns_RowUpToThreeThenNearSquare(int count, int columns) =>
        Assert.AreEqual(columns, ContactSheet.DefaultColumns(count));

    [TestMethod]
    [DataRow(64, 1)]
    [DataRow(320, 1)]
    [DataRow(639, 1)]
    [DataRow(640, 2)]
    [DataRow(1280, 4)]
    [DataRow(3840, 4)]
    public void Scale_GrowsWithTheCellWidth(int cellWidth, int scale) =>
        Assert.AreEqual(scale, ContactSheet.ScaleFor(cellWidth));

    [TestMethod]
    public void FourPreviews_MakeATwoByTwoGridUnderTheTitle()
    {
        var sizes = Enumerable.Repeat((320, 180), 4).ToList();
        SheetLayout layout = ContactSheet.Layout(sizes, ["seed=1", "seed=2", "seed=3", "seed=4"], 0, "alpine range · seed 42");

        // Scale 1: 6 px margins and gaps, an 11 px title line, a 13 px label strip under each image.
        Assert.AreEqual((2, 2, 1), (layout.Columns, layout.Rows, layout.Scale));
        Assert.AreEqual(6 + 320 + 6 + 320 + 6, layout.Width);
        Assert.AreEqual(6 + 11 + 6 + (180 + 13) + 6 + (180 + 13) + 6, layout.Height);
        Assert.AreEqual(new SheetText(6, 6, "alpine range · seed 42"), layout.Title.Single());
        SheetPlacement fourth = layout.Cells[3];
        Assert.AreEqual((1, 1, 332, 222, 320, 180), (fourth.Column, fourth.Row, fourth.X, fourth.Y, fourth.Width, fourth.Height));
        Assert.AreEqual(new SheetText(332, 222 + 180 + 2, "seed=4"), fourth.Label.Single());
    }

    [TestMethod]
    public void NoTitle_StartsTheGridAtTheMargin()
    {
        SheetLayout layout = ContactSheet.Layout([(64, 64)], ["a"], 0, "");
        Assert.AreEqual(0, layout.Title.Count);
        Assert.AreEqual((6, 6), (layout.Cells[0].X, layout.Cells[0].Y));
        Assert.AreEqual(6 + 64 + 13 + 6, layout.Height);
    }

    [TestMethod]
    public void MixedSizes_ShareTheLargestCell()
    {
        SheetLayout layout = ContactSheet.Layout([(320, 180), (64, 64), (100, 200)], ["a", "b", "c"], 2, "");
        Assert.AreEqual((320, 200), (layout.CellWidth, layout.CellHeight));
        Assert.AreEqual((64, 64), (layout.Cells[1].Width, layout.Cells[1].Height));
        Assert.AreEqual(layout.Cells[0].Label[0].Y, layout.Cells[1].Label[0].Y, "labels line up along a row");
        Assert.AreEqual((0, 1), (layout.Cells[2].Column, layout.Cells[2].Row));
    }

    [TestMethod]
    public void LongLabelsAndTitles_FitTheirSpace()
    {
        SheetLayout layout = ContactSheet.Layout([(64, 36), (64, 36)], ["camera=vista-views/down-slope.camera.json", "b"], 0,
            "snow-capped mountains, day · snowCappedMountains · summer · seed 42");
        Assert.IsTrue(layout.Cells.SelectMany(c => c.Label).All(l => BitmapFont.Measure(l.Text, layout.Scale) <= layout.CellWidth));
        StringAssert.EndsWith(layout.Cells[0].Label.Single().Text, BitmapFont.Ellipsis, "one word too long for the cell is cut");
        Assert.IsTrue(layout.Title.Count > 1, "the title wraps");
        Assert.IsTrue(layout.Title.All(t => BitmapFont.Measure(t.Text, layout.Scale) <= layout.Width - 12));
    }

    [TestMethod]
    public void LongLabels_WrapOntoUpToThreeLines()
    {
        // 10 characters fit a 64 px cell at scale 1.
        SheetLayout layout = ContactSheet.Layout([(64, 36), (64, 36), (64, 36)],
            ["build=11e489b seed=1", "build=b1df1da seed=1 time=dawn season=winter", "x"], 0, "");
        CollectionAssert.AreEqual(new[] { "build=11e…", "seed=1" }, layout.Cells[0].Label.Select(l => l.Text).ToArray());
        CollectionAssert.AreEqual(new[] { "build=b1d…", "seed=1", "time=dawn…" }, layout.Cells[1].Label.Select(l => l.Text).ToArray(),
            "the third line carries the rest, cut");
        Assert.AreEqual(1, layout.Cells[2].Label.Count);
        Assert.AreEqual(36 + 2 + 11, layout.Cells[0].Label[1].Y - layout.Cells[0].Y, "lines are a line height apart");
        Assert.AreEqual(6 + 36 + 2 + 3 * 11 + 6, layout.Height, "every label strip is as tall as the longest label");
    }

    [TestMethod]
    public void BadInput_IsRejected()
    {
        Assert.ThrowsExactly<ArgumentException>(() => ContactSheet.Layout([], [], 0, ""));
        Assert.ThrowsExactly<ArgumentException>(() => ContactSheet.Layout([(4, 4)], [], 0, ""));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ContactSheet.Layout([(4, 4), (4, 4)], ["a", "b"], 3, ""));
        Assert.ThrowsExactly<ArgumentException>(() => ContactSheet.Layout([(0, 4)], ["a"], 0, ""));
        Assert.ThrowsExactly<ArgumentException>(() =>
            ContactSheet.Layout(Enumerable.Repeat((4, 4), ContactSheet.MaxTiles + 1).ToList(), Enumerable.Repeat("x", ContactSheet.MaxTiles + 1).ToList(), 0, ""));
    }

    [TestMethod]
    public void Compose_PlacesTilesAndLabelsOnTheBackground()
    {
        byte[] red = Solid(10, 6, 255, 0, 0);
        byte[] blue = Solid(10, 6, 0, 0, 255);
        SheetLayout layout = ContactSheet.Layout([(10, 6), (10, 6)], ["A", ""], 0, "T");
        byte[] sheet = ContactSheet.Compose(layout, [red, blue]);

        Assert.AreEqual(layout.Width * layout.Height * 3, sheet.Length);
        Assert.AreEqual((24, 24, 24), Pixel(sheet, layout.Width, 0, 0), "background");
        SheetPlacement first = layout.Cells[0], second = layout.Cells[1];
        Assert.AreEqual((255, 0, 0), Pixel(sheet, layout.Width, first.X, first.Y));
        Assert.AreEqual((255, 0, 0), Pixel(sheet, layout.Width, first.X + 9, first.Y + 5));
        Assert.AreEqual((0, 0, 255), Pixel(sheet, layout.Width, second.X, second.Y));
        Assert.AreEqual((24, 24, 24), Pixel(sheet, layout.Width, first.X + 10, first.Y), "the gap between cells");
        // 'A' has ink in its top row's middle column; 'T' in its whole top row.
        Assert.AreEqual((200, 200, 200), Pixel(sheet, layout.Width, first.Label[0].X + 2, first.Label[0].Y));
        Assert.AreEqual((240, 240, 240), Pixel(sheet, layout.Width, layout.Title[0].X, layout.Title[0].Y));
    }

    [TestMethod]
    public void Compose_ChecksTileSizes() =>
        Assert.ThrowsExactly<ArgumentException>(() =>
            ContactSheet.Compose(ContactSheet.Layout([(10, 6)], ["a"], 0, ""), [Solid(10, 5, 1, 2, 3)]));

    private static byte[] Solid(int width, int height, byte r, byte g, byte b)
    {
        var rgb = new byte[width * height * 3];
        for (int p = 0; p < rgb.Length; p += 3)
        {
            rgb[p] = r;
            rgb[p + 1] = g;
            rgb[p + 2] = b;
        }
        return rgb;
    }

    private static (int, int, int) Pixel(byte[] rgb, int width, int x, int y)
    {
        int o = (y * width + x) * 3;
        return (rgb[o], rgb[o + 1], rgb[o + 2]);
    }
}
