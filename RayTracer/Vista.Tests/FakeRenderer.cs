using Vista.Atmosphere;
using Vista.Rendering;
using Vista.World;

namespace Vista.Tests;

/// <summary>
/// A stand-in for the GPU: drives the planner like the real host must and returns synthetic buffers of the
/// rendered window's size. A pixel's XYZ is a function of its place in the full frame, so a loupe's pixels
/// equal the same pixels of a full render, as the real integrator's do. It keeps its stages the way the D3D12 host does
/// (the device, the mesh's acceleration structure, the air's tables, orders and sky view, the sky distribution) and reports
/// those it built under <see cref="RenderStageKeys"/>. The incident meter's card gets flat spectra: the sky's always, the sun's
/// while it is up.
/// </summary>
internal sealed class FakeRenderer : IVistaRenderer
{
    public const string Name = "Fake GPU";

    private bool _initialised;
    private TerrainMesh? _mesh;
    private AtmosphereModel? _model;
    private (AtmosphereModel Model, double Ground)? _orders;
    private AtmosphereFrame? _skyView;
    private AtmosphereFrame? _skyLight;

    public Aov? Skip { get; init; }

    /// <summary>
    /// When set, the image also depends on what the real renderer reads of the scene and the settings (the camera without its
    /// exposure, the sun, the base altitude, the terrain, the samples), so a render reused when it should not have been shows.
    /// </summary>
    public bool SceneDependent { get; init; }

    public BatchPlanner? Planner { get; private set; }

    public IReadOnlySet<Aov>? Requested { get; private set; }

    public VistaScene? Scene { get; private set; }

    public RenderSettings? Settings { get; private set; }

    /// <summary>Every scene rendered, in order.</summary>
    public List<VistaScene> Scenes { get; } = [];

    public string DeviceName => Name;

    /// <summary>Exposure-meter renders (<see cref="RenderSettings.ExposureMeter"/>), kept apart from the image renders above.</summary>
    public List<RenderSettings> Meters { get; } = [];

    /// <summary>The scenes the incident meter's light was measured for, in order.</summary>
    public List<VistaScene> Incidents { get; } = [];

    /// <summary>The sky's flat spectral irradiance on the incident meter's card, W·m⁻²·nm⁻¹ (the card's illuminance in Y with no sun).</summary>
    public const double IncidentSky = 0.25;

    /// <summary>The sun's flat spectral irradiance on the card while the sun is up (with <see cref="SceneDependent"/>, times its elevation's sine).</summary>
    public const double IncidentSun = 2.0;

    public IncidentLight MeasureIncidentLight(VistaScene scene)
    {
        Incidents.Add(scene);
        double sun = scene.Sun.ElevationDegrees <= 0.0 ? 0.0 : SceneDependent ? IncidentSun * Math.Sin(scene.Sun.ElevationDegrees * Math.PI / 180.0) : IncidentSun;
        SortedDictionary<string, double> stages = AirStages(scene, LightSampling.SunOnly, AtmosphereMode.Luts);
        stages[RenderStageKeys.IncidentMeter] = 0.0001;
        return new IncidentLight(Flat(sun), Flat(IncidentSky)) { StageSeconds = stages };
    }

    private static double[] Flat(double value) => [.. Enumerable.Repeat(value, AtmosphereLuts.Wavelengths)];

