using System.Numerics;
using Vista.Atmosphere;
using Vista.Rendering;
using Vista.Scene;

namespace Vista.Output;

/// <summary>What the incident meter read (<see cref="IncidentMeter"/>).</summary>
/// <param name="CardElevationDegrees">The card's normal above the horizontal: the sun's elevation, or 0 while the sun is below the horizon.</param>
/// <param name="AltitudeMetres">Where the card stands: the camera's altitude above sea level (at least a metre above the ground sphere).</param>
/// <param name="IlluminanceY">The card's illuminance in the integrator's units: the ȳ-weighted mean spectral irradiance, W·m⁻²·nm⁻¹.</param>
/// <param name="IlluminanceLux">The same in lux.</param>
/// <param name="SunLux">The sun's part, lux: 0 in the Earth's shadow.</param>
/// <param name="SkyLux">The sky's part, lux (the ground sphere the sky view holds below the horizon included).</param>
public sealed record IncidentReading(double CardElevationDegrees, double AltitudeMetres, double IlluminanceY, double IlluminanceLux, double SunLux, double SkyLux);

/// <summary>
/// The incident meter (vista-plan §10 M1.10a), the PNG's default exposure since then: an 18% grey card where the camera stands,
/// facing the sun, exposed to middle grey. A Lambertian card of albedo ρ under illuminance E has radiance ρE/π, so putting the
/// 18% card at 0.18 scales the image by π/E whatever ρ is. The card is lit by the sun through the air (the Earth's shadow and its
/// penumbra included) and by the sky over its whole hemisphere, read from the camera's own sky view (slice 0) — the sky the
/// camera's rays see, and below the horizon the lit ground sphere it holds. The terrain is never in the card's way: the meter
/// reads the light of the place and the hour, not the framing, so a frame half in a valley's shadow is exposed as one in the sun.
///
/// <para>The card faces the sun's azimuth, tilted up to the sun's elevation, and stands vertical while the sun is below the
/// horizon: it never turns toward the ground. So the reading is continuous through sunset, where the sun's term fades with its
/// disk behind the ground sphere and only the sky's is left.</para>
///
/// <para>The GPU measures it (<c>CSIncidentMeter</c>) in items of four wavelengths: per zenith band of
/// <see cref="SkyDistribution"/>'s cells, the sum over the band's cells of the mean over their sub-samples of L·cos⁺(card, ω)
/// (<see cref="BandSky"/>), and the sun's irradiance on the card (<see cref="SunOnCard"/>); <see cref="FromKernel"/> weights the
/// bands by their cells' solid angle and adds them. The C# here is the reference of both.</para>
/// </summary>
public static class IncidentMeter
{
    /// <summary>Recorded in <see cref="ExposureInfo.Method"/>.</summary>
    public const string MethodName = "incident: an 18% grey card at the camera's altitude, facing the sun (never below the horizontal), lit by the sun through the air and by the sky view over its hemisphere, to 18% grey, plus the camera's EV; the terrain is not in its way";

    /// <summary>Recorded when the card has no light at all.</summary>
    public const string BlackMethodName = "incident: the card has no light, so only the camera's EV is applied";

    /// <summary>Incident-light meter calibration constant for a flat receptor, lux·s (ISO 2720).</summary>
    public const double Calibration = 250.0;

    /// <summary>Wavelengths per kernel item (a float4).</summary>
    public const int WavelengthsPerItem = 4;

    /// <summary>Items per group of four wavelengths: one per zenith band, then the sun.</summary>
    public const int ItemsPerGroup = SkyDistribution.ZenithBands + 1;

    /// <summary>Groups of four wavelengths (the last has two unused lanes).</summary>
    public const int Groups = (AtmosphereLuts.Wavelengths + WavelengthsPerItem - 1) / WavelengthsPerItem;

    /// <summary>Kernel items in all.</summary>
    public const int Items = Groups * ItemsPerGroup;

    /// <summary>Floats the kernel writes (four per item).</summary>
    public const int Floats = Items * WavelengthsPerItem;

