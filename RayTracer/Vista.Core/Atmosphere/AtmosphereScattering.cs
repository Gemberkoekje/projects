using Vista.Rendering;

namespace Vista.Atmosphere;

/// <summary>
/// Multiple scattering by iterated orders (vista-plan §5.2), after Bruneton 2017 ("Precomputed Atmospheric
/// Scattering: a New Implementation"): the C# reference of the 4D tables the GPU builds once per atmosphere and
/// ground altitude. Hillaire 2020's isotropic Ψ_ms was measured against the reference atmosphere first and came out
/// 6–13% dark on average, worst at the horizon and with haze: its second scattering is isotropic, while the haze's
/// forward lobe picks up the bright horizon. Single scattering stays exact in the sky-view march
/// (<see cref="AtmosphereLuts.SkyMarch"/>); these tables supply orders two and up.
///
/// <para><b>The 4D domain</b>, over a point at height <c>h</c> above the ground sphere looking along ω (the light
/// travels toward the point along −ω): <c>μ = ω·up</c>, <c>μ_s = s·up</c> and <c>ν = ω·s</c>, with Bruneton's maps
/// — height by the distance to the horizon, μ split at the horizon into rays that reach the top and rays that reach
/// the ground (a lookup never blends the two), μ_s dense near the horizon down to <see cref="MuSMin"/>, ν linear.</para>
///
/// <para><b>Wavelengths.</b> Multiple scattering is smooth in wavelength, so the tables hold <see cref="MsWavelengths"/>
/// of the 50 hero wavelengths (<see cref="HeroOf"/>) and a lookup for any of the 50 interpolates between the two around
/// it (<see cref="MsPositions"/>): a fifth of the time and memory, for an error measured in the plan.</para>
///
/// <para><b>Tables</b>, per table wavelength (index <c>texel · MsWavelengths + l</c>, texel
/// <c>((h · Nμ + μ) · Nμs + μs) · Nν + ν</c>):</para>
/// <list type="bullet">
///   <item>single scattering without phases (Rayleigh, haze) — <see cref="SingleScatteringTexel"/>;</item>
///   <item>for each order n ≥ 2, the <i>phase-weighted incident radiance</i> (Rayleigh, haze) — <see cref="DensityTexel"/>:
///   the light arriving from every direction, weighted by each species' phase toward −ω. The scattering density is
///   that times the local coefficients, <c>J = σ_R(h)·A_R + σ_M(h)·A_M</c>, so the table stays smooth in height even
///   where the haze's density falls off over 1.2 km;</item>
///   <item>the radiance of order n gathered along each ray — <see cref="MultipleScatteringTexel"/> — which feeds order
///   n + 1;</item>
///   <item>the ground's irradiance by order — <see cref="DirectIrradianceEntry"/>, <see cref="IndirectIrradianceEntry"/> —
///   for the light that reaches the ground, bounces, and scatters again.</item>
/// </list>
/// The sum of the phase-weighted radiance over orders 2 to <see cref="Orders"/> is what the sky-view march reads.
/// </summary>
public static class AtmosphereScattering
{
    /// <summary>Height texels.</summary>
    public const int HeightSize = 16;

    /// <summary>View-angle texels per half (rays to the top; rays to the ground).</summary>
    public const int MuHalfSize = 32;

    /// <summary>View-angle texels in all.</summary>
    public const int MuSize = 2 * MuHalfSize;

    /// <summary>Sun-angle texels.</summary>
    public const int MuSSize = 64;

    /// <summary>View-to-sun texels.</summary>
    public const int NuSize = 8;

    /// <summary>Texels in a table (per wavelength).</summary>
    public const int Texels = HeightSize * MuSize * MuSSize * NuSize;

    /// <summary>Wavelengths the tables hold.</summary>
    public const int MsWavelengths = 11;

    /// <summary>Floats per channel of a 4D table (texels × table wavelengths).</summary>
    public const int FloatsPerChannel = Texels * MsWavelengths;

