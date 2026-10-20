using Vista.Rendering;

namespace Vista.Atmosphere;

/// <summary>
/// The per-wavelength atmosphere tables the GPU reads (vista-plan §6.4: one grid, the engine's 50 hero
/// wavelengths in cycle order, so entry <c>i</c> lines up with every other table's entry <c>i</c>).
/// <list type="bullet">
///   <item><see cref="Coefficients"/>, 4 floats per entry: Rayleigh scattering, haze scattering, haze extinction
///   and ozone absorption, per metre at relative density 1 (<see cref="AtmosphereModel.CoefficientsAt"/>).</item>
///   <item><see cref="Sun"/>, 4 floats per entry: the disk's spectral radiance (W·m⁻²·sr⁻¹·nm⁻¹), the irradiance
///   above the atmosphere (W·m⁻²·nm⁻¹), the ground albedo, and the entry's place among the scattering tables'
///   wavelengths (<see cref="AtmosphereScattering.MsPositions"/>).</item>
/// </list>
/// </summary>
public sealed class AtmosphereTables
{
    private AtmosphereTables(float[] wavelengths, AtmosphereCoefficients[] exact, float[] coefficients, float[] sun)
    {
        WavelengthsNm = wavelengths;
        Exact = exact;
        Coefficients = coefficients;
        Sun = sun;
    }

    /// <summary>The wavelength of each entry, nm.</summary>
    public float[] WavelengthsNm { get; }

    /// <summary>The coefficients of each entry in double precision, for the C# references.</summary>
    public IReadOnlyList<AtmosphereCoefficients> Exact { get; }

    /// <summary>Optical coefficients, 4 floats per entry.</summary>
    public float[] Coefficients { get; }

    /// <summary>Sun and ground, 4 floats per entry.</summary>
    public float[] Sun { get; }

    /// <summary>Builds the tables of <paramref name="model"/> on the engine's wavelength cycle.</summary>
    public static AtmosphereTables Build(AtmosphereModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        IReadOnlyList<string> errors = model.Validate();
        if (errors.Count > 0)
            throw new ArgumentException(string.Join(" ", errors), nameof(model));

        int n = IntegratorTables.Wavelengths.DeterministicCount;
        if (n != AtmosphereLuts.Wavelengths)
            throw new InvalidOperationException($"The engine cycles {n} wavelengths; the atmosphere LUTs are laid out for {AtmosphereLuts.Wavelengths}.");
        var wavelengths = new float[n];
        var exact = new AtmosphereCoefficients[n];
        var coefficients = new float[n * 4];
        var sun = new float[n * 4];
        for (int i = 0; i < n; i++)
        {
            int nm = IntegratorTables.Wavelengths.GetDeterministicWavelength(i);
            wavelengths[i] = nm;
            AtmosphereCoefficients k = model.CoefficientsAt(nm);
            exact[i] = k;
            coefficients[i * 4] = (float)k.Rayleigh;
            coefficients[i * 4 + 1] = (float)k.AerosolScattering;
            coefficients[i * 4 + 2] = (float)k.AerosolExtinction;
            coefficients[i * 4 + 3] = (float)k.Ozone;
            sun[i * 4] = (float)SolarSpectrum.Radiance(nm);
            sun[i * 4 + 1] = (float)SolarSpectrum.Irradiance(nm);
            sun[i * 4 + 2] = (float)model.GroundAlbedo;
            sun[i * 4 + 3] = AtmosphereScattering.MsPositions[i];
        }
        return new AtmosphereTables(wavelengths, exact, coefficients, sun);
    }

    /// <summary>
    /// The coefficients of entry <paramref name="index"/> as the GPU sees them (each rounded to float), so a C#
    /// reference fed these reproduces the shader's arithmetic rather than the exact model's.
    /// </summary>
    public AtmosphereCoefficients AsUploaded(int index) => new(
        Coefficients[index * 4], Coefficients[index * 4 + 1], Coefficients[index * 4 + 2], Coefficients[index * 4 + 3]);
}
