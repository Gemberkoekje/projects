namespace RayTracer;

using System.Collections.Frozen;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Reflection;
using System.Text;

public class MaterialsLookup
{
    private readonly FrozenDictionary<string, MaterialData> _materials;

    public MaterialsLookup()
    {
        var materials = new Dictionary<string, MaterialData>();
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = "RayTracer.Data.materials_data.csv";

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' was not found.");
        using (var reader = new StreamReader(stream))
        {
            // Skip the header line
            reader.ReadLine();

            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                // Parse the CSV line
                var parts = ParseCSVLine(line);
                if (parts.Length < 13) continue;

                var id = parts[0];
                var name = parts[1];

                // Parse reflectance values - some may be empty strings
                var vReflectance = string.IsNullOrEmpty(parts[2]) ? null : parts[2];
                var mReflectance = string.IsNullOrEmpty(parts[3]) ? null : parts[3];

                if (float.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out float mpRatio) &&
                    float.TryParse(parts[5], NumberStyles.Float, CultureInfo.InvariantCulture, out float lStar) &&
                    float.TryParse(parts[6], NumberStyles.Float, CultureInfo.InvariantCulture, out float aStar) &&
                    float.TryParse(parts[7], NumberStyles.Float, CultureInfo.InvariantCulture, out float bStar) &&
                    TryParsePercentage(parts[8], out float rReflectance) &&
                    TryParsePercentage(parts[9], out float gReflectance) &&
                    TryParsePercentage(parts[10], out float bReflectance) &&
                    TryParsePercentage(parts[11], out float specularity) &&
                    float.TryParse(parts[12], NumberStyles.Float, CultureInfo.InvariantCulture, out float roughness))
                {
                    var spectralData = LoadSpectralData(assembly, id);

                    materials[id] = new MaterialData(
                        id,
                        name,
                        vReflectance,
                        mReflectance,
                        mpRatio,
                        lStar,
                        aStar,
                        bStar,
                        rReflectance,
                        gReflectance,
                        bReflectance,
                        specularity,
                        roughness,
                        spectralData
                    );
                }
            }
        }

        _materials = materials.ToFrozenDictionary();
    }

    // Helper method to load spectral data if available
    private SpectralData? LoadSpectralData(Assembly assembly, string materialId)
    {
        try
        {
            // Pad the ID with leading zeros to match the naming convention
            string paddedId = materialId.PadLeft(5, '0');
            string resourceName = $"RayTracer.Data.{paddedId}_spectral.csv";

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null) return null;

            using var reader = new StreamReader(stream);

            // Read header
            var header = reader.ReadLine();
            if (header == null) return null;

            var columns = header.Split(',');
            if (columns.Length < 2) return null;

            var wavelengths = new List<int>();
            var sciValues = new List<float>();
            var sceValues = new List<float>();

            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                var parts = line.Split(',');
                if (parts.Length < 3) continue;

                if (int.TryParse(parts[0], out int wavelength) &&
                    float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float sci) &&
                    float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float sce))
                {
                    wavelengths.Add(wavelength);
                    sciValues.Add(sci / 100f);
                    sceValues.Add(sce / 100f);
                }
            }

            if (wavelengths.Count > 0)
            {
                return new SpectralData(wavelengths.ToArray(), sciValues.ToArray(), sceValues.ToArray());
            }

            return null;
        }
        catch
        {
            // Silently fail if spectral data isn't available or can't be parsed
            return null;
        }
    }

    // Helper method for parsing CSV lines that properly handles quoted fields
    private string[] ParseCSVLine(string line)
    {
        List<string> result = new List<string>();
        bool inQuotes = false;
        StringBuilder field = new StringBuilder();

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];

            if (c == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (c == ',' && !inQuotes)
            {
                result.Add(field.ToString());
                field.Clear();
            }
            else
            {
                field.Append(c);
            }
        }

        // Add the last field
        result.Add(field.ToString());

        return result.ToArray();
    }

    // Helper method to parse percentages like "83.16%"
    private bool TryParsePercentage(string value, out float result)
    {
        result = 0f;
        if (string.IsNullOrEmpty(value)) return false;

        // Remove the % sign if present
        string cleanValue = value.TrimEnd('%');

        if (float.TryParse(cleanValue, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed))
        {
            // Convert percentage to a decimal value (0-1 range)
            result = parsed / 100f;
            return true;
        }

        return false;
    }

    // Method to get material data by ID
    public bool TryGetMaterial(string id, out MaterialData materialData)
    {
        if (_materials.TryGetValue(id, out var found) && found is not null)
        {
            materialData = found;
            return true;
        }

        materialData = default!;
        return false;
    }

    // Indexer to access material data by ID
    public MaterialData this[string id] => _materials[id];

    // Method to get all material IDs
    public IEnumerable<string> GetAllMaterialIds() => _materials.Keys;

    // Method to get all materials
    public IEnumerable<MaterialData> GetAllMaterials() => _materials.Values;

    // Method to find materials by name (partial match)
    public IEnumerable<MaterialData> FindMaterialsByName(string namePattern)
    {
        return _materials.Values.Where(m => m.Name.Contains(namePattern, StringComparison.OrdinalIgnoreCase));
    }
}

