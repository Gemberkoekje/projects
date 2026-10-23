using Vista.Scene;

namespace Vista.Atmosphere;

/// <summary>
/// Aerial perspective (vista-plan §5.2, §10 M1.4): every segment of a path that ends on the terrain, camera or bounce,
/// carries the air between its ends — the layered, receding blue ridges. This is the C# reference of the shader's
/// <c>SegmentBetween</c> (<c>VistaIntegrator.hlsl</c>) and <c>AerialSegment</c> (<c>VistaAtmosphere.hlsli</c>);
/// <c>--gpu-selftest</c> probes the GPU's against it.
///
/// <para><b>The world bent onto the Earth.</b> A world point keeps its height above the ground sphere and sits on the
/// sphere around the camera's tangent point: at a horizontal distance <c>s</c> from the camera it has sunk by the curvature
/// drop <c>2R·sin²(s/2R)</c> (≈ 200 m at 50 km) and its vertical is tilted by <c>s/R</c> (<see cref="World.EarthFrame"/>). A
/// segment is the straight chord between two points of that frame, so the far end sits at exactly its own height — not 200 m
/// higher, where a flat ray through spherical air would put it. M1.4 bent the world for the air alone; since M1.9 the mesh
/// is built in the frame, so a segment's ends are frame points already.</para>
///
/// <para><b>The march</b> (chosen over the sky view's difference <c>L(a) − T·L(b)</c> by error and by cost; the numbers
/// are in the plan): 2 steps plus one per <see cref="StepLengthMetres"/>, at most <see cref="MaxSteps"/>, dense at the
/// segment's lower end. Extinction comes from the analytic densities; single scattering from the sun's transmittance in
/// the columns LUT, seen through the Earth's shadow with its penumbra (<see cref="AtmosphereLuts.SunLight"/>) but not a
/// mountain's; multiple scattering from the summed orders, whose phase-weighted radiance is smooth along a segment while
/// the densities that turn it into emission are not — so it is read at the two ends and blended, or once in the middle
/// for a segment that climbs or drops less than <see cref="SingleLookupSpanMetres"/>.</para>
/// </summary>
public static class AerialPerspective
{
    /// <summary>The most steps a segment takes.</summary>
    public const int MaxSteps = 8;

    /// <summary>A segment takes 2 steps, plus one per this many metres, up to <see cref="MaxSteps"/>.</summary>
    public const double StepLengthMetres = 1000.0;

    /// <summary>A segment that climbs or drops less than this reads the scattering orders once, in its middle.</summary>
    public const double SingleLookupSpanMetres = 250.0;

    /// <summary>A straight segment through the air, described from its start.</summary>
    /// <param name="Height">Height of the start above the ground sphere, m.</param>
    /// <param name="Mu">Cosine of the direction to the vertical at the start.</param>
    /// <param name="MuS">Cosine of the sun to the vertical at the start.</param>
    /// <param name="Nu">Cosine between the direction and the sun.</param>
    /// <param name="Length">Length, m.</param>
    public readonly record struct Segment(double Height, double Mu, double MuS, double Nu, double Length);

    /// <summary>The steps a segment of <paramref name="length"/> metres takes.</summary>
    public static int StepsFor(double length) => Math.Min(MaxSteps, 2 + (int)(length / StepLengthMetres));

    /// <summary>How far a point at height 0, <paramref name="distance"/> metres away along the ground, lies below the tangent plane: <c>2R·sin²(s/2R)</c>.</summary>
    public static double CurvatureDrop(double groundRadius, double distance)
    {
        double half = Math.Sin(0.5 * distance / groundRadius);
        return 2.0 * groundRadius * half * half;
    }

