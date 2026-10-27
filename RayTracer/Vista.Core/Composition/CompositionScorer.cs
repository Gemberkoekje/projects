using Vista.Astronomy;
using Vista.Scene;

namespace Vista.Composition;

/// <summary>What a composition is about (vista-plan §10 M1.14): the recipe's mountain set piece.</summary>
public enum CompositionIntent
{
    /// <summary>Not chosen.</summary>
    None,
    /// <summary>Snow-capped mountains: one main peak, off-centre near a third, its snow in the light.</summary>
    Peak,
    /// <summary>An alpine range: a skyline of several summits across the frame.</summary>
    Range,
}

/// <summary>What a framing is scored against, besides its buffer.</summary>
/// <param name="Camera">The camera the buffer was traced from.</param>
/// <param name="Sun">Where the sun is.</param>
/// <param name="Intent">What the composition is about.</param>
/// <param name="MainPeak">The planted main peak (<see cref="ShotPlanter"/>), m above the world's height 0.</param>
/// <param name="SubjectDistanceMetres">How far the camera stands from the planted subject, horizontally, m.</param>
public sealed record FramingContext(CameraSpec Camera, SunPosition Sun, CompositionIntent Intent, WorldPoint MainPeak, double SubjectDistanceMetres);

/// <summary>
/// The limits a framing must keep to be picked at all (vista-plan §10 M1.14), recorded in the sidecar. <b>The near-field limit holds
/// until M5</b>: the materials' close-up look does not hold up yet (`m1-12-loupes.png`) and grass geometry is M5's, so ground nearer
/// than <see cref="NearFieldMetres"/> may cover at most <see cref="NearFieldMaxShare"/> of the frame unless it is snow (M1.13); the share
/// is relaxed as M5 and M7 make the near field hold.
/// </summary>
public sealed record CompositionLimits
{
    /// <summary>How near ground counts as the near field, m.</summary>
    public double NearFieldMetres { get; init; } = 100.0;

    /// <summary>The share of the frame the near field (but snow) may cover.</summary>
    public double NearFieldMaxShare { get; init; } = 0.02;

    /// <summary>How near ground counts as blocking the lens, snow or not, m.</summary>
    public double LensMetres { get; init; } = 25.0;

    /// <summary>The share of the frame ground blocking the lens may cover.</summary>
    public double LensMaxShare { get; init; } = 0.002;

    /// <summary>
    /// The most triangles the pick's 4K mesh may take: <see cref="TriangleMargin"/> under the 48 M one acceleration structure takes
    /// (vista-plan §10 M1.9), so a long lens on a busy view cannot reach the ceiling unnoticed (M1.12b's largest 4K view is 38.2 M).
    /// </summary>
    public long TriangleLimit => (long)(TriangleCeiling * (1.0 - TriangleMargin));

    /// <summary>The renderer's ceiling for one acceleration structure, triangles.</summary>
    public long TriangleCeiling { get; init; } = 48_000_000;

    /// <summary>The share of <see cref="TriangleCeiling"/> kept free.</summary>
    public double TriangleMargin { get; init; } = 0.15;

    /// <summary>
    /// The final render's frame, which the triangle count and the detail budget are taken at: the M1 target, 3840×2160. A larger
    /// target (M11's 8K and 16K) makes both stricter on their own.
    /// </summary>
    public int FinalFrameWidth { get; init; } = 3840;

    /// <summary>See <see cref="FinalFrameWidth"/>.</summary>
    public int FinalFrameHeight { get; init; } = 2160;

    /// <summary>
    /// <b>The detail budget</b> (vista-plan §10, M1.14's follow-up): a pixel of lit bare ground counts as over-magnified when the
    /// terrain's finest structure there (<see cref="ITerrainProbe.DetailMetres"/>: the world grid's cell) spans more than this many
    /// pixels of the final frame. Raking sun shows the relief a face lacks; until the mid-distance detail is built (the parked
    /// "nearest faces lack detail"), such faces stay out of the picks.
    /// </summary>
    public double DetailCellPixels { get; init; } = 12.0;

    /// <summary>The share of the frame lit, over-magnified bare ground may cover (<see cref="DetailCellPixels"/>).</summary>
    public double DetailMaxShare { get; init; } = 0.05;

    /// <summary>
    /// The ground the detail budget counts: bare ground whose relief the sun shows (rock, scree, snow; the forest's crowns and the
    /// meadow's are M5's, and they carry their own texture).
    /// </summary>
    public IReadOnlyList<GroundClass> DetailClasses { get; init; } = [GroundClass.Rock, GroundClass.Scree, GroundClass.Snow];

    /// <summary>The defaults.</summary>
    public static CompositionLimits Default { get; } = new();
}

/// <summary>
/// What the detail budget found in a frame (<see cref="CompositionScorer.Detail"/>): for each pixel of lit ground on the world grid, how
/// many final-frame pixels the terrain's finest structure there spans (0 elsewhere: the sky, shade, the far field, or under the threshold
/// it was measured from), and the shares it adds up to.
/// </summary>
public sealed class DetailMap
{
    private readonly CompositionBuffer _buffer;
    private readonly CompositionLimits _limits;

