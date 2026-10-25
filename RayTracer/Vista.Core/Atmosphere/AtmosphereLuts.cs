namespace Vista.Atmosphere;

/// <summary>
/// The C# reference of the atmosphere LUTs (vista-plan §5.2, §9: a small reference and a parity test for shared
/// shader maths): their sizes, texel mappings, samplers and integrands, per wavelength of a render's 50, in the
/// style of Hillaire 2020 ("A Scalable and Production Ready Sky and Atmosphere Rendering Technique") with the
/// multiple scattering of Bruneton 2017 (<see cref="AtmosphereScattering"/>). <c>Shaders/VistaAtmosphere.hlsli</c>
/// computes exactly these in float; <c>--gpu-selftest</c> checks texels of each GPU LUT against these functions
/// given the same input LUTs.
///
/// <para>The tables, built in this order because each reads the ones before it:</para>
/// <list type="number">
///   <item><b>Columns</b> (<see cref="ColumnsTexel"/>): the density columns from a point to the top of the
///   atmosphere, by altitude and view angle, on Bruneton's horizon-resolving map. They do not depend on the
///   wavelength, so one float4 per texel serves all 50: transmittance is <c>exp(−Σ coefficient · column)</c>.
///   This is Hillaire's transmittance LUT, wavelength-free.</item>
///   <item><b>Scattering orders</b> (<see cref="AtmosphereScattering"/>): 4D tables of the light that has scattered
///   twice or more, built once per atmosphere and ground altitude.</item>
///   <item><b>Ground irradiance</b> (<see cref="GroundIrradianceEntry"/>): the sky's irradiance on the ground
///   sphere by sun angle, for the land beyond the tile.</item>
///   <item><b>Sky view</b> (<see cref="SkyViewTexel"/>): the sky per direction for one sun, in slices of
///   altitude. Each texel holds two numbers: the light without the haze's phase (Rayleigh with its phase, the
///   multiple scattering, and the ground below the horizon), and the haze's single scattering <i>without</i>
///   its phase, which the lookup multiplies by the Henyey–Greenstein phase of the exact angle to the sun. The
///   aureole is sharper than any texel, so it must not be baked in.</item>
/// </list>
/// Every LUT value is per unit solar irradiance at the top of the atmosphere; the integrator multiplies by
/// <see cref="SolarSpectrum.Irradiance"/>. Texels are vertex-centred: texel <c>i</c> of <c>n</c> sits at
/// <c>i/(n − 1)</c>, so the first and last sit exactly on the domain's edges.
/// </summary>
public static class AtmosphereLuts
{
    /// <summary>Wavelengths per table (the hero cycle of a <see cref="Rendering.WavelengthTable"/>).</summary>
    public const int Wavelengths = 50;

    /// <summary>Columns LUT: view-angle texels.</summary>
    public const int ColumnsMuSize = 256;

    /// <summary>Columns LUT: altitude texels (from sea level to the top).</summary>
    public const int ColumnsAltitudeSize = 256;

    /// <summary>Midpoint steps per columns texel (256 lost 0.4% of the haze's column straight up).</summary>
    public const int ColumnSteps = 1024;

    /// <summary>Ground-irradiance entries, on the 4D tables' sun map (<see cref="GroundIrradianceMuS"/>).</summary>
    public const int GroundIrradianceSize = 128;

    /// <summary>Directions per ground-irradiance entry, per side of a cosine-weighted 32 × 32 grid.</summary>
    public const int GroundIrradianceDirectionsPerSide = 32;

    /// <summary>Sky-view LUT: azimuth columns, 0 (toward the sun) to π (away from it).</summary>
    public const int SkyAzimuthSize = 64;

    /// <summary>Sky-view LUT: rows from the zenith down to the horizon.</summary>
    public const int SkyRowsAbove = 64;

    /// <summary>Sky-view LUT: rows from the horizon down to the nadir (the ground sphere).</summary>
    public const int SkyRowsBelow = 32;

    /// <summary>Sky-view LUT: rows per slice.</summary>
    public const int SkyRows = SkyRowsAbove + SkyRowsBelow;