// Class to represent spectral data for a material
public class SpectralData
{
    // Flat arrays indexed by (wavelength - MinWavelength) for O(1) access.
    private readonly float[] _sciValues;
    private readonly float[] _sceValues;
    private readonly float[] _maxValues;

    // Minimum and maximum wavelengths in the data
    public int MinWavelength { get; }
    public int MaxWavelength { get; }

    public SpectralData(int[] wavelengths, float[] sciValues, float[] sceValues)
    {
        if (wavelengths.Length == 0)
            throw new ArgumentException("Wavelength array cannot be empty", nameof(wavelengths));

        MinWavelength = wavelengths.Min();
        MaxWavelength = wavelengths.Max();

        // Create a dictionary for the raw data points
        var rawData = new Dictionary<int, (float sci, float sce)>();
        for (int i = 0; i < wavelengths.Length; i++)
        {
            rawData[wavelengths[i]] = (sciValues[i], sceValues[i]);
        }

        int count = MaxWavelength - MinWavelength + 1;
        _sciValues = new float[count];
        _sceValues = new float[count];
        _maxValues = new float[count];

        // Precompute all values between min and max wavelength
        for (int wl = MinWavelength; wl <= MaxWavelength; wl++)
        {
            (float sci, float sce) entry;
            if (rawData.TryGetValue(wl, out var value))
            {
                entry = value;
            }
            else
            {
                entry = InterpolateValues(wl, wavelengths, sciValues, sceValues);
            }
            int idx = wl - MinWavelength;
            _sciValues[idx] = entry.sci;
            _sceValues[idx] = entry.sce;
            _maxValues[idx] = MathF.Max(entry.sci, entry.sce);
        }
    }

    private (float sci, float sce) InterpolateValues(int wavelength, int[] wavelengths, float[] sciValues, float[] sceValues)
    {
        // If wavelength is outside the range, return the closest value
        if (wavelength <= wavelengths[0])
            return (sciValues[0], sceValues[0]);
        if (wavelength >= wavelengths[^1])
            return (sciValues[^1], sceValues[^1]);

        // Find the two wavelengths that surround the requested wavelength
        int index = Array.BinarySearch(wavelengths, wavelength);

        if (index >= 0)
            return (sciValues[index], sceValues[index]); // Exact match

        // If not an exact match, BinarySearch returns the bitwise complement of the next larger element
        index = ~index;
        int lowerIndex = index - 1;

        // Linear interpolation
        float t = (float)(wavelength - wavelengths[lowerIndex]) / (wavelengths[index] - wavelengths[lowerIndex]);
        float interpolatedSci = sciValues[lowerIndex] * (1 - t) + sciValues[index] * t;
        float interpolatedSce = sceValues[lowerIndex] * (1 - t) + sceValues[index] * t;

        return (interpolatedSci, interpolatedSce);
    }

    // Get the SCI value for a specific wavelength
    public float GetSciValue(int wavelength)
    {
        if (wavelength < MinWavelength) wavelength = MinWavelength;
        if (wavelength > MaxWavelength) wavelength = MaxWavelength;
        return _sciValues[wavelength - MinWavelength];
    }