    /// <summary>The map of <paramref name="buffer"/>'s pixels, <paramref name="cellPixels"/> each, under <paramref name="limits"/>.</summary>
    public DetailMap(CompositionBuffer buffer, float[] cellPixels, CompositionLimits limits)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentNullException.ThrowIfNull(cellPixels);
        ArgumentNullException.ThrowIfNull(limits);
        if (cellPixels.Length != buffer.Width * buffer.Height)
            throw new ArgumentException("The map and the buffer are not the same frame.", nameof(cellPixels));
        (_buffer, CellPixels, _limits) = (buffer, cellPixels, limits);
    }

    /// <summary>How many final-frame pixels the terrain's finest structure spans at each lit pixel; 0 where nothing was measured.</summary>
    public float[] CellPixels { get; }

    /// <summary>True when pixel <paramref name="p"/> counts against the budget: lit, over-magnified, of a counted class.</summary>
    public bool Counts(int p) => CellPixels[p] > _limits.DetailCellPixels && _limits.DetailClasses.Contains(_buffer.Class[p]);

    /// <summary>The share of the frame that counts against the budget (<see cref="CompositionScores.DetailShare"/>).</summary>
    public double Share => ShareAt(_limits.DetailCellPixels);

    /// <summary>The share of the frame lit counted ground spanning more than <paramref name="cellPixels"/> per detail cell would take.</summary>
    public double ShareAt(double cellPixels) => ShareAt(cellPixels, _limits.DetailClasses);

    /// <summary>The share of the frame lit ground of <paramref name="classes"/> spanning more than <paramref name="cellPixels"/> would take.</summary>
    public double ShareAt(double cellPixels, IReadOnlyCollection<GroundClass> classes)
    {
        ArgumentNullException.ThrowIfNull(classes);
        int count = 0;
        for (int p = 0; p < CellPixels.Length; p++)
        {
            if (CellPixels[p] > cellPixels && classes.Contains(_buffer.Class[p]))
                count++;
        }
        return (double)count / CellPixels.Length;
    }
}

/// <summary>How much each score counts toward a framing's total (vista-plan §10 M1.14), recorded in the sidecar.</summary>
public sealed record CompositionWeights
{
    /// <summary>The sky's share of the frame.</summary>
    public double Sky { get; init; } = 0.06;

    /// <summary>The skyline near the upper third.</summary>
    public double Horizon { get; init; } = 0.08;

    /// <summary>The peak (or the range's summits) placed and in frame.</summary>
    public double Peak { get; init; } = 0.20;

    /// <summary>Foreground, middle ground and background, apart.</summary>
    public double Layers { get; init; } = 0.16;

    /// <summary>A framing mass at one edge.</summary>
    public double Framing { get; init; } = 0.08;

    /// <summary>Low ground in the lower third.</summary>
    public double LowerThird { get; init; } = 0.06;

    /// <summary>A diagonal leading line.</summary>
    public double LeadingLine { get; init; } = 0.06;

    /// <summary>Side or back light.</summary>
    public double Sun { get; init; } = 0.08;

    /// <summary>The subject in the sun.</summary>
    public double Light { get; init; } = 0.18;

    /// <summary>The squint test (finalists only).</summary>
    public double Squint { get; init; } = 0.10;

    /// <summary>Light background, darker middle ground, dark foreground (finalists only).</summary>
    public double Values { get; init; } = 0.08;

    /// <summary>The defaults.</summary>
    public static CompositionWeights Default { get; } = new();
}

/// <summary>
/// A framing's scores (vista-plan §4.8, §10 M1.14), each from 0 (bad) to 1 (good), and what they measured. The buffer's scores come
/// from <see cref="CompositionScorer.Score"/>; the squint test and the values by layer (<see cref="ValueStructure"/>) and the 4K mesh's
/// triangles only for the finalists, which are rendered.
/// </summary>
public sealed record CompositionScores
{
    /// <summary>The sky's share of the frame near a third (<see cref="SkyShare"/>).</summary>
    public double Sky { get; init; }

    /// <summary>The skyline's mean height near the upper third line.</summary>
    public double Horizon { get; init; }

    /// <summary>
    /// For a peak: the highest summit in frame off-centre near a third, below the top edge, standing above the rest of the skyline,
    /// the planted main peak and snow-capped. For a range: several summits along the skyline, the range across the frame, the highest
    /// near a third.
    /// </summary>
    public double Peak { get; init; }

    /// <summary>Clear depth layers: ridges standing in front of farther ground in most columns, three groups by distance, the nearest lowest in the frame.</summary>
    public double Layers { get; init; }

    /// <summary>A near mass at one edge of the frame and not across its middle.</summary>
    public double Framing { get; init; }

    /// <summary>Low ground (a valley floor) in the lower third, where water will lie once there is any (M3).</summary>
    public double LowerThird { get; init; }

    /// <summary>How much of the frame's edge structure below the skyline runs diagonally.</summary>
    public double LeadingLine { get; init; }

    /// <summary>Side or back light, weighed by how low the sun stands.</summary>
    public double Sun { get; init; }

    /// <summary>
    /// The subject catching the sun: the share of the ground just under the skyline around the summit that the sun reaches (null
    /// without a way to trace toward the sun, or with the sun below the horizon).
    /// </summary>
    public double? Light { get; init; }

    /// <summary>The squint test: the blurred thumbnail's values in a few distinct groups (finalists; null before).</summary>
    public double? Squint { get; init; }

    /// <summary>The values by depth layer: the background lightest, the foreground darkest (finalists; null before).</summary>
    public double? Values { get; init; }