    /// <summary>Floats per ground-irradiance-by-order table (entries × table wavelengths).</summary>
    public const int IrradianceFloats = AtmosphereLuts.GroundIrradianceSize * MsWavelengths;

    /// <summary>The lowest sun cosine the tables hold (120° from the zenith, Bruneton's full-precision choice).</summary>
    public const double MuSMin = -0.5;

    /// <summary>The highest scattering order summed.</summary>
    public const int Orders = 5;

    /// <summary>Steps along each ray of the single and multiple scattering tables.</summary>
    public const int Steps = 40;

    /// <summary>Polar angles of the sphere quadrature of the density.</summary>
    public const int DensityTheta = 32;

    /// <summary>Azimuths of the sphere quadrature of the density.</summary>
    public const int DensityPhi = 32;

    /// <summary>Polar angles of the hemisphere quadrature of the ground irradiance.</summary>
    public const int IrradianceTheta = 16;

    /// <summary>Azimuths of the hemisphere quadrature of the ground irradiance.</summary>
    public const int IrradiancePhi = 16;

    /// <summary>The lowest height a texel describes (the ground itself has no horizon to split at), m.</summary>
    public const double MinimumHeight = 1.0;

    /// <summary>The hero wavelength index of table wavelength <paramref name="l"/>: every fifth, and the last.</summary>
    public static int HeroOf(int l) => l == MsWavelengths - 1 ? AtmosphereLuts.Wavelengths - 1 : 5 * l;

    private static readonly Lazy<float[]> SharedPositions = new(() => PositionsFor(i => IntegratorTables.Wavelengths.GetDeterministicWavelength(i)));

    /// <summary>
    /// For each of the 50 hero wavelengths, its place among the table wavelengths: <c>l + f</c> means between table
    /// wavelengths <c>l</c> and <c>l + 1</c>, a fraction <c>f</c> of the way in nanometres.
    /// </summary>
    public static IReadOnlyList<float> MsPositions => SharedPositions.Value;

    /// <summary><see cref="MsPositions"/> for a wavelength table <paramref name="nm"/> (the hero cycle is in ascending wavelength).</summary>
    public static float[] PositionsFor(Func<int, double> nm)
    {
        ArgumentNullException.ThrowIfNull(nm);
        var positions = new float[AtmosphereLuts.Wavelengths];
        for (int i = 0; i < positions.Length; i++)
        {
            int l0 = Math.Min(i / 5, MsWavelengths - 2);
            double a = nm(HeroOf(l0)), b = nm(HeroOf(l0 + 1));
            positions[i] = (float)(l0 + Math.Clamp((nm(i) - a) / (b - a), 0.0, 1.0));
        }
        return positions;
    }

    // ── The 4D map ─────────────────────────────────────────────────────────────────────────────────────

    /// <summary>A point and direction of the 4D domain.</summary>
    /// <param name="Height">Height above the ground sphere, m.</param>
    /// <param name="Mu">View cosine to the vertical.</param>
    /// <param name="MuS">Sun cosine to the vertical.</param>
    /// <param name="Nu">View-to-sun cosine.</param>
    /// <param name="Ground">Whether the view ray ends on the ground.</param>
    public readonly record struct Sample(double Height, double Mu, double MuS, double Nu, bool Ground);