    // Get the SCE value for a specific wavelength
    public float GetSceValue(int wavelength)
    {
        if (wavelength < MinWavelength) wavelength = MinWavelength;
        if (wavelength > MaxWavelength) wavelength = MaxWavelength;
        return _sceValues[wavelength - MinWavelength];
    }

    // Get max(SCI, SCE) in a single lookup — avoids two separate array accesses
    // in the common case where callers want the robust maximum.
    public float GetMaxReflectance(int wavelength)
    {
        if (wavelength < MinWavelength) wavelength = MinWavelength;
        if (wavelength > MaxWavelength) wavelength = MaxWavelength;
        return _maxValues[wavelength - MinWavelength];
    }

    // Get all wavelengths available
    public IEnumerable<int> GetWavelengths()
    {
        for (int wl = MinWavelength; wl <= MaxWavelength; wl++)
            yield return wl;
    }
}

// Class to represent a single material's data
public class MaterialData
{
    public string Id { get; }
    public string Name { get; }
    public string? VReflectance { get; }
    public string? MReflectance { get; }
    public float MPRatio { get; }
    public float LStar { get; }
    public float AStar { get; }
    public float BStar { get; }
    public float RReflectance { get; }
    public float GReflectance { get; }
    public float BReflectance { get; }
    public float Specularity { get; }
    public float Roughness { get; }
    public SpectralData? SpectralData { get; }

    /// <summary>
    /// How this material interacts with light. Measured maze materials are
    /// <see cref="SurfaceKind.Diffuse"/>; optical materials (glass, water,
    /// mirrors, …) set this so the integrator branches into the corresponding
    /// effect (spectral-effects-plan.md §0.1).
    /// </summary>
    public SurfaceKind Surface { get; }

    /// <summary>Fraction of light transmitted through the surface (0 = opaque).</summary>
    public float Transmission { get; }

    /// <summary>Cauchy coefficient <c>A</c> — the base index of refraction (dimensionless).</summary>
    public float CauchyA { get; }

    /// <summary>
    /// Cauchy coefficient <c>B</c> (in µm²) — the dispersion term in
    /// <c>n(λ) = A + B/λ²</c>. Zero means a non-dispersive medium.
    /// </summary>
    public float CauchyB { get; }

    /// <summary>
    /// Base (wavelength-flat) Beer–Lambert absorption coefficient σ (per world unit) for
    /// light travelling inside the medium — a uniform darkening added on top of the coloured
    /// <see cref="AbsorptionRgb"/>. 0 for non-absorbing media.
    /// </summary>
    public float ExtinctionSigma { get; }

    /// <summary>
    /// Coloured Beer–Lambert absorption: the σ (per world unit) at the red (630 nm), green
    /// (532 nm), and blue (465 nm) anchors, interpolated across the spectrum by
    /// <see cref="Optics.AbsorptionAt"/>. A coloured glass absorbs the complement of its
    /// tint, so light through it shifts hue and darkens with depth (spectral-effects-plan
    /// §2.3). Default (0,0,0) = clear.
    /// </summary>
    public Vector3 AbsorptionRgb { get; }

    /// <summary>
    /// Thin-film thickness <c>d</c> in nanometres for iridescent surfaces
    /// (<see cref="SurfaceKind.ThinFilm"/> — soap bubble, oil slick, beetle shell).
    /// The reflectance is modulated by two-beam interference across the film
    /// (<c>Optics.ThinFilmReflectance</c>), with the film's index of refraction taken
    /// from <see cref="CauchyA"/>. Zero means no film (spectral-effects-plan.md §2.2).
    /// </summary>
    public float FilmThicknessNm { get; }

    /// <summary>
    /// Amplitude (nm) of a smooth per-position variation added to
    /// <see cref="FilmThicknessNm"/> for thin-film surfaces, so a single flat quad shows
    /// gently-shifting interference colour across it (the swirl of an oil slick) rather
    /// than one flat hue. 0 = uniform film. The variation is <c>Optics.FilmSwirl(x,z)</c>
    /// keyed on world position (spectral-effects-plan §2.2).
    /// </summary>
    public float FilmSpatialAmpNm { get; }

