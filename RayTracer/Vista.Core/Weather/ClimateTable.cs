using Vista.Astronomy;

namespace Vista.Weather;

/// <summary>
/// The dropdown places' climate normals (vista-plan §3.5, §3.6; the user's decision of 2026-10-19: a hand-entered table, with a
/// source per place). Monthly means of the daily high and low, January first, °C, at the station nearest each place's spot that
/// publishes them, all for 1991–2020. NOAA publishes °F; those were converted and rounded to 0.1 °C. Three places have no
/// station of their own, and their stand-ins differ from the spot: Lofoten's is Skrova lighthouse, 70 km from Reine and a little
/// less oceanic; Þingvellir's own record ended in 1983, so Iceland's is Hjarðarland, an inland lowland station 40 km east; Glen
/// Coe's nearest is a summit station, so the Highlands' is Tulloch Bridge, inland in Glen Spean; and nothing near Torres del Paine
/// has normals (Puerto Natales' 30-year table is incomplete and inconsistent), so its stand-in is Punta Arenas airport, 230 km
/// south-south-east on the Strait of Magellan.
///
/// <para>Since M1.13 each place's monthly precipitation for 1991–2020 too, from its temperatures' station (gauge totals, not
/// corrected for the snow the gauges miss: <see cref="SnowCover"/> does that), but for Lofoten: Skrova publishes no precipitation,
/// so its precipitation is Leknes i Lofoten's, 43 km west in the middle of the islands and nearer Reine. Torres del Paine's stand-in,
/// Punta Arenas, is far drier (390 mm a year) than the Paine massif beside the Patagonian ice field.</para>
/// </summary>
public static class ClimateTable
{
    private static readonly (Place Place, ClimateNormals Normals)[] Table =
    [
        (Place.Yellowstone, new ClimateNormals("Old Faithful, WY (NOAA USC00486845)", 2240.6, "1991–2020",
            "NOAA NCEI U.S. Climate Normals 1991–2020 (°F converted)",
            [-2.1, -0.5, 3.7, 7.5, 12.9, 18.4, 24.4, 23.9, 18.2, 9.9, 1.8, -3.1],
            [-17.2, -16.7, -12.3, -7.2, -1.8, 1.5, 4.0, 2.7, -1.2, -5.6, -12.3, -17.3])
            {
                Precipitation = [52.8, 54.1, 56.9, 61.0, 68.1, 61.5, 31.8, 36.3, 43.2, 58.4, 56.4, 70.6],
                PrecipitationSource = "Old Faithful, WY (NOAA USC00486845), NOAA NCEI U.S. Climate Normals 1991–2020 (inches converted)",
            }),
        (Place.Banff, new ClimateNormals("Banff (ECCC 3050519)", 1396.9, "1991–2020",
            "Environment and Climate Change Canada, Canadian Climate Normals 1991–2020",
            [-3.5, -0.5, 3.7, 9.0, 14.3, 17.9, 22.5, 22.1, 16.7, 9.1, 0.6, -4.6],
            [-13.5, -12.6, -8.2, -3.5, 0.5, 4.4, 6.8, 5.8, 2.2, -2.4, -8.2, -13.4])
            {
                Precipitation = [18.8, 18.6, 29.3, 39.2, 50.0, 73.3, 46.9, 46.9, 44.9, 35.8, 27.8, 22.5],
                PrecipitationSource = "Banff (ECCC 3050519), Canadian Climate Normals 1991–2020, total precipitation",
            }),
        (Place.Yosemite, new ClimateNormals("Yosemite Park HQ, CA (NOAA USC00049855)", 1224.7, "1991–2020",
            "NOAA NCEI U.S. Climate Normals 1991–2020 (°F converted)",
            [8.6, 10.7, 13.7, 17.3, 21.4, 26.9, 31.8, 31.7, 28.3, 21.6, 13.3, 7.7],
            [-1.9, -1.0, 0.8, 3.1, 6.9, 10.6, 14.1, 13.8, 10.8, 5.2, 0.3, -2.3])
            {
                Precipitation = [177.3, 164.8, 138.9, 80.5, 48.8, 11.7, 7.4, 4.1, 10.2, 39.6, 102.9, 142.2],
                PrecipitationSource = "Yosemite Park HQ, CA (NOAA USC00049855), NOAA NCEI U.S. Climate Normals 1991–2020 (inches converted)",
            }),
        (Place.Lofoten, new ClimateNormals("Skrova fyr (Met Norway, WMO 01160)", 11.0, "1991–2020",
            "Met Norway, WMO climatological standard normals 1991–2020 (NOAA NCEI archive)",
            [2.4, 1.6, 2.2, 4.8, 9.0, 13.0, 16.1, 15.4, 12.0, 7.8, 5.4, 3.6],
            [-1.0, -1.6, -0.9, 1.4, 4.9, 8.6, 11.5, 11.5, 8.8, 4.9, 2.2, 0.3])
            {
                Precipitation = [152.7, 135.7, 126.5, 85.5, 61.0, 52.7, 50.6, 67.2, 123.1, 150.1, 154.3, 169.8],
                PrecipitationSource = "Leknes i Lofoten (Met Norway SN 85540), WMO climatological standard normals 1991–2020 (NOAA NCEI archive); Skrova publishes none",
                PrecipitationAltitudeMetres = 13.0,
            }),
        (Place.Iceland, new ClimateNormals("Hjarðarland (Icelandic Met Office, WMO 04042)", 89.0, "1991–2020",
            "Icelandic Met Office, WMO climatological standard normals 1991–2020 (NOAA NCEI archive)",
            [1.8, 1.9, 2.9, 6.5, 10.9, 14.7, 16.5, 15.3, 11.6, 6.8, 3.2, 1.9],
            [-4.3, -4.2, -3.5, -0.7, 2.2, 5.8, 7.8, 7.0, 4.5, 0.6, -2.6, -4.4])
            {
                Precipitation = [130.8, 130.7, 127.5, 84.4, 82.5, 78.2, 88.9, 104.1, 147.1, 120.3, 112.7, 129.2],
                PrecipitationSource = "Hjarðarland (Icelandic Met Office, WMO 04042), WMO climatological standard normals 1991–2020 (NOAA NCEI archive)",
            }),
        (Place.ScottishHighlands, new ClimateNormals("Tulloch Bridge (Met Office, WMO 03047)", 249.0, "1991–2020",
            "Met Office UK climate averages 1991–2020 (as rounded in its WMO submission)",
            [5.7, 6.0, 7.6, 10.6, 14.0, 16.0, 17.4, 17.0, 14.9, 11.2, 8.0, 5.9],
            [-0.1, -0.2, 0.9, 2.4, 4.6, 7.7, 9.5, 9.2, 7.5, 4.8, 2.2, -0.5])
            {
                Precipitation = [250.0, 186.6, 154.0, 103.3, 95.3, 88.1, 94.2, 108.3, 129.7, 191.2, 203.4, 222.8],
                PrecipitationSource = "Tulloch Bridge (Met Office, WMO 03047), Met Office UK climate averages 1991–2020",
            }),
        (Place.Zermatt, new ClimateNormals("Zermatt (MeteoSwiss ZER, WMO 06748)", 1638.0, "1991–2020",
            "MeteoSwiss, WMO climatological standard normals 1991–2020 (NOAA NCEI archive)",
            [0.8, 1.7, 5.7, 9.8, 13.9, 17.8, 20.0, 19.6, 15.4, 11.2, 5.1, 1.5],
            [-7.4, -7.3, -4.0, -0.7, 3.2, 6.6, 8.3, 8.3, 5.0, 1.4, -3.1, -6.3])
            {
                Precipitation = [42.2, 29.7, 33.3, 39.9, 77.4, 69.9, 58.9, 68.4, 51.2, 57.0, 65.2, 47.3],
                PrecipitationSource = "Zermatt (MeteoSwiss ZER, WMO 06748), MeteoSwiss climate normals 1991–2020",
            }),
        (Place.Dolomites, new ClimateNormals("Cortina d'Ampezzo, Gilardon (ARPAV)", 1216.0, "1991–2020",
            "ISPRA, Valori climatici normali in Italia (SCIA) 1991–2020",
            [3.8, 4.7, 8.0, 11.3, 15.7, 19.8, 21.9, 21.5, 16.9, 12.4, 7.0, 3.9],
            [-5.3, -4.9, -2.2, 1.2, 5.3, 8.6, 10.3, 10.4, 6.8, 3.3, -0.7, -4.3])
            {
                Precipitation = [47.7, 40.7, 54.4, 74.2, 112.9, 115.1, 126.8, 142.7, 106.6, 120.6, 131.6, 62.3],
                PrecipitationSource = "Cortina d'Ampezzo, Gilardon (ARPAV), ISPRA Valori climatici normali in Italia (SCIA) 1991–2020",
            }),
        (Place.TorresDelPaine, new ClimateNormals("Punta Arenas airport (DMC 520006, WMO 85934)", 36.0, "1991–2020",
            "Dirección Meteorológica de Chile, WMO climatological standard normals 1991–2020 (NOAA NCEI archive)",
            [15.3, 14.8, 13.0, 10.1, 6.9, 4.3, 4.2, 5.7, 8.3, 10.8, 12.9, 14.3],
            [6.9, 6.6, 5.4, 3.5, 1.5, -0.5, -0.7, 0.1, 1.4, 2.7, 4.5, 5.8])
            {
                Precipitation = [38.1, 31.5, 42.9, 45.1, 36.9, 31.3, 30.9, 29.5, 24.5, 24.6, 23.1, 31.8],
                PrecipitationSource = "Punta Arenas airport (DMC 520006, WMO 85934), WMO climatological standard normals 1991–2020 (NOAA NCEI archive)",
            }),
        (Place.Fiordland, new ClimateNormals("Milford Sound AWS (NIWA 18309)", 3.0, "1991–2020",
            "NIWA climate normals 1991–2020",
            [19.5, 20.0, 18.5, 15.9, 12.5, 9.3, 9.2, 11.2, 13.2, 15.0, 16.4, 18.5],
            [10.7, 10.7, 9.2, 7.0, 4.9, 2.3, 1.6, 2.7, 4.1, 5.8, 7.6, 9.7])
            {
                Precipitation = [667.2, 466.6, 571.3, 528.2, 645.1, 440.4, 468.0, 457.0, 541.3, 617.2, 557.7, 585.1],
                PrecipitationSource = "Milford Sound AWS (NIWA 18309), NIWA mean monthly rainfall 1991–2020 (from at least 5 complete years)",
            }),
        (Place.Hokkaido, new ClimateNormals("Furano (JMA AMeDAS 0021)", 174.0, "1991–2020",
            "Japan Meteorological Agency, normals 1991–2020",
            [-3.7, -2.3, 2.7, 10.9, 18.4, 22.9, 26.2, 26.3, 21.9, 14.9, 6.4, -1.2],
            [-14.4, -14.0, -7.8, -0.2, 6.0, 11.5, 16.0, 16.3, 11.1, 4.1, -2.1, -10.2])
            {
                Precipitation = [44.3, 36.5, 49.8, 55.4, 67.5, 64.5, 116.0, 168.6, 147.3, 106.8, 104.2, 71.2],
                PrecipitationSource = "Furano (JMA AMeDAS 0021), Japan Meteorological Agency normals 1991–2020",
            }),
    ];

    /// <summary>Every dropdown place's normals, in catalogue order.</summary>
    public static IReadOnlyList<(Place Place, ClimateNormals Normals)> All => Table;

    /// <summary>The normals of <paramref name="place"/>; <see cref="Place.None"/> gives <see cref="Places.Default"/>'s.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="place"/> has no normals.</exception>
    public static ClimateNormals For(Place place)
    {
        Place key = place == Place.None ? Places.Default : place;
        foreach ((Place p, ClimateNormals normals) in Table)
        {
            if (p == key)
                return normals;
        }
        throw new ArgumentOutOfRangeException(nameof(place), place, "No climate normals for this place.");
    }
}