    /// <summary>The domain point of texel (<paramref name="ih"/>, <paramref name="imu"/>, <paramref name="imus"/>, <paramref name="inu"/>), ν clamped to what μ and μ_s allow.</summary>
    public static Sample TexelToSample(AtmosphereFrame frame, int ih, int imu, int imus, int inu)
    {
        ArgumentNullException.ThrowIfNull(frame);
        double rb = frame.GroundRadius;
        double top = frame.TopRadius - rb;
        double bigH = Math.Sqrt(top * (2.0 * rb + top));
        double rho = bigH * ih / (HeightSize - 1);
        double h = Math.Max(rho * rho / (rb + Math.Sqrt(rb * rb + rho * rho)), MinimumHeight);
        rho = Math.Sqrt(h * (2.0 * rb + h));
        double r = rb + h;

        bool ground = imu >= MuHalfSize;
        double x = (double)(ground ? imu - MuHalfSize : imu) / (MuHalfSize - 1);
        double mu;
        if (ground)
        {
            double d = h + x * (rho - h);
            mu = d <= 0.0 ? -1.0 : Math.Clamp(-(rho * rho + d * d) / (2.0 * r * d), -1.0, 1.0);
        }
        else
        {
            double dMin = top - h;
            double d = dMin + x * (rho + bigH - dMin);
            mu = d <= 0.0 ? 1.0 : Math.Clamp(((top - h) * (2.0 * rb + top + h) - d * d) / (2.0 * r * d), -1.0, 1.0);
        }

        double muS = MuSFromUnit(frame, (double)imus / (MuSSize - 1));
        double nu = Math.Clamp(2.0 * inu / (NuSize - 1) - 1.0, -1.0, 1.0);
        return new Sample(h, mu, muS, ClampNu(mu, muS, nu), ground);
    }

    /// <summary><paramref name="nu"/> clamped to the cosines two directions with these zenith cosines can make.</summary>
    public static double ClampNu(double mu, double muS, double nu)
    {
        double spread = Math.Sqrt(Math.Max((1.0 - mu * mu) * (1.0 - muS * muS), 0.0));
        return Math.Clamp(nu, mu * muS - spread, mu * muS + spread);
    }

    /// <summary>Bruneton's sun map, unit coordinate to cosine: dense near the horizon, <see cref="MuSMin"/> at 0 and 1 at 1.</summary>
    public static double MuSFromUnit(AtmosphereFrame frame, double x)
    {
        ArgumentNullException.ThrowIfNull(frame);
        double rb = frame.GroundRadius;
        double top = frame.TopRadius - rb;
        double bigH = Math.Sqrt(top * (2.0 * rb + top));
        double dMin = top, dMax = bigH;
        double bigA = (AtmosphereLuts.DistanceToShellAbove(rb, 0.0, top, MuSMin) - dMin) / (dMax - dMin);
        double a = (bigA - x * bigA) / (1.0 + x * bigA);
        double d = dMin + Math.Min(a, bigA) * (dMax - dMin);
        return d <= 0.0 ? 1.0 : Math.Clamp((bigH * bigH - d * d) / (2.0 * rb * d), -1.0, 1.0);
    }

    /// <summary>The inverse of <see cref="MuSFromUnit"/>.</summary>
    public static double MuSToUnit(AtmosphereFrame frame, double muS)
    {
        ArgumentNullException.ThrowIfNull(frame);
        double rb = frame.GroundRadius;
        double top = frame.TopRadius - rb;
        double bigH = Math.Sqrt(top * (2.0 * rb + top));
        double dMin = top, dMax = bigH;
        double a = (AtmosphereLuts.DistanceToShellAbove(rb, 0.0, top, muS) - dMin) / (dMax - dMin);
        double bigA = (AtmosphereLuts.DistanceToShellAbove(rb, 0.0, top, MuSMin) - dMin) / (dMax - dMin);
        return Math.Clamp(Math.Max(1.0 - a / bigA, 0.0) / (1.0 + a), 0.0, 1.0);
    }

    /// <summary>The cell of a lookup: two texels per axis and the weights of the second ones.</summary>
    public readonly record struct Cell(int H0, int Mu0, int MuS0, int Nu0, double Fh, double Fmu, double Fmus, double Fnu);