    /// <summary>
    /// Saturation of the thin-film interference in [0,1]: the reflectance factor is pulled
    /// toward its mean (½) by <c>factor = ½ + FilmContrast·(sin²φ − ½)</c>. 1 = full,
    /// vivid interference (default); lower values give a subtler, pastel sheen (an oil
    /// slick rather than a neon rainbow). Only affects <see cref="SurfaceKind.ThinFilm"/>.
    /// </summary>
    public float FilmContrast { get; }

    public bool HasSpectralData => SpectralData != null;

    public MaterialData(
        string id,
        string name,
        string? vReflectance,
        string? mReflectance,
        float mpRatio,
        float lStar,
        float aStar,
        float bStar,
        float rReflectance,
        float gReflectance,
        float bReflectance,
        float specularity,
        float roughness,
        SpectralData? spectralData = null,
        SurfaceKind surface = SurfaceKind.Diffuse,
        float transmission = 0f,
        float cauchyA = 1f,
        float cauchyB = 0f,
        float extinctionSigma = 0f,
        float filmThicknessNm = 0f,
        float filmSpatialAmpNm = 0f,
        float filmContrast = 1f,
        Vector3 absorptionRgb = default)
    {
        Id = id;
        Name = name;
        VReflectance = vReflectance;
        MReflectance = mReflectance;
        MPRatio = mpRatio;
        LStar = lStar;
        AStar = aStar;
        BStar = bStar;
        RReflectance = rReflectance;
        GReflectance = gReflectance;
        BReflectance = bReflectance;
        Specularity = specularity;
        Roughness = roughness;
        SpectralData = spectralData;
        Surface = surface;
        Transmission = transmission;
        CauchyA = cauchyA;
        CauchyB = cauchyB;
        ExtinctionSigma = extinctionSigma;
        FilmThicknessNm = filmThicknessNm;
        FilmSpatialAmpNm = filmSpatialAmpNm;
        FilmContrast = filmContrast;
        AbsorptionRgb = absorptionRgb;
    }

    // Convenience property to get the RGB reflectance as a Vector3
    public Vector3 RGBReflectance => new Vector3(RReflectance, GReflectance, BReflectance);

    // Convenience property to get the Lab color as a Vector3
    public Vector3 Lab => new Vector3(LStar, AStar, BStar);

    // Get spectral reflectance at a specific wavelength
    public float GetSpectralReflectance(int wavelength, bool includeSpecular = true)
    {
        if (SpectralData == null)
            return 0f; // Or return RGB-based approximation if desired

        if (!includeSpecular)
            return SpectralData.GetSceValue(wavelength);

        // Single-lookup path: precomputed max(SCI, SCE) avoids two
        // separate array accesses on every intersection.
        return SpectralData.GetMaxReflectance(wavelength);
    }

    /// <summary>
    /// Index of refraction at <paramref name="wavelengthNm"/> via Cauchy's
    /// equation <c>n(λ) = A + B/λ²</c> (λ in µm). Non-dispersive materials
    /// (<see cref="CauchyB"/> = 0) return <see cref="CauchyA"/> directly. This is
    /// the sole entry point for dispersion — a wavelength-dependent IOR is what
    /// splits white light through a prism (spectral-effects-plan.md §2.1).
    /// </summary>
    public float IorAt(float wavelengthNm)
    {
        if (CauchyB == 0f)
            return CauchyA;

        float um = wavelengthNm * 1e-3f;
        return CauchyA + CauchyB / (um * um);
    }

    /// <summary>
    /// Beer–Lambert absorption coefficient σ at the given wavelength: the coloured
    /// <see cref="AbsorptionRgb"/> anchors (plus the uniform <see cref="ExtinctionSigma"/>)
    /// interpolated across the spectrum (spectral-effects-plan.md §2.3).
    /// </summary>
    public float ExtinctionAt(int wavelength) => Optics.AbsorptionAt(
        AbsorptionRgb.X + ExtinctionSigma,
        AbsorptionRgb.Y + ExtinctionSigma,
        AbsorptionRgb.Z + ExtinctionSigma,
        wavelength);
}