    /// <summary>How well the squint thumbnail's values separate into three groups (<see cref="ValueStructure.Squint"/>; finalists).</summary>
    public double? SquintSeparation { get; init; }

    /// <summary>The spread between the squint thumbnail's darkest and lightest group (finalists).</summary>
    public double? SquintSpread { get; init; }

    /// <summary>The mean value of the foreground, the middle ground and the background, 0 to 1 (finalists).</summary>
    public IReadOnlyList<double>? LayerValues { get; init; }

    /// <summary>The share of the frame that is sky.</summary>
    public double SkyShare { get; init; }

    /// <summary>The skyline's mean row as a share of the frame's height from the top.</summary>
    public double SkylineRow { get; init; }

    /// <summary>Where the highest summit stands across the frame, a share of its width from the left.</summary>
    public double SummitX { get; init; }

    /// <summary>Where the highest summit stands down the frame, a share of its height from the top.</summary>
    public double SummitY { get; init; }

    /// <summary>The share of the frame the near field covers (ground but snow nearer than <see cref="CompositionLimits.NearFieldMetres"/>).</summary>
    public double NearShare { get; init; }

    /// <summary>The share of the frame ground nearer than <see cref="CompositionLimits.LensMetres"/> covers.</summary>
    public double LensShare { get; init; }

    /// <summary>The 4K mesh's triangles (finalists; null before).</summary>
    public long? Triangles { get; init; }

    /// <summary>
    /// The share of the frame lit bare ground covers that the final frame magnifies past the terrain's detail (the detail budget,
    /// <see cref="CompositionLimits.DetailCellPixels"/>; 0 without a way to trace toward the sun).
    /// </summary>
    public double DetailShare { get; init; }

    /// <summary>
    /// The weighted mean of the scores there are (<paramref name="weights"/>): the buffer's always, the squint test and the values once
    /// rendered.
    /// </summary>
    public double Total(CompositionWeights weights)
    {
        ArgumentNullException.ThrowIfNull(weights);
        double sum = weights.Sky * Sky + weights.Horizon * Horizon + weights.Peak * Peak + weights.Layers * Layers + weights.Framing * Framing
            + weights.LowerThird * LowerThird + weights.LeadingLine * LeadingLine + weights.Sun * Sun;
        double norm = weights.Sky + weights.Horizon + weights.Peak + weights.Layers + weights.Framing + weights.LowerThird + weights.LeadingLine + weights.Sun;
        if (Light is double light)
        {
            sum += weights.Light * light;
            norm += weights.Light;
        }
        if (Squint is double squint)
        {
            sum += weights.Squint * squint;
            norm += weights.Squint;
        }
        if (Values is double values)
        {
            sum += weights.Values * values;
            norm += weights.Values;
        }
        return norm > 0.0 ? sum / norm : 0.0;
    }

    /// <summary>The limit this framing breaks first, or null when it keeps them all.</summary>
    public string? Breaks(CompositionLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        if (LensShare > limits.LensMaxShare)
            return "lens";
        if (NearShare > limits.NearFieldMaxShare)
            return "nearField";
        if (DetailShare > limits.DetailMaxShare)
            return "detail";
        if (Triangles is long triangles && triangles > limits.TriangleLimit)
            return "triangles";
        return null;
    }
}

/// <summary>
/// The composition scores of a framing's buffer (vista-plan §4.8, §10 M1.14), after what the user's own paintings do consistently: clear
/// depth layers, a framing mass at one edge, the main peak off-centre near a third with low ground in the lower third, a diagonal leading
/// line; and the photographer's rules of §4.8: the skyline near a third, the sky's share, side or back light, the peaks in frame, nothing
/// near the lens. Every score is a pure function of the buffer and the context, so CI checks each on synthetic buffers.
/// </summary>
public static class CompositionScorer
{
    /// <summary>How much farther ground above a pixel must be for the step between them to be a ridge's silhouette.</summary>
    public const double SilhouetteRatio = 1.6;

    /// <summary>Ground lower than this above its valley floor counts as low ground, m.</summary>
    public const double LowGroundMetres = 60.0;

    /// <summary>
    /// Scores <paramref name="buffer"/> against <paramref name="context"/>, measuring the limits' shares too; with
    /// <paramref name="probe"/>, whether the subject catches the sun (<see cref="CompositionScores.Light"/>) and how much lit bare ground
    /// the final frame magnifies past the terrain's detail (<see cref="CompositionScores.DetailShare"/>) as well.
    /// </summary>
    public static CompositionScores Score(CompositionBuffer buffer, FramingContext context, CompositionLimits limits, ITerrainProbe? probe = null)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(limits);
        int w = buffer.Width, h = buffer.Height, n = w * h;

        int sky = 0, near = 0, lens = 0;
        for (int p = 0; p < n; p++)
        {
            if (buffer.Class[p] is GroundClass.Sky or GroundClass.None)
            {
                sky++;
                continue;
            }
            float d = buffer.Depth[p];
            if (d < limits.NearFieldMetres && buffer.Class[p] != GroundClass.Snow)
                near++;
            if (d < limits.LensMetres)
                lens++;
        }
        int[] skyline = Skyline(buffer);
        double skylineRow = skyline.Average() / h;
        double skyShare = (double)sky / n;