    /// <summary>The cell of <paramref name="sample"/>; the view axis stays in the half its ray belongs to.</summary>
    public static Cell CellOf(AtmosphereFrame frame, in Sample sample)
    {
        ArgumentNullException.ThrowIfNull(frame);
        double rb = frame.GroundRadius;
        double top = frame.TopRadius - rb;
        double bigH = Math.Sqrt(top * (2.0 * rb + top));
        double h = Math.Clamp(sample.Height, 0.0, top);
        double rho = Math.Sqrt(h * (2.0 * rb + h));
        double xMu;
        if (sample.Ground)
        {
            double d = AtmosphereLuts.DistanceToSphereBelow(rb, h, sample.Mu);
            xMu = rho > h ? (d - h) / (rho - h) : 0.0;
        }
        else
        {
            double d = AtmosphereLuts.DistanceToShellAbove(rb, h, top, sample.Mu);
            double dMin = top - h, dMax = rho + bigH;
            xMu = (d - dMin) / (dMax - dMin);
        }
        (int h0, _, double fh) = AtmosphereLuts.Cell(rho / bigH, HeightSize);
        (int m0, _, double fm) = AtmosphereLuts.Cell(xMu, MuHalfSize);
        (int s0, _, double fs) = AtmosphereLuts.Cell(MuSToUnit(frame, sample.MuS), MuSSize);
        (int n0, _, double fn) = AtmosphereLuts.Cell((sample.Nu + 1.0) * 0.5, NuSize);
        return new Cell(h0, sample.Ground ? MuHalfSize + m0 : m0, s0, n0, fh, fm, fs, fn);
    }

    /// <summary>Index of the first float of texel (<paramref name="ih"/>, <paramref name="imu"/>, <paramref name="imus"/>, <paramref name="inu"/>) for table wavelength <paramref name="l"/>, with <paramref name="channels"/> floats per value.</summary>
    public static int Index(int ih, int imu, int imus, int inu, int l, int channels) =>
        ((((ih * MuSize + imu) * MuSSize + imus) * NuSize + inu) * MsWavelengths + l) * channels;

    /// <summary>Quadrilinear sample of channel <paramref name="channel"/> at table wavelength <paramref name="l"/> of a table with <paramref name="channels"/> floats per value.</summary>
    public static double Lookup(ReadOnlySpan<float> table, in Cell cell, int l, int channels, int channel)
    {
        double sum = 0.0;
        for (int corner = 0; corner < 16; corner++)
        {
            int bh = corner & 1, bm = (corner >> 1) & 1, bs = (corner >> 2) & 1, bn = (corner >> 3) & 1;
            double w = (bh == 1 ? cell.Fh : 1.0 - cell.Fh) * (bm == 1 ? cell.Fmu : 1.0 - cell.Fmu)
                * (bs == 1 ? cell.Fmus : 1.0 - cell.Fmus) * (bn == 1 ? cell.Fnu : 1.0 - cell.Fnu);
            sum += w * table[Index(cell.H0 + bh, cell.Mu0 + bm, cell.MuS0 + bs, cell.Nu0 + bn, l, channels) + channel];
        }
        return sum;
    }

    /// <summary>The direction of a sample (view along +X tilted by μ, in the x–y plane) and its sun.</summary>
    public static (double Wx, double Wy, double Wz, double Sx, double Sy, double Sz) Directions(in Sample sample)
    {
        double sinMu = Math.Sqrt(Math.Max(1.0 - sample.Mu * sample.Mu, 0.0));
        double sx = sinMu > 1e-6 ? (sample.Nu - sample.Mu * sample.MuS) / sinMu : 0.0;
        double sz = Math.Sqrt(Math.Max(1.0 - sx * sx - sample.MuS * sample.MuS, 0.0));
        return (sinMu, sample.Mu, 0.0, sx, sample.MuS, sz);
    }

    // ── Ground irradiance by order ─────────────────────────────────────────────────────────────────────

    /// <summary>Direct sunlight on the ground per unit solar irradiance, with the sun at entry <paramref name="entry"/>'s cosine (the order-0 irradiance).</summary>
    public static double DirectIrradianceEntry(ReadOnlySpan<float> columns, AtmosphereFrame frame, in AtmosphereCoefficients k, int entry)
    {
        ArgumentNullException.ThrowIfNull(frame);
        double muS = 2.0 * entry / (AtmosphereLuts.GroundIrradianceSize - 1) - 1.0;
        return muS > 0.0 ? muS * Math.Exp(-k.OpticalDepth(AtmosphereLuts.SampleColumns(columns, frame.Model, frame.GroundAltitudeMetres, muS))) : 0.0;
    }