    /// <summary>Sky-view slices over the terrain's altitudes (slice 0 is the camera's).</summary>
    public const int SkyTerrainSlices = 12;

    /// <summary>Sky-view slices in all.</summary>
    public const int SkySlices = 1 + SkyTerrainSlices;

    /// <summary>Steps along each sky-view (and ground-irradiance) ray.</summary>
    public const int SkySteps = 128;

    /// <summary>Floats in the columns LUT (four per texel: Rayleigh, haze, ozone, 0).</summary>
    public const int ColumnsFloats = ColumnsMuSize * ColumnsAltitudeSize * 4;

    /// <summary>Floats per channel of a 4D scattering table (<see cref="AtmosphereScattering"/>).</summary>
    public const int ScatterFloats = AtmosphereScattering.FloatsPerChannel;

    /// <summary>Floats in the ground-irradiance table (one per entry and wavelength).</summary>
    public const int GroundIrradianceFloats = GroundIrradianceSize * Wavelengths;

    /// <summary>Floats in the sky-view LUT (two per texel and wavelength).</summary>
    public const int SkyViewFloats = SkySlices * SkyRows * SkyAzimuthSize * Wavelengths * 2;

    // ── Geometry on spheres (every formula in the stable form the float shader needs) ───────────────────

    /// <summary>
    /// Altitude above a sphere of radius <paramref name="radius"/> of the point <paramref name="t"/> metres along a
    /// ray from altitude <paramref name="h0"/> whose direction makes <c>cos = mu</c> with the vertical there.
    /// <c>r(t)² − R² = h₀(2R + h₀) + t(t + 2r₀μ)</c>, divided by <c>R + r(t)</c>: no cancellation of radii.
    /// </summary>
    public static double AltitudeAlong(double radius, double h0, double mu, double t)
    {
        double q = h0 * (2.0 * radius + h0) + t * (t + 2.0 * (radius + h0) * mu);
        return q / (radius + Math.Sqrt(Math.Max(radius * radius + q, 0.0)));
    }

    /// <summary>True when a ray from altitude <paramref name="h"/> above a sphere of radius <paramref name="radius"/>, at <c>cos = mu</c> to the vertical, hits it.</summary>
    public static bool HitsSphere(double radius, double h, double mu)
    {
        double rmu = (radius + h) * mu;
        return mu < 0.0 && rmu * rmu >= h * (2.0 * radius + h);
    }

    /// <summary>
    /// Distance along that ray to the sphere below (call only when <see cref="HitsSphere"/>): the near root in the
    /// form <c>h(2R + h)/(−rμ + √D)</c>, which stays exact for grazing rays.
    /// </summary>
    public static double DistanceToSphereBelow(double radius, double h, double mu)
    {
        double rmu = (radius + h) * mu;
        double c = h * (2.0 * radius + h);
        double denominator = -rmu + Math.Sqrt(Math.Max(rmu * rmu - c, 0.0));
        return denominator > 0.0 ? c / denominator : 0.0;
    }

    /// <summary>
    /// Distance from altitude <paramref name="h"/> above a sphere of radius <paramref name="radius"/> to the shell
    /// <paramref name="hTop"/> above it, along <c>cos = mu</c>; upward rays use <c>gap/(rμ + √D)</c>, where the
    /// usual <c>−rμ + √D</c> would subtract two radii.
    /// </summary>
    public static double DistanceToShellAbove(double radius, double h, double hTop, double mu)
    {
        double rmu = (radius + h) * mu;
        double gap = (hTop - h) * (2.0 * radius + hTop + h); // R_t² − r²
        double root = Math.Sqrt(Math.Max(gap + rmu * rmu, 0.0));
        double d = rmu >= 0.0 ? (rmu + root > 0.0 ? gap / (rmu + root) : 0.0) : root - rmu;
        return Math.Max(d, 0.0);
    }