    /// <summary>
    /// The card's normal for the sun toward <paramref name="toSun"/>: its cosine and sine to the vertical, the normal lying in the
    /// sun's vertical plane on the sun's side (relative azimuth 0). Its cosine is the sun's, never below 0.
    /// </summary>
    public static (double Cos, double Sin) Card(Vector3 toSun)
    {
        double length = Math.Sqrt((double)toSun.X * toSun.X + (double)toSun.Y * toSun.Y + (double)toSun.Z * toSun.Z);
        double cos = length > 0.0 ? Math.Clamp(toSun.Y / length, 0.0, 1.0) : 1.0;
        return (cos, Math.Sqrt(Math.Max(1.0 - cos * cos, 0.0)));
    }

    /// <summary>The card's elevation above the horizontal in degrees, for the sun toward <paramref name="toSun"/>.</summary>
    public static double CardElevationDegrees(Vector3 toSun) => Math.Asin(Card(toSun).Cos) * 180.0 / Math.PI;

    /// <summary>
    /// cos⁺ between the card (<paramref name="cardCos"/>, <paramref name="cardSin"/> from <see cref="Card"/>) and the direction at
    /// cos θ = <paramref name="cosTheta"/> to the vertical and relative azimuth <paramref name="phi"/> to the sun; 0 behind the card.
    /// </summary>
    public static double CardCosine(double cardCos, double cardSin, double cosTheta, double phi)
    {
        double sinTheta = Math.Sqrt(Math.Max(1.0 - cosTheta * cosTheta, 0.0));
        return Math.Max(cardSin * sinTheta * Math.Cos(phi) + cardCos * cosTheta, 0.0);
    }

    /// <summary>The solid angle of one cell of zenith band <paramref name="band"/>, both mirror halves of it.</summary>
    public static double CellSolidAngle(int band) => 2.0 * SkyDistribution.ColumnWidth * (SkyDistribution.EdgeCos(band) - SkyDistribution.EdgeCos(band + 1));