    /// <summary>Linear sample of a ground-irradiance-by-order table (layout <c>entry · MsWavelengths + l</c>) at sun cosine <paramref name="muS"/>.</summary>
    public static double SampleIrradiance(ReadOnlySpan<float> irradiance, int l, double muS)
    {
        (int i0, int i1, double f) = AtmosphereLuts.Cell((muS + 1.0) * 0.5, AtmosphereLuts.GroundIrradianceSize);
        double a = irradiance[i0 * MsWavelengths + l];
        return a + f * (irradiance[i1 * MsWavelengths + l] - a);
    }

    /// <summary>
    /// The ground's irradiance from light of one order, by a hemisphere quadrature: order 1 from the single-scattering
    /// table with the phases applied (<paramref name="single"/>, 2 channels), later orders from that order's radiance
    /// (<paramref name="multiple"/>, 1 channel).
    /// </summary>
    public static double IndirectIrradianceEntry(ReadOnlySpan<float> single, ReadOnlySpan<float> multiple, bool firstOrder,
        AtmosphereFrame frame, int l, int entry)
    {
        ArgumentNullException.ThrowIfNull(frame);
        double muS = 2.0 * entry / (AtmosphereLuts.GroundIrradianceSize - 1) - 1.0;
        double sinS = Math.Sqrt(Math.Max(1.0 - muS * muS, 0.0));
        double g = frame.Model.AerosolAsymmetry;
        double dTheta = 0.5 * Math.PI / IrradianceTheta;
        double dPhi = 2.0 * Math.PI / IrradiancePhi;
        double sum = 0.0;
        for (int i = 0; i < IrradianceTheta; i++)
        {
            double theta = (i + 0.5) * dTheta;
            double cosTheta = Math.Cos(theta), sinTheta = Math.Sin(theta);
            for (int j = 0; j < IrradiancePhi; j++)
            {
                double phi = (j + 0.5) * dPhi;
                double nu = sinTheta * Math.Cos(phi) * sinS + cosTheta * muS;
                Cell cell = CellOf(frame, new Sample(0.0, cosTheta, muS, ClampNu(cosTheta, muS, nu), false));
                double radiance = firstOrder
                    ? Lookup(single, cell, l, 2, 0) * AirOptics.RayleighPhase(nu) + Lookup(single, cell, l, 2, 1) * AirOptics.HenyeyGreenstein(nu, g)
                    : Lookup(multiple, cell, l, 1, 0);
                sum += radiance * cosTheta * sinTheta * dTheta * dPhi;
            }
        }
        return sum;
    }

    // ── Single scattering, density, multiple scattering ────────────────────────────────────────────────

    /// <summary>Single scattering without phases (Rayleigh, haze) arriving at a texel's point along its ray, per unit solar irradiance.</summary>
    public static (double Rayleigh, double Mie) SingleScatteringTexel(ReadOnlySpan<float> columns, AtmosphereFrame frame, in AtmosphereCoefficients k,
        int ih, int imu, int imus, int inu)
    {
        Sample sample = TexelToSample(frame, ih, imu, imus, inu);
        AtmosphereModel model = frame.Model;
        double rb = frame.GroundRadius;
        double top = frame.TopRadius - rb;
        double r0 = rb + sample.Height;
        double length = sample.Ground
            ? AtmosphereLuts.DistanceToSphereBelow(rb, sample.Height, sample.Mu)
            : AtmosphereLuts.DistanceToShellAbove(rb, sample.Height, top, sample.Mu);
        double transmittance = 1.0, rayleigh = 0.0, mie = 0.0;
        for (int s = 0; s < Steps; s++)
        {
            (double t, double dt) = AtmosphereLuts.Segment(length, s, Steps, sample.Ground);
            double hg = AtmosphereLuts.AltitudeAlong(rb, sample.Height, sample.Mu, t);
            double rp = rb + hg;
            double muSp = (r0 * sample.MuS + t * sample.Nu) / rp;
            double altitude = frame.GroundAltitudeMetres + hg;
            (_, double extinction, double rayleighScattering, double mieScattering) = AtmosphereLuts.Coefficients(model, k, altitude);
            double sun = AtmosphereLuts.HitsSphere(rb, hg, muSp) ? 0.0 : Math.Exp(-k.OpticalDepth(AtmosphereLuts.SampleColumns(columns, model, altitude, muSp)));
            double step = transmittance * AtmosphereLuts.StepIntegral(extinction, dt);
            rayleigh += step * sun * rayleighScattering;
            mie += step * sun * mieScattering;
            transmittance *= Math.Exp(-extinction * dt);
        }
        return (rayleigh, mie);
    }