    /// <summary>
    /// The air between frame points <paramref name="a"/> and <paramref name="b"/> (<see cref="World.EarthFrame"/> on
    /// <paramref name="frame"/>'s ground sphere): the chord seen from <paramref name="a"/>, its height taken from its distance to
    /// the planet's centre (below the ground sphere counts as on it) and its vertical pointing away from it.
    /// </summary>
    public static Segment Between(AtmosphereFrame frame, Vector3D a, Vector3D b)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var earth = new World.EarthFrame(frame.GroundRadius, 0.0, 0.0);
        Vector3D up = earth.LocalUp(a);
        Vector3D chord = b - a;
        double length = chord.Length();
        Vector3D dir = length > 0.0 ? chord * (1.0 / length) : up;
        var sun = new Vector3D(frame.ToSun.X, frame.ToSun.Y, frame.ToSun.Z);
        return new Segment(
            Math.Max(earth.HeightAbove(a), 0.0),
            Math.Clamp(Vector3D.Dot(dir, up), -1.0, 1.0),
            Math.Clamp(Vector3D.Dot(sun, up), -1.0, 1.0),
            Math.Clamp(Vector3D.Dot(dir, sun), -1.0, 1.0),
            length);
    }

    /// <summary>The 4D domain point <paramref name="t"/> metres along <paramref name="segment"/> (the shader's <c>AirPointAt</c>).</summary>
    public static AtmosphereScattering.Sample PointAt(AtmosphereFrame frame, in Segment segment, double t)
    {
        ArgumentNullException.ThrowIfNull(frame);
        double rb = frame.GroundRadius;
        double r0 = rb + segment.Height;
        double h = Math.Max(AtmosphereLuts.AltitudeAlong(rb, segment.Height, segment.Mu, t), 0.0);
        double r = rb + h;
        double mu = Math.Clamp((r0 * segment.Mu + t) / r, -1.0, 1.0);
        double muS = Math.Clamp((r0 * segment.MuS + t * segment.Nu) / r, -1.0, 1.0);
        return new AtmosphereScattering.Sample(h, mu, muS, AtmosphereScattering.ClampNu(mu, muS, segment.Nu), AtmosphereLuts.HitsSphere(rb, h, mu));
    }

    /// <summary>
    /// The phase-weighted radiance of the summed orders (<paramref name="orders"/>, 2 channels) at a point, for a wavelength
    /// at table position <paramref name="position"/> (<see cref="Rendering.WavelengthTable.MsPositions"/>): each corner blended in
    /// wavelength, then the corners summed, as the shader's <c>LookupWeighted4</c> does. Zero below the tables' lowest sun.
    /// </summary>
    public static (double Rayleigh, double Mie) Weighted(ReadOnlySpan<float> orders, AtmosphereFrame frame, double position, in AtmosphereScattering.Sample sample)
    {
        if (sample.MuS < AtmosphereScattering.MuSMin)
            return (0.0, 0.0);
        AtmosphereScattering.Cell c = AtmosphereScattering.CellOf(frame, sample);
        int l0 = Math.Min((int)Math.Floor(position), AtmosphereScattering.MsWavelengths - 2);
        double f = position - l0;
        double rayleigh = 0.0, mie = 0.0;
        for (int corner = 0; corner < 16; corner++)
        {
            int bh = corner & 1, bm = (corner >> 1) & 1, bs = (corner >> 2) & 1, bn = (corner >> 3) & 1;
            double w = (bh == 1 ? c.Fh : 1.0 - c.Fh) * (bm == 1 ? c.Fmu : 1.0 - c.Fmu) * (bs == 1 ? c.Fmus : 1.0 - c.Fmus) * (bn == 1 ? c.Fnu : 1.0 - c.Fnu);
            int at = AtmosphereScattering.Index(c.H0 + bh, c.Mu0 + bm, c.MuS0 + bs, c.Nu0 + bn, l0, 2);
            rayleigh += w * (orders[at] + f * (orders[at + 2] - orders[at]));
            mie += w * (orders[at + 1] + f * (orders[at + 3] - orders[at + 1]));
        }
        return (rayleigh, mie);
    }

    /// <summary>
    /// The air along <paramref name="segment"/> at one wavelength (coefficients <paramref name="k"/>, table position
    /// <paramref name="position"/>): its transmittance, and the light it scatters toward the segment's start per unit solar
    /// irradiance at the top of the atmosphere. An empty <paramref name="orders"/> leaves out multiple scattering.
    /// </summary>
    public static (double Transmittance, double Inscatter) Integrate(ReadOnlySpan<float> columns, ReadOnlySpan<float> orders, AtmosphereFrame frame,
        in AtmosphereCoefficients k, double position, in Segment segment)
    {
        ArgumentNullException.ThrowIfNull(frame);
        AtmosphereModel model = frame.Model;
        double rb = frame.GroundRadius;
        double r0 = rb + segment.Height;
        double hEnd = AtmosphereLuts.AltitudeAlong(rb, segment.Height, segment.Mu, segment.Length);
        bool denseAtEnd = hEnd < segment.Height;
        int steps = StepsFor(segment.Length);

        (double R, double M) a0 = (0.0, 0.0), a1 = (0.0, 0.0);
        if (!orders.IsEmpty)
        {
            if (Math.Abs(hEnd - segment.Height) < SingleLookupSpanMetres)
            {
                a0 = Weighted(orders, frame, position, PointAt(frame, segment, 0.5 * segment.Length));
                a1 = a0;
            }
            else
            {
                a0 = Weighted(orders, frame, position, PointAt(frame, segment, 0.0));
                a1 = Weighted(orders, frame, position, PointAt(frame, segment, segment.Length));
            }
        }

        double phaseR = AirOptics.RayleighPhase(segment.Nu);
        double phaseM = AirOptics.HenyeyGreenstein(segment.Nu, model.AerosolAsymmetry);
        double transmittance = 1.0, inscatter = 0.0;
        for (int s = 0; s < steps; s++)
        {
            (double t, double dt) = AtmosphereLuts.Segment(segment.Length, s, steps, denseAtEnd);
            double hg = Math.Max(AtmosphereLuts.AltitudeAlong(rb, segment.Height, segment.Mu, t), 0.0);
            double muSp = (r0 * segment.MuS + t * segment.Nu) / (rb + hg);
            (_, double extinction, double sigmaR, double sigmaM) = AtmosphereLuts.Coefficients(model, k, frame.GroundAltitudeMetres + hg);
            double sun = AtmosphereLuts.SunLight(columns, frame, k, hg, muSp);
            double f = segment.Length > 0.0 ? t / segment.Length : 0.0;
            double emission = sun * (sigmaR * phaseR + sigmaM * phaseM)
                + sigmaR * (a0.R + f * (a1.R - a0.R)) + sigmaM * (a0.M + f * (a1.M - a0.M));
            inscatter += transmittance * AtmosphereLuts.StepIntegral(extinction, dt) * emission;
            transmittance *= Math.Exp(-extinction * dt);
        }
        return (transmittance, inscatter);
    }
}