    /// <summary>
    /// The C# reference of a band item of <c>CSIncidentMeter</c>: for wavelength <paramref name="lambda"/>, the sum over band
    /// <paramref name="band"/>'s cells of the mean over their <see cref="SkyDistribution.SubSamples"/>² points of the camera's sky
    /// (slice 0 of <paramref name="skyView"/>, the haze's phase at the exact angle to the sun, times <paramref name="toa"/>, the
    /// solar irradiance above the air as uploaded) times cos⁺ to the card. Times <see cref="CellSolidAngle"/> it is the band's
    /// share of the irradiance.
    /// </summary>
    public static double BandSky(ReadOnlySpan<float> skyView, AtmosphereFrame frame, float toa, int lambda, int band)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (band < 0 || band >= SkyDistribution.ZenithBands)
            throw new ArgumentOutOfRangeException(nameof(band), band, "No such zenith band.");
        (double cardCos, double cardSin) = Card(frame.ToSun);
        double muS = frame.SunCosZenith;
        double sinS = Math.Sqrt(Math.Max(1.0 - muS * muS, 0.0));
        double g = frame.Model.AerosolAsymmetry;
        double sum = 0.0;
        for (int column = 0; column < SkyDistribution.AzimuthBands; column++)
        {
            for (int a = 0; a < SkyDistribution.SubSamples; a++)
            {
                for (int b = 0; b < SkyDistribution.SubSamples; b++)
                {
                    (double mu, double phi) = SkyDistribution.CellPoint(band, column, a, b);
                    double cosCard = CardCosine(cardCos, cardSin, mu, phi);
                    if (cosCard <= 0.0)
                        continue;
                    double sinTheta = Math.Sqrt(Math.Max(1.0 - mu * mu, 0.0));
                    double phase = AirOptics.HenyeyGreenstein(sinTheta * Math.Cos(phi) * sinS + mu * muS, g);
                    (double smooth, double mie) = AtmosphereLuts.SampleSkyView(skyView, frame, lambda, 0, mu, phi);
                    sum += (smooth + phase * mie) * cosCard;
                }
            }
        }
        return toa * sum / (SkyDistribution.SubSamples * SkyDistribution.SubSamples);
    }

    /// <summary>
    /// The C# reference of the sun item of <c>CSIncidentMeter</c>: <paramref name="toa"/> through the air from the card's altitude
    /// toward the sun (the visible part of its disk over the ground sphere, <see cref="AtmosphereLuts.SunLight"/>), times the
    /// cosine between the card and the sun's centre.
    /// </summary>
    public static double SunOnCard(ReadOnlySpan<float> columns, AtmosphereFrame frame, in AtmosphereCoefficients k, float toa)
    {
        ArgumentNullException.ThrowIfNull(frame);
        (double cardCos, double cardSin) = Card(frame.ToSun);
        double muS = frame.SunCosZenith;
        double cosCard = Math.Max(cardSin * Math.Sqrt(Math.Max(1.0 - muS * muS, 0.0)) + cardCos * muS, 0.0);
        double height = frame.ClampAltitude(frame.CameraAltitudeMetres) - frame.GroundAltitudeMetres;
        return toa * AtmosphereLuts.SunLight(columns, frame, k, height, muS) * cosCard;
    }

    /// <summary>
    /// The light on the card from what <c>CSIncidentMeter</c> wrote (<see cref="Floats"/> floats: item
    /// <c>group · ItemsPerGroup + band</c>, lane <c>λ mod 4</c> of group <c>λ / 4</c>, the sun after the bands): each band weighted by
    /// its cells' solid angle and summed in band order, in double, so the reading is the same every time.
    /// </summary>
    public static IncidentLight FromKernel(ReadOnlySpan<float> floats)
    {
        if (floats.Length != Floats)
            throw new ArgumentException($"Expected {Floats} floats, got {floats.Length}.", nameof(floats));
        var sun = new double[AtmosphereLuts.Wavelengths];
        var sky = new double[AtmosphereLuts.Wavelengths];
        for (int lambda = 0; lambda < AtmosphereLuts.Wavelengths; lambda++)
        {
            int first = lambda / WavelengthsPerItem * ItemsPerGroup;
            int lane = lambda % WavelengthsPerItem;
            double sum = 0.0;
            for (int band = 0; band < SkyDistribution.ZenithBands; band++)
                sum += CellSolidAngle(band) * floats[(first + band) * WavelengthsPerItem + lane];
            sky[lambda] = sum;
            sun[lambda] = floats[(first + SkyDistribution.ZenithBands) * WavelengthsPerItem + lane];
        }
        return new IncidentLight(sun, sky);
    }

    /// <summary>
    /// The ȳ-weighted mean of <paramref name="spectrum"/> (one value per hero wavelength) as the integrator weighs a pixel's
    /// samples, so a card irradiance comes out in the image's own Y units: (correction / count) · Σ ȳᵢ · Eᵢ.
    /// </summary>
    public static double Luminous(ReadOnlySpan<double> spectrum, IntegratorTables tables)
    {
        ArgumentNullException.ThrowIfNull(tables);
        if (spectrum.Length != tables.Count)
            throw new ArgumentException($"Expected {tables.Count} values, got {spectrum.Length}.", nameof(spectrum));
        double sum = 0.0;
        for (int i = 0; i < spectrum.Length; i++)
            sum += tables.Cie[i * 4 + 1] * spectrum[i];
        return sum * tables.DeterministicCorrection / tables.Count;
    }

    /// <summary>
    /// The exposure of a view whose card received <paramref name="light"/> in <paramref name="frame"/>, with
    /// <paramref name="compensationEv"/> stops on top: log₂(π / E) for the card's illuminance E in the image's Y units.
    /// </summary>
    public static ExposureInfo Measure(IncidentLight light, AtmosphereFrame frame, IntegratorTables tables, double compensationEv)
    {
        ArgumentNullException.ThrowIfNull(light);
        ArgumentNullException.ThrowIfNull(frame);
        double sun = Luminous(light.Sun, tables);
        double sky = Luminous(light.Sky, tables);
        double total = sun + sky;
        var reading = new IncidentReading(
            CardElevationDegrees(frame.ToSun),
            frame.ClampAltitude(frame.CameraAltitudeMetres),
            total,
            total * IntegratorTables.LuminancePerY,
            sun * IntegratorTables.LuminancePerY,
            sky * IntegratorTables.LuminancePerY);
        if (!double.IsFinite(total) || total <= 0.0)
            return new ExposureInfo(Metering.Incident, BlackMethodName, compensationEv, null, null, compensationEv) { Incident = reading };
        double metered = Math.Log2(Math.PI / total);
        return new ExposureInfo(Metering.Incident, MethodName, compensationEv, metered, Math.Log2(reading.IlluminanceLux * 100.0 / Calibration), metered + compensationEv)
        {
            Incident = reading,
        };
    }
}