    // ── Columns LUT ────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The altitude above sea level and view cosine of columns texel coordinates <paramref name="xMu"/>,
    /// <paramref name="xH"/> in [0, 1] (Bruneton 2017's map: <c>x_h = ρ/H</c> with <c>ρ</c> the distance to the
    /// sea-level horizon, <c>x_μ</c> the distance to the top between its least and greatest, so the horizon is
    /// <c>x_μ = 1</c> at every altitude and <c>x_μ = 0</c> the zenith).
    /// </summary>
    public static (double Altitude, double Mu) ColumnsCoordinatesToAltitudeMu(AtmosphereModel model, double xMu, double xH)
    {
        ArgumentNullException.ThrowIfNull(model);
        double radius = model.PlanetRadiusMetres;
        double top = model.TopAltitudeMetres;
        double bigH = Math.Sqrt(top * (2.0 * radius + top));
        double rho = bigH * xH;
        double h = rho * rho / (radius + Math.Sqrt(radius * radius + rho * rho));
        double r = radius + h;
        double dMin = top - h;
        double dMax = rho + bigH;
        double d = dMin + xMu * (dMax - dMin);
        double mu = d <= 0.0 ? 1.0 : ((top - h) * (2.0 * radius + top + h) - d * d) / (2.0 * r * d);
        return (h, Math.Clamp(mu, -1.0, 1.0));
    }

    /// <summary>The columns texel coordinates of altitude <paramref name="altitude"/> above sea level and view cosine <paramref name="mu"/>.</summary>
    public static (double XMu, double XH) ColumnsAltitudeMuToCoordinates(AtmosphereModel model, double altitude, double mu)
    {
        ArgumentNullException.ThrowIfNull(model);
        double radius = model.PlanetRadiusMetres;
        double top = model.TopAltitudeMetres;
        double h = Math.Clamp(altitude, 0.0, top);
        double bigH = Math.Sqrt(top * (2.0 * radius + top));
        double rho = Math.Sqrt(h * (2.0 * radius + h));
        double d = DistanceToShellAbove(radius, h, top, mu);
        double dMin = top - h;
        double dMax = rho + bigH;
        double xMu = dMax > dMin ? (d - dMin) / (dMax - dMin) : 0.0;
        return (Math.Clamp(xMu, 0.0, 1.0), Math.Clamp(rho / bigH, 0.0, 1.0));
    }

    /// <summary>
    /// The density columns along a ray from <paramref name="altitude"/> (above sea level) at <c>cos = mu</c> to the top, by <paramref name="steps"/>
    /// midpoint steps (the valley haze by its mean over each step, <see cref="AtmosphereModel.AerosolStepDensity"/>).
    /// </summary>
    public static DensityColumns ColumnsAlong(AtmosphereModel model, double altitude, double mu, int steps)
    {
        ArgumentNullException.ThrowIfNull(model);
        double radius = model.PlanetRadiusMetres;
        double length = DistanceToShellAbove(radius, altitude, model.TopAltitudeMetres, mu);
        double dt = length / steps;
        double rayleigh = 0.0, aerosol = 0.0, ozone = 0.0;
        double start = AltitudeAlong(radius, altitude, mu, 0.0);
        for (int k = 0; k < steps; k++)
        {
            double h = AltitudeAlong(radius, altitude, mu, (k + 0.5) * dt);
            double end = AltitudeAlong(radius, altitude, mu, (k + 1) * dt);
            rayleigh += model.RayleighDensity(h);
            aerosol += model.AerosolStepDensity(start, h, end);
            ozone += model.OzoneDensity(h);
            start = end;
        }
        return new DensityColumns(rayleigh * dt, aerosol * dt, ozone * dt);
    }

    /// <summary>Columns texel (<paramref name="i"/> along μ, <paramref name="j"/> along altitude).</summary>
    public static DensityColumns ColumnsTexel(AtmosphereModel model, int i, int j)
    {
        (double h, double mu) = ColumnsCoordinatesToAltitudeMu(model, (double)i / (ColumnsMuSize - 1), (double)j / (ColumnsAltitudeSize - 1));
        return ColumnsAlong(model, h, mu, ColumnSteps);
    }