    /// <summary>
    /// The phase-weighted incident radiance (Rayleigh, haze) of order <paramref name="order"/> ≥ 2 at a texel, for table
    /// wavelength <paramref name="l"/>: a sphere quadrature of the light of order − 1 arriving from every direction — the
    /// single-scattering table with its phases for order 2 (<paramref name="single"/>), else that order's radiance
    /// (<paramref name="multiple"/>) — plus, for directions that end on the ground, the ground lit by the irradiance of
    /// order − 2 (<paramref name="irradiance"/>) and seen through the air; each weighted by the species' phase toward −ω.
    /// </summary>
    public static (double Rayleigh, double Mie) DensityTexel(ReadOnlySpan<float> columns, ReadOnlySpan<float> single, ReadOnlySpan<float> multiple,
        ReadOnlySpan<float> irradiance, AtmosphereFrame frame, in AtmosphereCoefficients k, int order, int l, int ih, int imu, int imus, int inu)
    {
        if (order < 2)
            throw new ArgumentOutOfRangeException(nameof(order), order, "Densities start at order 2.");
        Sample sample = TexelToSample(frame, ih, imu, imus, inu);
        (double wx, double wy, double wz, double sx, double sy, double sz) = Directions(sample);
        AtmosphereModel model = frame.Model;
        double rb = frame.GroundRadius;
        double r = rb + sample.Height;
        double g = model.AerosolAsymmetry;
        double dTheta = Math.PI / DensityTheta;
        double dPhi = 2.0 * Math.PI / DensityPhi;
        double rayleigh = 0.0, mie = 0.0;
        for (int i = 0; i < DensityTheta; i++)
        {
            double theta = (i + 0.5) * dTheta;
            double cosTheta = Math.Cos(theta), sinTheta = Math.Sin(theta);
            bool ground = AtmosphereLuts.HitsSphere(rb, sample.Height, cosTheta);
            double groundDistance = 0.0, groundTransmittance = 0.0;
            if (ground)
            {
                groundDistance = AtmosphereLuts.DistanceToSphereBelow(rb, sample.Height, cosTheta);
                double muGround = -(r * cosTheta + groundDistance) / rb; // the reversed ray's cosine at the ground point
                DensityColumns fromGround = AtmosphereLuts.SampleColumns(columns, model, frame.GroundAltitudeMetres, muGround);
                DensityColumns fromPoint = AtmosphereLuts.SampleColumns(columns, model, frame.GroundAltitudeMetres + sample.Height, -cosTheta);
                groundTransmittance = Math.Exp(-Math.Max(k.OpticalDepth(fromGround) - k.OpticalDepth(fromPoint), 0.0));
            }
            for (int j = 0; j < DensityPhi; j++)
            {
                double phi = (j + 0.5) * dPhi;
                double ox = sinTheta * Math.Cos(phi), oy = cosTheta, oz = sinTheta * Math.Sin(phi);
                double nu1 = ox * sx + oy * sy + oz * sz;
                Cell cell = CellOf(frame, new Sample(sample.Height, cosTheta, sample.MuS, ClampNu(cosTheta, sample.MuS, nu1), ground));
                double incident = order == 2
                    ? Lookup(single, cell, l, 2, 0) * AirOptics.RayleighPhase(nu1) + Lookup(single, cell, l, 2, 1) * AirOptics.HenyeyGreenstein(nu1, g)
                    : Lookup(multiple, cell, l, 1, 0);
                if (ground)
                {
                    double muSg = (r * sample.MuS + groundDistance * nu1) / rb;
                    incident += groundTransmittance * model.GroundAlbedo / Math.PI * SampleIrradiance(irradiance, l, muSg);
                }
                double nu2 = ox * wx + oy * wy + oz * wz;
                double weight = incident * sinTheta * dTheta * dPhi;
                rayleigh += weight * AirOptics.RayleighPhase(nu2);
                mie += weight * AirOptics.HenyeyGreenstein(nu2, g);
            }
        }
        return (rayleigh, mie);
    }

