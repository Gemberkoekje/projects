namespace Vista.World;

/// <summary>
/// The measures that decide M1.11a (vista-plan §10): whether a world's valleys branch and join like real drainage. A channel is
/// a cell whose upstream area reaches <see cref="ChannelAreaKm2"/>; a lake is a cell the depression filling raised by more
/// than <see cref="LakeDepthMetres"/> (<see cref="DrainageMap.FillDepth"/>), a hollow the terrain does not drain; a closed basin
/// is one filled deeper than <see cref="BasinDepthMetres"/>, which tells a basin the ground never drained from a pool behind a
/// bar of sediment. Every share is of the whole grid.
/// </summary>
public sealed record DrainageStats
{
    /// <summary>The upstream area from which a cell counts as a channel, in km².</summary>
    public required double ChannelAreaKm2 { get; init; }

    /// <summary>The fill depth from which a cell counts as a lake, in metres.</summary>
    public required double LakeDepthMetres { get; init; }

    /// <summary>The fill depth from which a cell counts as a closed basin, in metres.</summary>
    public required double BasinDepthMetres { get; init; }

    /// <summary>The share of the grid that is channel (lakes included).</summary>
    public required double ChannelShare { get; init; }

    /// <summary>
    /// The longest path through channel cells that crosses no lake, in km: how far the terrain itself carries water. A lake
    /// ends a channel and the water leaving it starts a new one.
    /// </summary>
    public required double LongestChannelKm { get; init; }

    /// <summary>The longest path through channel cells that crosses no closed basin (shallower lakes included), in km.</summary>
    public required double LongestOpenChannelKm { get; init; }

    /// <summary>The longest path through channel cells, lakes included, in km: the main stem of the largest river system.</summary>
    public required double LongestRouteKm { get; init; }

    /// <summary>The share of the grid whose water reaches a channel before it reaches a lake or the grid's edge.</summary>
    public required double DrainsToChannels { get; init; }

    /// <summary>The share of the grid that is lake (filled deeper than <see cref="LakeDepthMetres"/>).</summary>
    public required double LakeShare { get; init; }

    /// <summary>The share of the grid in closed basins (filled deeper than <see cref="BasinDepthMetres"/>).</summary>
    public required double BasinShare { get; init; }

    /// <summary>The share of the grid in the largest catchment (the land that leaves through one outlet).</summary>
    public required double LargestBasinShare { get; init; }

    /// <summary>
    /// Streams per Strahler order over the channel network, lakes included: index 0 holds the order-1 streams (from a channel
    /// head to the first confluence with another), index 1 the order-2 streams, and so on.
    /// </summary>
    public required IReadOnlyList<int> Streams { get; init; }

    /// <summary>The highest Strahler order.</summary>
    public int MaxOrder => Streams.Count;

    /// <summary>
    /// The geometric mean of the bifurcation ratios <c>N(k) / N(k + 1)</c> over the orders (natural networks: 3–5); 0 with fewer
    /// than two orders.
    /// </summary>
    public double BifurcationRatio
    {
        get
        {
            if (Streams.Count < 2)
                return 0.0;
            double logSum = 0.0;
            for (int k = 0; k + 1 < Streams.Count; k++)
                logSum += Math.Log(Streams[k] / (double)Streams[k + 1]);
            return Math.Exp(logSum / (Streams.Count - 1));
        }
    }

    /// <summary>Measures <paramref name="map"/>.</summary>
    public static DrainageStats Measure(DrainageMap map, double channelAreaKm2 = 1.0, double lakeDepthMetres = 1.0, double basinDepthMetres = 10.0)
    {
        ArgumentNullException.ThrowIfNull(map);
        int cells = map.Size * map.Size;
        int threshold = (int)Math.Ceiling(channelAreaKm2 * 1e6 / map.CellArea);
        bool Channel(int c) => map.Upstream[c] >= threshold;
        bool Lake(int c) => map.FillDepth[c] > lakeDepthMetres;
        bool Basin(int c) => map.FillDepth[c] > basinDepthMetres;

        // Downstream: the path lengths through channels (with and without lakes), and the Strahler orders. Each cell is
        // complete when its turn comes, because everything upstream of it came first.
        var dry = new double[cells];
        var open = new double[cells];
        var route = new double[cells];
        var highestIn = new int[cells];
        var atHighest = new int[cells];
        var streams = new List<int>();
        int channels = 0, lakes = 0, basins = 0, largest = 0;
        double longestDry = 0.0, longestOpen = 0.0, longestRoute = 0.0;
        foreach (int c in map.Order)
        {
            if (Lake(c))
                lakes++;
            if (Basin(c))
                basins++;
            int r = map.Receiver(c);
            if (r < 0)
                largest = Math.Max(largest, map.Upstream[c]);
            if (!Channel(c))
                continue;
            channels++;
            longestRoute = Math.Max(longestRoute, route[c]);
            if (!Lake(c))
                longestDry = Math.Max(longestDry, dry[c]);
            if (!Basin(c))
                longestOpen = Math.Max(longestOpen, open[c]);
            // A channel head starts at order 1; two streams of the same highest order joining start the next order.
            bool starts = highestIn[c] == 0 || atHighest[c] >= 2;
            int order = starts ? highestIn[c] + 1 : highestIn[c];
            if (starts)
            {
                while (streams.Count < order)
                    streams.Add(0);
                streams[order - 1]++;
            }
            if (r < 0)
                continue;
            double step = map.StepLength(c);
            route[r] = Math.Max(route[r], route[c] + step);
            if (!Lake(c) && !Lake(r))
                dry[r] = Math.Max(dry[r], dry[c] + step);
            if (!Basin(c) && !Basin(r))
                open[r] = Math.Max(open[r], open[c] + step);
            if (order > highestIn[r])
                (highestIn[r], atHighest[r]) = (order, 1);
            else if (order == highestIn[r])
                atHighest[r]++;
        }

        // Upstream: whether each cell's water reaches a channel before a lake or the edge (its receiver is settled first).
        var reaches = new bool[cells];
        int draining = 0;
        for (int k = cells - 1; k >= 0; k--)
        {
            int c = map.Order[k];
            int r = map.Receiver(c);
            reaches[c] = !Lake(c) && (Channel(c) || (r >= 0 && reaches[r]));
            if (reaches[c])
                draining++;
        }

        return new DrainageStats
        {
            ChannelAreaKm2 = channelAreaKm2,
            LakeDepthMetres = lakeDepthMetres,
            BasinDepthMetres = basinDepthMetres,
            ChannelShare = channels / (double)cells,
            LongestChannelKm = longestDry / 1000.0,
            LongestOpenChannelKm = longestOpen / 1000.0,
            LongestRouteKm = longestRoute / 1000.0,
            DrainsToChannels = draining / (double)cells,
            LakeShare = lakes / (double)cells,
            BasinShare = basins / (double)cells,
            LargestBasinShare = largest / (double)cells,
            Streams = streams,
        };
    }
}