        (double[] centres, int[] cluster) = DepthLayers(buffer);
        (int summitX, int summitY) = Summit(skyline);
        return new CompositionScores
        {
            Sky = Bump(skyShare, 0.35, 0.16),
            Horizon = Bump(skylineRow, 1.0 / 3.0, 0.1),
            Peak = context.Intent == CompositionIntent.Range
                ? RangeScore(buffer, context, skyline, summitX, summitY)
                : PeakScore(buffer, context, skyline, summitX, summitY),
            Layers = LayersScore(buffer, skyline, cluster),
            Framing = FramingScore(buffer, centres, cluster),
            LowerThird = LowerThirdScore(buffer),
            LeadingLine = LeadingLineScore(buffer, skyline),
            Sun = SunScore(context.Camera, context.Sun, w, h),
            Light = probe is null || context.Sun.ElevationDegrees <= 0.0
                ? null
                : LightScore(buffer, context, skyline, context.Intent == CompositionIntent.Range ? w / 2 : summitX,
                    context.Intent == CompositionIntent.Range ? 0.5 : 0.1, probe),
            DetailShare = probe is null ? 0.0 : Detail(buffer, context.Camera, context.Sun, limits, probe).Share,
            SkyShare = skyShare,
            SkylineRow = skylineRow,
            SummitX = (summitX + 0.5) / w,
            SummitY = (double)summitY / h,
            NearShare = (double)near / n,
            LensShare = (double)lens / n,
        };
    }

    /// <summary>Each column's skyline: the first row from the top that shows the ground (the height when none does).</summary>
    public static int[] Skyline(CompositionBuffer buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        var skyline = new int[buffer.Width];
        for (int x = 0; x < buffer.Width; x++)
        {
            int y = 0;
            while (y < buffer.Height && !buffer.IsGround(x, y))
                y++;
            skyline[x] = y;
        }
        return skyline;
    }

    /// <summary>The highest point of the skyline: the middle of the leftmost run of columns at its highest row.</summary>
    public static (int X, int Y) Summit(int[] skyline)
    {
        ArgumentNullException.ThrowIfNull(skyline);
        int top = skyline.Min();
        int start = Array.IndexOf(skyline, top), end = start;
        while (end + 1 < skyline.Length && skyline[end + 1] == top)
            end++;
        return ((start + end) / 2, top);
    }

    /// <summary>
    /// The ground's depths in three groups (<see cref="KMeans3"/> on log₁₀ of the depth): the groups' centres, nearest first, and each
    /// pixel's group (−1 for the sky).
    /// </summary>
    public static (double[] Centres, int[] Cluster) DepthLayers(CompositionBuffer buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        int n = buffer.Width * buffer.Height;
        var logs = new List<double>(n);
        for (int p = 0; p < n; p++)
        {
            if (buffer.Class[p] is not (GroundClass.Sky or GroundClass.None))
                logs.Add(Math.Log10(Math.Max(buffer.Depth[p], 1f)));
        }
        var cluster = new int[n];
        Array.Fill(cluster, -1);
        if (logs.Count == 0)
            return ([0.0, 0.0, 0.0], cluster);
        (double[] centres, _, _) = KMeans3(logs);
        for (int p = 0; p < n; p++)
        {
            if (buffer.Class[p] is GroundClass.Sky or GroundClass.None)
                continue;
            cluster[p] = Nearest(centres, Math.Log10(Math.Max(buffer.Depth[p], 1f)));
        }
        return (centres, cluster);
    }

    /// <summary>
    /// A peak's placement: the highest summit in the frame (whichever it is: from most places a nearer summit stands higher than the
    /// planted main peak, and the picture is of the one that dominates it) off-centre near a third, its top some way below the frame's
    /// top edge (never cut by it), standing above the rest of the skyline, and white with snow just under the skyline; the planted main
    /// peak itself earns a little more.
    /// </summary>
    private static double PeakScore(CompositionBuffer buffer, FramingContext context, int[] skyline, int summitX, int summitY)
    {
        int w = buffer.Width, h = buffer.Height;
        if (summitY >= h)
            return 0.0;
        double third = Third((summitX + 0.5) / w);
        double top = summitY <= 0 ? 0.0 : Bump((double)summitY / h, 0.2, 0.12);
        double median = Median(skyline);
        double dominance = Clamp01((median - summitY) / h / 0.15);
        double main = IsMainPeak(buffer, context, summitX, summitY) ? 1.0 : 0.0;
        double snow = Clamp01(SnowUnderSkyline(buffer, skyline, summitX, 0.1) / 0.4);
        return third * top * (0.5 + 0.5 * dominance) * (0.85 + 0.15 * main) * (0.7 + 0.3 * snow);
    }

    /// <summary>
    /// A range's skyline: several summits along it (four or more score fully), the range across the frame (the skyline's ground at least
    /// half as far as the planted subject), and its highest summit near a third, below the top edge.
    /// </summary>
    private static double RangeScore(CompositionBuffer buffer, FramingContext context, int[] skyline, int summitX, int summitY)
    {
        int w = buffer.Width, h = buffer.Height;
        if (summitY >= h)
            return 0.0;
        int summits = Summits(skyline, h).Count;
        double count = Clamp01((summits - 1) / 3.0);
        int far = 0;
        for (int x = 0; x < w; x++)
        {
            if (skyline[x] < h && buffer.Depth[skyline[x] * w + x] >= 0.5 * context.SubjectDistanceMetres)
                far++;
        }
        double span = Clamp01(((double)far / w - 0.3) / 0.5);
        double placed = Third((summitX + 0.5) / w) * (summitY <= 0 ? 0.0 : Bump((double)summitY / h, 0.2, 0.12));
        double snow = Clamp01(SnowUnderSkyline(buffer, skyline, w / 2, 0.5) / 0.3);
        return (0.4 * count + 0.3 * span + 0.3 * placed) * (0.7 + 0.3 * snow);
    }

    /// <summary>
    /// The summits along a skyline (rows, top 0): its local highest points, each standing at least 3% of the frame's height above the
    /// saddles to the next higher summit on either side (the frame's edge counts as a saddle), and at least 5% of its width apart.
    /// </summary>
    public static List<int> Summits(int[] skyline, int height)
    {
        ArgumentNullException.ThrowIfNull(skyline);
        int w = skyline.Length;
        double prominence = 0.03 * height;
        int apart = Math.Max(1, (int)Math.Round(0.05 * w));
        var candidates = new List<int>();
        for (int x = 0; x < w; x++)
        {
            if (skyline[x] >= height)
                continue;
            bool highest = true;
            for (int k = Math.Max(0, x - apart); k <= Math.Min(w - 1, x + apart) && highest; k++)
                highest = skyline[k] > skyline[x] || (skyline[k] == skyline[x] && k >= x);
            if (highest)
                candidates.Add(x);
        }
        var summits = new List<int>();
        foreach (int x in candidates)
        {
            // The lowest point (largest row) on the way to the next higher summit, each side.
            int Saddle(int step)
            {
                int saddle = skyline[x];
                for (int k = x + step; k >= 0 && k < w; k += step)
                {
                    if (skyline[k] < skyline[x])
                        return saddle;
                    saddle = Math.Max(saddle, skyline[k]);
                }
                return saddle;
            }
            if (Math.Min(Saddle(-1), Saddle(1)) - skyline[x] >= prominence)
                summits.Add(x);
        }
        return summits;
    }

    /// <summary>
    /// Layers: ridges in front of farther ground (a silhouette is a step up the column to ground <see cref="SilhouetteRatio"/> times as far;
    /// two in a column make three layers), three groups by depth each holding at least 8% of the ground, and the nearest group lowest in
    /// the frame, the farthest highest.
    /// </summary>
    private static double LayersScore(CompositionBuffer buffer, int[] skyline, int[] cluster)
    {
        int w = buffer.Width, h = buffer.Height;
        double silhouettes = 0.0;
        for (int x = 0; x < w; x++)
        {
            int count = 0;
            bool inJump = false;
            for (int y = h - 1; y > skyline[x]; y--)
            {
                float below = buffer.Depth[y * w + x], above = buffer.Depth[(y - 1) * w + x];
                bool jump = buffer.IsGround(x, y - 1) && above > SilhouetteRatio * below;
                if (jump && !inJump)
                    count++;
                inJump = jump;
            }
            silhouettes += Math.Min(count, 2) / 2.0;
        }
        silhouettes /= w;

        var counts = new int[3];
        var rows = new double[3];
        int ground = 0;
        for (int p = 0; p < cluster.Length; p++)
        {
            if (cluster[p] < 0)
                continue;
            counts[cluster[p]]++;
            rows[cluster[p]] += p / w;
            ground++;
        }
        if (ground == 0)
            return 0.0;
        double shares = 1.0;
        for (int k = 0; k < 3; k++)
            shares *= Clamp01((double)counts[k] / ground / 0.08);
        double Row(int k) => counts[k] > 0 ? rows[k] / counts[k] : 0.0;
        double order = Row(0) > Row(1) && Row(1) > Row(2) ? 1.0 : Row(0) > Row(2) ? 0.5 : 0.0;
        return silhouettes * Math.Cbrt(shares) * (0.5 + 0.5 * order);
    }

    /// <summary>
    /// A framing mass: the nearest ground (the nearest depth group when it stands apart from the next, by a factor of 1.6 or more; else
    /// ground nearer than 30% of the ground's median depth) covering a good part of the columns at one edge (the outer fifth), little
    /// of the middle two fifths, and less of the other edge.
    /// </summary>
    private static double FramingScore(CompositionBuffer buffer, double[] centres, int[] cluster)
    {
        int w = buffer.Width, h = buffer.Height;
        bool apart = centres[1] - centres[0] >= Math.Log10(SilhouetteRatio);
        double median = MedianDepth(buffer);
        var mass = new double[w];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int p = y * w + x;
                if (cluster[p] < 0)
                    continue;
                if (apart ? cluster[p] == 0 : buffer.Depth[p] < 0.3 * median)
                    mass[x] += 1.0 / h;
            }
        }
        double Mean(double from, double to)
        {
            int a = (int)(from * w), b = Math.Max(a + 1, (int)(to * w));
            double sum = 0.0;
            for (int x = a; x < b; x++)
                sum += mass[x];
            return sum / (b - a);
        }
        double left = Mean(0.0, 0.2), right = Mean(0.8, 1.0), centre = Mean(0.3, 0.7);
        double edge = Math.Max(left, right), other = Math.Min(left, right);
        return Clamp01((edge - centre) / 0.3) * Clamp01((edge - other) / 0.25) * Clamp01(edge / 0.25);
    }

    /// <summary>
    /// Low ground in the lower third: of the lower third's pixels, the share showing ground less than <see cref="LowGroundMetres"/> above
    /// its valley floor and farther than 250 m (a floor seen across, not the ground at one's feet); 30% scores fully.
    /// </summary>
    private static double LowerThirdScore(CompositionBuffer buffer)
    {
        int w = buffer.Width, h = buffer.Height;
        int from = (2 * h + 2) / 3, low = 0, all = 0;
        for (int y = from; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int p = y * w + x;
                all++;
                if (buffer.IsGround(x, y) && buffer.Rise[p] < LowGroundMetres && buffer.Depth[p] > 250f)
                    low++;
            }
        }
        return all == 0 ? 0.0 : Clamp01((double)low / all / 0.3);
    }

    /// <summary>
    /// A diagonal leading line: the edges below the skyline (steps in log depth and in the normal's steepness, from Sobel gradients), and
    /// the share of their strength running 20°–70° from the horizontal either way; a fifth or less scores nothing, 55% fully. The
    /// skyline itself is left out: the peak's own flanks would count for every framing with a peak.
    /// </summary>
    private static double LeadingLineScore(CompositionBuffer buffer, int[] skyline)
    {
        int w = buffer.Width, h = buffer.Height;
        var logDepth = new double[w * h];
        var steep = new double[w * h];
        for (int p = 0; p < w * h; p++)
        {
            bool ground = buffer.Class[p] is not (GroundClass.Sky or GroundClass.None);
            logDepth[p] = ground ? Math.Log10(Math.Max(buffer.Depth[p], 1f)) : double.NaN;
            steep[p] = ground ? buffer.Normal[p * 3 + 1] : double.NaN;
        }
        double diagonal = 0.0, total = 0.0;
        void Add(double[] field, double scale, int x, int y)
        {
            double At(int dx, int dy) => field[(y + dy) * w + x + dx];
            double gx = (At(1, -1) + 2 * At(1, 0) + At(1, 1) - At(-1, -1) - 2 * At(-1, 0) - At(-1, 1)) / 8.0;
            double gy = (At(-1, 1) + 2 * At(0, 1) + At(1, 1) - At(-1, -1) - 2 * At(0, -1) - At(1, -1)) / 8.0;
            if (double.IsNaN(gx) || double.IsNaN(gy))
                return;
            double m = Math.Min(Math.Sqrt(gx * gx + gy * gy) * scale, 0.5);
            if (m < 0.01)
                return;
            // The edge runs across its gradient: its angle from the horizontal, folded into [0°, 90°].
            double angle = Math.Atan2(Math.Abs(gx), Math.Abs(gy)) * (180.0 / Math.PI);
            total += m;
            if (angle is >= 20.0 and <= 70.0)
                diagonal += m;
        }
        for (int y = 1; y < h - 1; y++)
        {
            for (int x = 1; x < w - 1; x++)
            {
                int ridge = Math.Max(skyline[x - 1], Math.Max(skyline[x], skyline[x + 1]));
                if (y <= ridge + 1)
                    continue;
                Add(logDepth, 1.0, x, y);
                Add(steep, 0.5, x, y);
            }
        }
        return total > 0.0 ? Clamp01((diagonal / total - 0.2) / 0.35) : 0.0;
    }

    /// <summary>
    /// The light's direction: side light (the sun 60°–120° from the heading) scores fully, back light from 40° (rim light; toward the
    /// sun only while the sun stays out of the frame) and front light less, flat light with the sun behind the camera least. The higher
    /// the sun, the less its direction matters: fully up to 15°, a quarter from 50°.
    /// </summary>
    public static double SunScore(CameraSpec camera, SunPosition sun, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(camera);
        double phi = Math.Abs(((sun.AzimuthDegrees - camera.YawDegrees) % 360.0 + 540.0) % 360.0 - 180.0);
        double light = Interpolate(phi, [0.0, 25.0, 40.0, 60.0, 120.0, 140.0, 165.0, 180.0], [0.5, 0.55, 0.85, 1.0, 1.0, 0.75, 0.45, 0.4]);
        // Into the sun: the disk in the frame (with a margin) flares and silhouettes everything.
        var rays = new CameraRays(camera, width, height);
        Vector3D toSun = SunDirection(sun);
        if (rays.Project(toSun) is { } at && at.X > -0.05 * width && at.X < 1.05 * width && at.Y > -0.05 * height && at.Y < 1.05 * height)
            light = 0.1;
        double weight = Interpolate(sun.ElevationDegrees, [15.0, 50.0], [1.0, 0.25]);
        return 1.0 - weight * (1.0 - light);
    }

    /// <summary>
    /// The subject in the light: of the ground in the band just under the skyline (12% of the frame's height deep) over the columns
    /// within <paramref name="halfWidth"/> of the frame's width around <paramref name="centre"/> (the summit's for a peak, the whole
    /// skyline for a range), the share the sun reaches (<see cref="ITerrainProbe"/>); half of it lit scores fully. A low sun behind the
    /// subject leaves the faces toward the camera in shadow, and the picture's focal point dark.
    /// </summary>
    private static double LightScore(CompositionBuffer buffer, FramingContext context, int[] skyline, int centre, double halfWidth, ITerrainProbe sunlight)
    {
        int w = buffer.Width, h = buffer.Height;
        int band = Math.Max(1, (int)Math.Round(0.12 * h));
        int reach = Math.Max(1, (int)Math.Round(halfWidth * w));
        int stride = halfWidth >= 0.5 ? 2 : 1;
        var rays = new CameraRays(context.Camera, w, h);
        Vector3D toSun = SunDirection(context.Sun);
        WorldPoint eye = context.Camera.Position;
        int lit = 0, ground = 0;
        for (int x = Math.Max(0, centre - reach); x <= Math.Min(w - 1, centre + reach); x += stride)
        {
            for (int y = skyline[x]; y < Math.Min(h, skyline[x] + band); y++)
            {
                if (!buffer.IsGround(x, y))
                    continue;
                int p = y * w + x;
                Vector3D d = rays.Direction(x, y);
                float depth = buffer.Depth[p];
                var normal = new Vector3D(buffer.Normal[p * 3], buffer.Normal[p * 3 + 1], buffer.Normal[p * 3 + 2]);
                ground++;
                if (sunlight.Lit(eye.X + d.X * depth, eye.Z + d.Z * depth, normal, toSun))
                    lit++;
            }
        }
        return ground == 0 ? 0.0 : Clamp01((double)lit / ground / 0.5);
    }

    /// <summary>
    /// The detail budget's look at a frame (vista-plan §10, M1.14's follow-up): for each pixel of ground on the world grid, how many
    /// pixels of the final frame (<see cref="CompositionLimits.FinalFrameWidth"/>) the terrain's finest structure there spans
    /// (<see cref="ITerrainProbe.DetailMetres"/> over the ground one pixel covers, its distance times the final pixel's angle), kept
    /// where the sun lights the ground and it spans more than <paramref name="fromCellPixels"/> (the limits' own by default; a lower one
    /// to read the shares at other thresholds too). It measures metres per pixel, not distance: a far face under a long lens is as
    /// magnified as a near one under a wide lens, and a larger final frame magnifies everything more. The sun below the horizon
    /// reveals no relief, and nothing counts.
    /// </summary>
    public static DetailMap Detail(CompositionBuffer buffer, CameraSpec camera, SunPosition sun, CompositionLimits limits, ITerrainProbe probe,
        double fromCellPixels = double.NaN)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentNullException.ThrowIfNull(camera);
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(probe);
        int w = buffer.Width, h = buffer.Height;
        var cellPixels = new float[w * h];
        var map = new DetailMap(buffer, cellPixels, limits);
        if (sun.ElevationDegrees <= 0.0)
            return map;
        double from = double.IsNaN(fromCellPixels) ? limits.DetailCellPixels : fromCellPixels;
        double angle = 2.0 * Math.Tan(0.5 * camera.VerticalFovRadians()) / limits.FinalFrameHeight;
        var rays = new CameraRays(camera, w, h);
        Vector3D toSun = SunDirection(sun);
        WorldPoint eye = camera.Position;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int p = y * w + x;
                if (!buffer.IsGround(x, y))
                    continue;
                float depth = buffer.Depth[p];
                Vector3D d = rays.Direction(x, y);
                double wx = eye.X + d.X * depth, wz = eye.Z + d.Z * depth;
                double detail = probe.DetailMetres(wx, wz);
                if (!(detail > 0.0))
                    continue;
                double spans = detail / (depth * angle);
                if (!(spans > from))
                    continue;
                var normal = new Vector3D(buffer.Normal[p * 3], buffer.Normal[p * 3 + 1], buffer.Normal[p * 3 + 2]);
                if (probe.Lit(wx, wz, normal, toSun))
                    cellPixels[p] = (float)spans;
            }
        }
        return map;
    }

    /// <summary>The unit direction toward the sun in world axes (+X east, +Y up, +Z north).</summary>
    public static Vector3D SunDirection(SunPosition sun)
    {
        (double sa, double ca) = Math.SinCos(sun.AzimuthDegrees * (Math.PI / 180.0));
        (double se, double ce) = Math.SinCos(sun.ElevationDegrees * (Math.PI / 180.0));
        return new Vector3D(sa * ce, se, ca * ce);
    }

    /// <summary>True when the skyline's summit at (<paramref name="x"/>, <paramref name="y"/>) lies within 1.5 km of the planted main peak (or 15% of its distance).</summary>
    private static bool IsMainPeak(CompositionBuffer buffer, FramingContext context, int x, int y)
    {
        float depth = buffer.Depth[y * buffer.Width + x];
        if (!float.IsFinite(depth))
            return false;
        var rays = new CameraRays(context.Camera, buffer.Width, buffer.Height);
        Vector3D d = rays.Direction(x, y);
        double px = context.Camera.Position.X + d.X * depth, pz = context.Camera.Position.Z + d.Z * depth;
        double dx = px - context.MainPeak.X, dz = pz - context.MainPeak.Z;
        return Math.Sqrt(dx * dx + dz * dz) <= Math.Max(1500.0, 0.15 * context.SubjectDistanceMetres);
    }

    /// <summary>The share of snow in the band just under the skyline (12% of the frame's height deep) over the columns within <paramref name="halfWidth"/> of the frame's width around <paramref name="centre"/>.</summary>
    private static double SnowUnderSkyline(CompositionBuffer buffer, int[] skyline, int centre, double halfWidth)
    {
        int w = buffer.Width, h = buffer.Height;
        int band = Math.Max(1, (int)Math.Round(0.12 * h));
        int reach = Math.Max(1, (int)Math.Round(halfWidth * w));
        int snow = 0, ground = 0;
        for (int x = Math.Max(0, centre - reach); x <= Math.Min(w - 1, centre + reach); x++)
        {
            for (int y = skyline[x]; y < Math.Min(h, skyline[x] + band); y++)
            {
                if (!buffer.IsGround(x, y))
                    continue;
                ground++;
                if (buffer.Class[y * w + x] == GroundClass.Snow)
                    snow++;
            }
        }
        return ground == 0 ? 0.0 : (double)snow / ground;
    }

    private static double MedianDepth(CompositionBuffer buffer)
    {
        var depths = new List<float>();
        for (int p = 0; p < buffer.Depth.Length; p++)
        {
            if (buffer.Class[p] is not (GroundClass.Sky or GroundClass.None))
                depths.Add(buffer.Depth[p]);
        }
        if (depths.Count == 0)
            return 0.0;
        depths.Sort();
        return depths[depths.Count / 2];
    }

    private static double Median(int[] values)
    {
        int[] sorted = [.. values];
        Array.Sort(sorted);
        return sorted[sorted.Length / 2];
    }

    /// <summary>
    /// Three groups of <paramref name="values"/> by 1-D k-means (Lloyd's, from the 1/6, 1/2 and 5/6 quantiles; deterministic): the
    /// groups' means, lowest first, their sizes, and how well they separate the values (the variance between the groups over the total
    /// variance: 1 for three tight groups, 0.89 for an even spread, lower for one muddy middle).
    /// </summary>
    public static (double[] Centres, int[] Counts, double Separation) KMeans3(IReadOnlyList<double> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count == 0)
            return ([0.0, 0.0, 0.0], [0, 0, 0], 0.0);
        double[] sorted = [.. values];
        Array.Sort(sorted);
        int n = sorted.Length;
        var prefix = new double[n + 1];
        var prefix2 = new double[n + 1];
        for (int i = 0; i < n; i++)
        {
            prefix[i + 1] = prefix[i] + sorted[i];
            prefix2[i + 1] = prefix2[i] + sorted[i] * sorted[i];
        }
        double[] centres = [sorted[n / 6], sorted[n / 2], sorted[Math.Min(n - 1, 5 * n / 6)]];
        int[] bounds = new int[4];
        for (int iteration = 0; iteration < 50; iteration++)
        {
            // With the values sorted, each group is a run: its bounds are where the midpoints between the centres fall.
            bounds[0] = 0;
            bounds[1] = LowerBound(sorted, 0.5 * (centres[0] + centres[1]));
            bounds[2] = Math.Max(bounds[1], LowerBound(sorted, 0.5 * (centres[1] + centres[2])));
            bounds[3] = n;
            bool moved = false;
            for (int k = 0; k < 3; k++)
            {
                int count = bounds[k + 1] - bounds[k];
                if (count == 0)
                    continue;
                double mean = (prefix[bounds[k + 1]] - prefix[bounds[k]]) / count;
                moved |= mean != centres[k];
                centres[k] = mean;
            }
            if (!moved)
                break;
        }
        double all = prefix[n] / n;
        double total = prefix2[n] / n - all * all;
        double between = 0.0;
        var counts = new int[3];
        for (int k = 0; k < 3; k++)
        {
            counts[k] = bounds[k + 1] - bounds[k];
            between += counts[k] * (centres[k] - all) * (centres[k] - all);
        }
        between /= n;
        return (centres, counts, total > 1e-12 ? Math.Min(1.0, between / total) : 1.0);
    }

    private static int LowerBound(double[] sorted, double value)
    {
        int lo = 0, hi = sorted.Length;
        while (lo < hi)
        {
            int mid = (lo + hi) >>> 1;
            if (sorted[mid] < value)
                lo = mid + 1;
            else
                hi = mid;
        }
        return lo;
    }

    /// <summary>The index of the centre nearest <paramref name="value"/>.</summary>
    public static int Nearest(double[] centres, double value)
    {
        ArgumentNullException.ThrowIfNull(centres);
        int best = 0;
        for (int k = 1; k < centres.Length; k++)
        {
            if (Math.Abs(value - centres[k]) < Math.Abs(value - centres[best]))
                best = k;
        }
        return best;
    }

    /// <summary>1 at a third of the way across (either way), falling off over 8% of the frame.</summary>
    public static double Third(double share) => Math.Max(Bump(share, 1.0 / 3.0, 0.08), Bump(share, 2.0 / 3.0, 0.08));

    /// <summary>A Gaussian bump: 1 at <paramref name="centre"/>, e⁻¹ at <paramref name="width"/> from it.</summary>
    public static double Bump(double value, double centre, double width) => Math.Exp(-Math.Pow((value - centre) / width, 2.0));

    /// <summary><paramref name="value"/> clamped to [0, 1].</summary>
    public static double Clamp01(double value) => Math.Clamp(value, 0.0, 1.0);

    /// <summary>Piecewise linear through (<paramref name="xs"/>, <paramref name="ys"/>), flat past the ends.</summary>
    public static double Interpolate(double x, double[] xs, double[] ys)
    {
        ArgumentNullException.ThrowIfNull(xs);
        ArgumentNullException.ThrowIfNull(ys);
        if (x <= xs[0])
            return ys[0];
        for (int i = 1; i < xs.Length; i++)
        {
            if (x <= xs[i])
                return ys[i - 1] + (ys[i] - ys[i - 1]) * (x - xs[i - 1]) / (xs[i] - xs[i - 1]);
        }
        return ys[^1];
    }
}