    /// <summary>
    /// The scattering density of a phase-weighted radiance table (2 channels) at a sample, for a wavelength at
    /// <paramref name="position"/> among the table wavelengths (<see cref="MsPositions"/>, or an integer inside the
    /// iteration) with coefficients <paramref name="k"/>: <c>σ_R(h)·A_R + σ_M(h)·A_M</c>.
    /// </summary>
    public static double Density(ReadOnlySpan<float> weighted, AtmosphereFrame frame, in AtmosphereCoefficients k, double position, in Sample sample)
    {
        ArgumentNullException.ThrowIfNull(frame);
        Cell cell = CellOf(frame, sample);
        int l0 = Math.Min((int)Math.Floor(position), MsWavelengths - 2);
        double f = position - l0;
        double rayleigh = Lookup(weighted, cell, l0, 2, 0), mie = Lookup(weighted, cell, l0, 2, 1);
        if (f > 0.0)
        {
            rayleigh += f * (Lookup(weighted, cell, l0 + 1, 2, 0) - rayleigh);
            mie += f * (Lookup(weighted, cell, l0 + 1, 2, 1) - mie);
        }
        double altitude = frame.GroundAltitudeMetres + sample.Height;
        return k.Rayleigh * frame.Model.RayleighDensity(altitude) * rayleigh + k.AerosolScattering * frame.Model.AerosolDensity(altitude) * mie;
    }

    /// <summary>The radiance of one order at table wavelength <paramref name="l"/> arriving at a texel's point along its ray: its density (<paramref name="weighted"/>, 2 channels) gathered through the air.</summary>
    public static double MultipleScatteringTexel(ReadOnlySpan<float> weighted, AtmosphereFrame frame, in AtmosphereCoefficients k,
        int l, int ih, int imu, int imus, int inu)
    {
        Sample sample = TexelToSample(frame, ih, imu, imus, inu);
        AtmosphereModel model = frame.Model;
        double rb = frame.GroundRadius;
        double top = frame.TopRadius - rb;
        double r0 = rb + sample.Height;
        double length = sample.Ground
            ? AtmosphereLuts.DistanceToSphereBelow(rb, sample.Height, sample.Mu)
            : AtmosphereLuts.DistanceToShellAbove(rb, sample.Height, top, sample.Mu);
        double transmittance = 1.0, radiance = 0.0;
        for (int s = 0; s < Steps; s++)
        {
            (double t, double dt) = AtmosphereLuts.Segment(length, s, Steps, sample.Ground);
            double hg = AtmosphereLuts.AltitudeAlong(rb, sample.Height, sample.Mu, t);
            double rp = rb + hg;
            double muP = (r0 * sample.Mu + t) / rp;
            double muSp = (r0 * sample.MuS + t * sample.Nu) / rp;
            double extinction = AtmosphereLuts.Coefficients(model, k, frame.GroundAltitudeMetres + hg).Extinction;
            double density = Density(weighted, frame, k, l, new Sample(hg, muP, muSp, ClampNu(muP, muSp, sample.Nu), sample.Ground));
            radiance += transmittance * density * AtmosphereLuts.StepIntegral(extinction, dt);
            transmittance *= Math.Exp(-extinction * dt);
        }
        return radiance;
    }
}