    /// <summary>Bilinear sample of a columns LUT (<see cref="ColumnsFloats"/> floats) at an altitude above sea level and a view cosine.</summary>
    public static DensityColumns SampleColumns(ReadOnlySpan<float> columns, AtmosphereModel model, double altitude, double mu)
    {
        (double xMu, double xH) = ColumnsAltitudeMuToCoordinates(model, altitude, mu);
        (int i0, int i1, double fi) = Cell(xMu, ColumnsMuSize);
        (int j0, int j1, double fj) = Cell(xH, ColumnsAltitudeSize);
        int a00 = (j0 * ColumnsMuSize + i0) * 4, a10 = (j0 * ColumnsMuSize + i1) * 4;
        int a01 = (j1 * ColumnsMuSize + i0) * 4, a11 = (j1 * ColumnsMuSize + i1) * 4;
        return new DensityColumns(
            Bilinear(columns, a00, a10, a01, a11, fi, fj),
            Bilinear(columns, a00 + 1, a10 + 1, a01 + 1, a11 + 1, fi, fj),
            Bilinear(columns, a00 + 2, a10 + 2, a01 + 2, a11 + 2, fi, fj));
    }

    /// <summary>
    /// Sunlight transmittance from <paramref name="altitude"/> (above sea level) toward a direction at
    /// <c>cos = mu</c> to the vertical through a columns LUT: zero when the ground sphere is in the way.
    /// </summary>
    public static double SunTransmittance(ReadOnlySpan<float> columns, AtmosphereFrame frame, in AtmosphereCoefficients k, double altitude, double mu)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (HitsSphere(frame.GroundRadius, altitude - frame.GroundAltitudeMetres, mu))
            return 0.0;
        return Math.Exp(-k.OpticalDepth(SampleColumns(columns, frame.Model, altitude, mu)));
    }

    /// <summary>
    /// The fraction of the sun's disk above the ground sphere's horizon, seen from <paramref name="height"/> above it with
    /// the disk's centre at <c>cos = muS</c> to the vertical — the Earth's shadow with its penumbra — and the direction to
    /// take that light along: <paramref name="muS"/>, or the horizon itself while the centre is below it (the visible
    /// upper limb grazes the ground). The disk is 0.53° across, so near the terminator the penumbra is kilometres deep
    /// (2.9–4.9 km up with the sun 2° down); a point sun, all or nothing at the centre, left segments ending in it 6% dark
    /// against the reference, which samples the disk (vista-plan §10 M1.4).
    /// </summary>
    public static (double Fraction, double SunMu) SunVisibility(AtmosphereFrame frame, double height, double muS)
    {
        ArgumentNullException.ThrowIfNull(frame);
        double radius = SolarSpectrum.AngularRadiusRadians;
        if (muS >= Math.Sin(radius))
            return (1.0, muS);
        double rb = frame.GroundRadius;
        double h = Math.Max(height, 0.0);
        double dip = Math.Atan2(Math.Sqrt(h * (2.0 * rb + h)), rb);
        double y = Math.Clamp((Math.Asin(Math.Clamp(muS, -1.0, 1.0)) + dip) / radius, -1.0, 1.0);
        return (0.5 + (y * Math.Sqrt(1.0 - y * y) + Math.Asin(y)) / Math.PI, Math.Max(muS, -Math.Sin(dip) + 1e-5));
    }

    /// <summary>
    /// Sunlight per unit solar irradiance reaching <paramref name="height"/> above the ground sphere with the sun's centre at
    /// <c>cos = muS</c>: the visible part of the disk (<see cref="SunVisibility"/>) through the air's transmittance.
    /// </summary>
    public static double SunLight(ReadOnlySpan<float> columns, AtmosphereFrame frame, in AtmosphereCoefficients k, double height, double muS)
    {
        ArgumentNullException.ThrowIfNull(frame);
        (double visible, double sunMu) = SunVisibility(frame, height, muS);
        return visible > 0.0 ? visible * Math.Exp(-k.OpticalDepth(SampleColumns(columns, frame.Model, frame.GroundAltitudeMetres + height, sunMu))) : 0.0;
    }

    // ── Ground irradiance ──────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The sky's irradiance on the ground sphere, per unit solar irradiance, with the sun at
    /// <see cref="GroundIrradianceMuS"/> (no direct sun): π times the mean sky radiance over a cosine-weighted
    /// grid of directions, each from <see cref="SkyMarch"/> with the summed scattering orders <paramref name="orders"/>
    /// at table position <paramref name="position"/> (<see cref="Rendering.WavelengthTable.MsPositions"/>).
    /// </summary>
    public static double GroundIrradianceEntry(ReadOnlySpan<float> columns, ReadOnlySpan<float> orders, AtmosphereFrame frame, in AtmosphereCoefficients k, int lambda,
        double position, int entry)
    {
        ArgumentNullException.ThrowIfNull(frame);
        double muS = GroundIrradianceMuS(frame, entry);
        double sinS = Math.Sqrt(Math.Max(1.0 - muS * muS, 0.0));
        double g = frame.Model.AerosolAsymmetry;
        double sum = 0.0;
        for (int a = 0; a < GroundIrradianceDirectionsPerSide; a++)
        {
            for (int b = 0; b < GroundIrradianceDirectionsPerSide; b++)
            {
                double u1 = (a + 0.5) / GroundIrradianceDirectionsPerSide;
                double u2 = (b + 0.5) / GroundIrradianceDirectionsPerSide;
                double radial = Math.Sqrt(u1);
                double phi = 2.0 * Math.PI * u2;
                double cosTheta = Math.Sqrt(1.0 - u1);
                double nu = radial * Math.Cos(phi) * sinS + cosTheta * muS;
                (double smooth, double mie) = SkyMarch(columns, orders, [], frame, k, lambda, position, AtmosphereScattering.MinimumHeight, cosTheta, muS, nu, toGround: false);
                sum += smooth + AirOptics.HenyeyGreenstein(nu, g) * mie;
            }
        }
        return Math.PI * sum / (GroundIrradianceDirectionsPerSide * GroundIrradianceDirectionsPerSide);
    }

    /// <summary>
    /// The sun cosine of ground-irradiance entry <paramref name="entry"/>: the 4D tables' sun map
    /// (<see cref="AtmosphereScattering.MuSFromUnit"/>), dense near the horizon, where twilight irradiance changes several-fold
    /// per degree. Evenly spaced cosines, 0.9° apart there, overshot it and left the twilight ground 2–3% bright against the
    /// reference (vista-plan §10 M1.4). Below the map's lowest sun the first entry, already dark, stands.
    /// </summary>
    public static double GroundIrradianceMuS(AtmosphereFrame frame, int entry) =>
        AtmosphereScattering.MuSFromUnit(frame, (double)entry / (GroundIrradianceSize - 1));

    /// <summary>Linear sample of a ground-irradiance table for wavelength <paramref name="lambda"/> at sun cosine <paramref name="muS"/>.</summary>
    public static double SampleGroundIrradiance(ReadOnlySpan<float> ground, AtmosphereFrame frame, int lambda, double muS)
    {
        (int i0, int i1, double f) = Cell(AtmosphereScattering.MuSToUnit(frame, muS), GroundIrradianceSize);
        double a = ground[i0 * Wavelengths + lambda];
        return a + f * (ground[i1 * Wavelengths + lambda] - a);
    }

    // ── Sky-view LUT ───────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The horizon of the ground sphere seen from <paramref name="height"/> above it: the zenith angle of the
    /// horizon (π/2 plus the dip) and the angle from there to the nadir (π/2 minus the dip).
    /// </summary>
    public static (double HorizonZenith, double BelowSpan) Horizon(double groundRadius, double height)
    {
        double dip = Math.Atan2(Math.Sqrt(Math.Max(height * (2.0 * groundRadius + height), 0.0)), groundRadius);
        return (0.5 * Math.PI + dip, 0.5 * Math.PI - dip);
    }

    /// <summary>
    /// The view zenith angle of sky-view row <paramref name="row"/> at <paramref name="height"/> above the ground,
    /// and whether the row looks at the ground. Rows above the horizon follow Hillaire's map, dense at the horizon
    /// (<c>θ = θ_h·(1 − (1 − v)²)</c>); rows below it follow <c>θ = θ_h + β·v²</c>, so both halves end exactly on
    /// the horizon and a lookup never interpolates across it.
    /// </summary>
    public static (double Zenith, bool Ground) SkyRowToZenith(double groundRadius, double height, int row)
    {
        (double horizon, double below) = Horizon(groundRadius, height);
        if (row < SkyRowsAbove)
        {
            double v = (double)row / (SkyRowsAbove - 1);
            return (horizon * (1.0 - (1.0 - v) * (1.0 - v)), false);
        }
        double w = (double)(row - SkyRowsAbove) / (SkyRowsBelow - 1);
        return (horizon + below * w * w, true);
    }

    /// <summary>The relative azimuth to the sun of sky-view column <paramref name="column"/>: 0 to π, evenly.</summary>
    public static double SkyColumnToAzimuth(int column) => Math.PI * column / (SkyAzimuthSize - 1);

    /// <summary>
    /// Sky-view texel (<paramref name="slice"/>, <paramref name="row"/>, <paramref name="column"/>) for wavelength
    /// <paramref name="lambda"/> at table position <paramref name="position"/>: (light without the haze phase, haze single
    /// scattering without its phase).
    /// </summary>
    public static (double Smooth, double Mie) SkyViewTexel(
        ReadOnlySpan<float> columns, ReadOnlySpan<float> orders, ReadOnlySpan<float> ground, AtmosphereFrame frame, in AtmosphereCoefficients k,
        int lambda, double position, int slice, int row, int column)
    {
        ArgumentNullException.ThrowIfNull(frame);
        double height = frame.SliceAltitude(slice) - frame.GroundAltitudeMetres;
        (double zenith, bool toGround) = SkyRowToZenith(frame.GroundRadius, height, row);
        double phi = SkyColumnToAzimuth(column);
        double muS = frame.SunCosZenith;
        double sinS = Math.Sqrt(Math.Max(1.0 - muS * muS, 0.0));
        double mu = Math.Cos(zenith);
        double nu = Math.Sin(zenith) * Math.Cos(phi) * sinS + mu * muS;
        return SkyMarch(columns, orders, ground, frame, k, lambda, position, height, mu, muS, nu, toGround);
    }

    /// <summary>
    /// The sky along one ray from <paramref name="height"/> above the ground sphere at <c>cos = mu</c> to the
    /// vertical, with the sun at <c>cos θ_s = muS</c> and <c>cos(ray, sun) = nu</c>: single scattering through the
    /// columns LUT (the Earth's shadow included), multiple scattering from the summed scattering orders
    /// (<paramref name="orders"/>, the phase-weighted radiance of <see cref="AtmosphereScattering"/>), and, for a ray that
    /// ends on the ground (<paramref name="toGround"/>), the ground lit by the sun and the sky, seen through the air.
    /// Each step integrates its emission exactly for a constant extinction over the step (Hillaire 2020). The wavelength is
    /// <paramref name="lambda"/> in the ground's table and <paramref name="position"/> among the orders' table wavelengths.
    /// </summary>
    public static (double Smooth, double Mie) SkyMarch(
        ReadOnlySpan<float> columns, ReadOnlySpan<float> orders, ReadOnlySpan<float> ground, AtmosphereFrame frame, in AtmosphereCoefficients k,
        int lambda, double position, double height, double mu, double muS, double nu, bool toGround)
    {
        ArgumentNullException.ThrowIfNull(frame);
        AtmosphereModel model = frame.Model;
        double groundRadius = frame.GroundRadius;
        double top = frame.TopRadius - groundRadius;
        double r0 = groundRadius + height;
        double length = toGround ? DistanceToSphereBelow(groundRadius, height, mu) : DistanceToShellAbove(groundRadius, height, top, mu);
        double transmittance = 1.0, rayleigh = 0.0, mie = 0.0, multiple = 0.0;
        for (int s = 0; s < SkySteps; s++)
        {
            (double t0, double t1) = SegmentEnds(length, s, SkySteps, toGround);
            (double t, double dt) = (0.5 * (t0 + t1), t1 - t0);
            double hg = AltitudeAlong(groundRadius, height, mu, t);
            double rp = groundRadius + hg;
            double muP = (r0 * mu + t) / rp;
            double muSp = (r0 * muS + t * nu) / rp;
            double altitude = frame.GroundAltitudeMetres + hg;
            double aerosol = model.AerosolStepDensity(frame.GroundAltitudeMetres + AltitudeAlong(groundRadius, height, mu, t0), altitude,
                frame.GroundAltitudeMetres + AltitudeAlong(groundRadius, height, mu, t1));
            (_, double extinction, double rayleighScattering, double mieScattering) = Coefficients(model, k, altitude, aerosol);
            double sun = SunLight(columns, frame, k, hg, muSp);
            var point = new AtmosphereScattering.Sample(hg, muP, muSp, AtmosphereScattering.ClampNu(muP, muSp, nu), toGround);
            double step = transmittance * StepIntegral(extinction, dt);
            rayleigh += step * sun * rayleighScattering;
            mie += step * sun * mieScattering;
            multiple += step * AtmosphereScattering.Density(orders, frame, k, position, point, aerosol);
            transmittance *= Math.Exp(-extinction * dt);
        }
        double smooth = AirOptics.RayleighPhase(nu) * rayleigh + multiple;
        if (toGround)
        {
            double muSg = (r0 * muS + length * nu) / groundRadius;
            double direct = muSg > 0.0 ? muSg * Math.Exp(-k.OpticalDepth(SampleColumns(columns, model, frame.GroundAltitudeMetres, muSg))) : 0.0;
            smooth += transmittance * model.GroundAlbedo / Math.PI * (direct + SampleGroundIrradiance(ground, frame, lambda, muSg));
        }
        return (smooth, mie);
    }

    /// <summary>
    /// Bilinear sample of a sky-view LUT in one slice: the ray's view cosine <paramref name="mu"/> and relative
    /// azimuth <paramref name="phi"/> are mapped on that slice's own horizon, and the lookup stays on the side of
    /// the horizon the direction is on.
    /// </summary>
    public static (double Smooth, double Mie) SampleSkyView(ReadOnlySpan<float> sky, AtmosphereFrame frame, int lambda, int slice, double mu, double phi)
    {
        ArgumentNullException.ThrowIfNull(frame);
        double height = frame.SliceAltitude(slice) - frame.GroundAltitudeMetres;
        (double horizon, double below) = Horizon(frame.GroundRadius, height);
        double zenith = Math.Acos(Math.Clamp(mu, -1.0, 1.0));
        (int first, int count, double v) = zenith <= horizon
            ? (0, SkyRowsAbove, 1.0 - Math.Sqrt(Math.Max(1.0 - zenith / horizon, 0.0)))
            : (SkyRowsAbove, SkyRowsBelow, Math.Sqrt(Math.Clamp((zenith - horizon) / below, 0.0, 1.0)));
        (int r0, int r1, double fr) = Cell(v, count);
        (int c0, int c1, double fc) = Cell(Math.Clamp(phi / Math.PI, 0.0, 1.0), SkyAzimuthSize);
        int Address(int row, int col) => ((((slice * SkyRows) + first + row) * SkyAzimuthSize + col) * Wavelengths + lambda) * 2;
        int a00 = Address(r0, c0), a10 = Address(r0, c1), a01 = Address(r1, c0), a11 = Address(r1, c1);
        return (Bilinear(sky, a00, a10, a01, a11, fc, fr), Bilinear(sky, a00 + 1, a10 + 1, a01 + 1, a11 + 1, fc, fr));
    }

    /// <summary>The relative azimuth (0 to π) between the horizontal parts of a direction and the sun; 0 when either is vertical.</summary>
    public static double RelativeAzimuth(double dirX, double dirZ, double sunX, double sunZ)
    {
        double dirLength = Math.Sqrt(dirX * dirX + dirZ * dirZ);
        double sunLength = Math.Sqrt(sunX * sunX + sunZ * sunZ);
        if (dirLength < 1e-6 || sunLength < 1e-6)
            return 0.0;
        return Math.Acos(Math.Clamp((dirX * sunX + dirZ * sunZ) / (dirLength * sunLength), -1.0, 1.0));
    }

    // ── Shared pieces ──────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Scattering and extinction per metre at <paramref name="altitude"/> above sea level, with the molecular and
    /// haze scattering apart: (total scattering, extinction, Rayleigh scattering, haze scattering).
    /// </summary>
    public static (double Scattering, double Extinction, double Rayleigh, double Mie) Coefficients(AtmosphereModel model, in AtmosphereCoefficients k, double altitude)
    {
        ArgumentNullException.ThrowIfNull(model);
        return Coefficients(model, k, altitude, model.AerosolDensity(altitude));
    }

    /// <summary>
    /// The same with the haze's relative density given, as a march's step has it (<see cref="AtmosphereModel.AerosolStepDensity"/>):
    /// the molecules and ozone at <paramref name="altitude"/>, the haze at <paramref name="aerosol"/>.
    /// </summary>
    public static (double Scattering, double Extinction, double Rayleigh, double Mie) Coefficients(AtmosphereModel model, in AtmosphereCoefficients k, double altitude,
        double aerosol)
    {
        ArgumentNullException.ThrowIfNull(model);
        double rayleigh = k.Rayleigh * model.RayleighDensity(altitude);
        double mie = k.AerosolScattering * aerosol;
        double extinction = rayleigh + k.AerosolExtinction * aerosol + k.Ozone * model.OzoneDensity(altitude);
        return (rayleigh + mie, extinction, rayleigh, mie);
    }

    /// <summary>
    /// Step <paramref name="s"/> of <paramref name="steps"/> along a ray of <paramref name="length"/>: its midpoint and
    /// its length. Steps grow quadratically away from the end where the air is densest — the start, or the ground
    /// for a ray that ends there (<paramref name="denseAtEnd"/>) — so a vertical ray resolves the haze's 1.2 km
    /// scale height (uniform steps of 780 m left its single scattering 1.3% low) while a horizontal one, whose air
    /// thins over a hundred kilometres, loses nothing.
    /// </summary>
    public static (double T, double Dt) Segment(double length, int s, int steps, bool denseAtEnd)
    {
        (double t0, double t1) = SegmentEnds(length, s, steps, denseAtEnd);
        return (0.5 * (t0 + t1), t1 - t0);
    }

    /// <summary>The ends of step <paramref name="s"/> of <see cref="Segment"/>: a step's end is the next one's start, exactly.</summary>
    public static (double T0, double T1) SegmentEnds(double length, int s, int steps, bool denseAtEnd)
    {
        double u0 = (double)s / steps, u1 = (double)(s + 1) / steps;
        double t0 = length * (denseAtEnd ? 1.0 - (1.0 - u0) * (1.0 - u0) : u0 * u0);
        double t1 = length * (denseAtEnd ? 1.0 - (1.0 - u1) * (1.0 - u1) : u1 * u1);
        return (t0, t1);
    }

    /// <summary>
    /// <c>∫₀^dt e^(−σt) dt = dt·(1 − e^(−x))/x</c> with <c>x = σ·dt</c>; below <c>x = 0.01</c> by its series
    /// <c>1 − x/2 + x²/6</c> (error under 5·10⁻⁸), as the float shader must: there <c>1 − e^(−x)</c> cancels, and at
    /// <c>x = 10⁻⁶</c> it was 4% off.
    /// </summary>
    public static double StepIntegral(double extinction, double dt)
    {
        double x = extinction * dt;
        return x < 0.01 ? dt * (1.0 - x * (0.5 - x * (1.0 / 6.0))) : (1.0 - Math.Exp(-x)) / extinction;
    }

    /// <summary>Bilinear blend of the four values at <paramref name="a00"/> … <paramref name="a11"/> (x then y).</summary>
    private static double Bilinear(ReadOnlySpan<float> data, int a00, int a10, int a01, int a11, double fx, double fy)
    {
        double low = data[a00] + fx * (data[a10] - data[a00]);
        double high = data[a01] + fx * (data[a11] - data[a01]);
        return low + fy * (high - low);
    }

    /// <summary>The two texels around <paramref name="x"/> ∈ [0, 1] on a vertex-centred axis of <paramref name="size"/> texels, and the weight of the second.</summary>
    public static (int I0, int I1, double F) Cell(double x, int size)
    {
        double f = Math.Clamp(x, 0.0, 1.0) * (size - 1);
        int i0 = Math.Min((int)Math.Floor(f), size - 2);
        return (i0, i0 + 1, f - i0);
    }
}