    public RenderOutput Render(VistaScene scene, RenderSettings settings, IReadOnlySet<Aov> aovs, BatchPlanner planner)
    {
        if (settings.ExposureMeter)
        {
            Meters.Add(settings);
        }
        else
        {
            Scene = scene;
            Scenes.Add(scene);
            Settings = settings;
            Planner = planner;
            Requested = aovs;
        }
        PixelWindow window = settings.Rendered;
        Assert.AreEqual(window.Width, planner.Width, "the planner is sized to the rendered window");
        Assert.AreEqual(window.Height, planner.Height, "the planner is sized to the rendered window");
        int dispatches = 0;
        while (planner.HasNext)
        {
            SampleBatch batch = planner.Next();
            planner.Complete(batch, TimeSpan.FromMilliseconds(1));
            dispatches++;
        }

        int pixels = window.Pixels;
        var xyz = new float[pixels * 3];
        float k = SceneDependent ? Signature(scene, settings) : 1f;
        for (int p = 0; p < pixels; p++)
        {
            int frameX = window.X + p % window.Width;
            int frameY = window.Y + p / window.Width;
            float t = (frameY * settings.Width + frameX) / (float)(settings.Width * settings.Height);
            xyz[p * 3] = 0.95f * t * k;
            xyz[p * 3 + 1] = t * k;
            xyz[p * 3 + 2] = 1.09f * t * k;
        }
        var buffers = new Dictionary<Aov, float[]>();
        foreach (Aov aov in aovs.Where(a => a != Skip))
        {
            var data = new float[pixels * AovLayout.Channels(aov)];
            for (int i = 0; i < data.Length; i++)
                data[i] = (i % 7) * 0.1f + 0.05f;
            if (aov == Aov.MaterialId)
            {
                for (int i = 1; i < data.Length; i += 2)
                    data[i] = -1f; // every other pixel is sky
                for (int i = 0; i < data.Length; i += 2)
                    data[i] = 0f;  // the rest is terrain
            }
            buffers[aov] = data;
        }
        long pixelSamples = (long)pixels * settings.SamplesPerPixel;
        return new RenderOutput(window.Width, window.Height, xyz, buffers, new RenderStats
        {
            RaysTraced = (ulong)(pixelSamples * 3),
            PixelSamples = pixelSamples,
            Dispatches = dispatches,
            TraceSeconds = dispatches * 0.001,
            StageSeconds = Stages(scene, settings),
        });
    }

    /// <summary>A brightness in [1, 2) from everything a real render reads.</summary>
    private static float Signature(VistaScene scene, RenderSettings settings)
    {
        double h = scene.Camera.Position.X * 1.3e-4 + scene.Camera.Position.Y * 2.9e-4 + scene.Camera.Position.Z * 3.7e-4
            + scene.Camera.YawDegrees * 1.1e-2 + scene.Camera.PitchDegrees * 1.7e-2 + scene.Camera.FocalLengthMm * 1.9e-2
            + scene.Sun.ElevationDegrees * 2.3e-2 + scene.Sun.AzimuthDegrees * 2.9e-3 + scene.GroundAltitudeMetres * 3.1e-5
            + scene.Terrain.MaxHeight * 4.1e-5 + settings.SamplesPerPixel * 4.3e-3 + settings.MaxBounces * 4.7e-2;
        return 1f + (float)(h - Math.Floor(h));
    }

    /// <summary>What a D3D12 host would build for this render, given what it built before.</summary>
    private SortedDictionary<string, double> Stages(VistaScene scene, RenderSettings settings)
    {
        SortedDictionary<string, double> stages = AirStages(scene, settings.LightSampling, settings.Atmosphere);
        if (!ReferenceEquals(scene.Mesh, _mesh))
        {
            stages[RenderStageKeys.MeshUpload] = 0.01;
            stages[RenderStageKeys.AccelerationBuild] = 0.01;
            _mesh = scene.Mesh;
        }
        return stages;
    }

    /// <summary>The device and the air a D3D12 host would build for <paramref name="scene"/>, given what it built before.</summary>
    private SortedDictionary<string, double> AirStages(VistaScene scene, LightSampling lightSampling, AtmosphereMode atmosphere)
    {
        var stages = new SortedDictionary<string, double>(StringComparer.Ordinal);
        if (!_initialised)
        {
            stages[RenderStageKeys.DeviceInit] = 0.1;
            stages[RenderStageKeys.ShaderCompile] = 0.2;
            _initialised = true;
        }
        AtmosphereFrame frame = scene.AirFrame();
        if (!Equals(_model, frame.Model))
        {
            stages[RenderStageKeys.AtmosphereTables] = 0.001;
            (_model, _orders, _skyView) = (frame.Model, null, null);
        }
        if (_orders != (frame.Model, frame.GroundAltitudeMetres))
        {
            stages[RenderStageKeys.ScatteringOrders] = 0.01;
            (_orders, _skyView) = ((frame.Model, frame.GroundAltitudeMetres), null);
        }
        if (!Equals(_skyView, frame))
        {
            stages[RenderStageKeys.SkyView] = 0.001;
            _skyView = frame;
        }
        if (lightSampling == LightSampling.Mis && atmosphere == AtmosphereMode.Luts && !Equals(_skyLight, frame))
        {
            stages[RenderStageKeys.SkyDistribution] = 0.001;
            _skyLight = frame;
        }
        return stages;
    }
}
