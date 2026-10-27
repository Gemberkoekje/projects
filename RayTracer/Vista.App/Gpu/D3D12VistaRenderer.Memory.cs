using Vista.Atmosphere;
using Vista.Rendering;
using Vista.World;
using Vortice.Direct3D12;
using Vortice.DXGI;

namespace Vista.App.Gpu;

/// <summary>
/// The video memory of a render (vista-plan §7.1, §10 M1.15): the plan before anything is allocated, from the sizes of the buffers the
/// render would make, and the peak the render did take, read after each of its allocations and polled while it runs.
/// </summary>
internal sealed partial class D3D12VistaRenderer
{
    /// <summary>How often the poll reads the process's video memory while a render runs, ms.</summary>
    private const int PollMs = 2;

    private long _baseVideoMemory;
    private long _peakVideoMemory;

    /// <summary>DXGI's budget for the process now, bytes (0 without an adapter that reports it).</summary>
    private long VideoMemoryBudget => _adapter is null ? 0 : (long)_adapter.QueryVideoMemoryInfo(0, MemorySegmentGroup.Local).Budget;

    /// <summary>
    /// What the process holds once the device, its pipelines and fixed tables exist: a plan's base. Called by the constructor, after a
    /// build of one triangle: the driver takes memory of its own at the first acceleration structure it builds and keeps it (about 45 MiB
    /// on the RTX 3070, the same for every mesh), which a plan from the buffers alone missed.
    /// </summary>
    private void MeasureBaseMemory()
    {
        _positions = CreateDefaultBuffer<float>([0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 1f]);
        _indices = CreateDefaultBuffer<uint>([0u, 1u, 2u]);
        BuildAccelerationStructures(3, 3);
        DisposeScene();
        _baseVideoMemory = VideoMemoryUsed;
    }

    /// <summary>Reads the process's video memory and keeps the most seen since the render began.</summary>
    private void NoteMemory()
    {
        long used = VideoMemoryUsed;
        long seen;
        do
        {
            seen = Interlocked.Read(ref _peakVideoMemory);
            if (used <= seen)
                return;
        }
        while (Interlocked.CompareExchange(ref _peakVideoMemory, used, seen) != seen);
    }

    /// <summary>
    /// Starts a render's peak: the memory now, then a background poll every <see cref="PollMs"/> until the returned handle is disposed,
    /// beside the explicit reads after each allocation (<see cref="NoteMemory"/>). The poll only reads DXGI's counters.
    /// </summary>
    private IDisposable WatchMemory()
    {
        Interlocked.Exchange(ref _peakVideoMemory, 0);
        NoteMemory();
        if (_adapter is null)
            return new Poll(null);
        var stop = new ManualResetEventSlim(false);
        var thread = new Thread(() =>
        {
            while (!stop.Wait(PollMs))
                NoteMemory();
        })
        { IsBackground = true, Name = "video memory poll", Priority = ThreadPriority.BelowNormal };
        thread.Start();
        return new Poll(() =>
        {
            stop.Set();
            thread.Join();
            stop.Dispose();
            NoteMemory();
        });
    }

    private sealed class Poll(Action? stop) : IDisposable
    {
        public void Dispose() => stop?.Invoke();
    }

    /// <inheritdoc />
    public MemoryPlan PlanMemory(VistaScene scene, RenderSettings settings, IReadOnlySet<Aov> aovs)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(aovs);
        var items = new List<MemoryItem>();
        void Add(string name, long bytes, MemoryStage from, MemoryStage until = MemoryStage.Trace)
        {
            if (bytes > 0)
                items.Add(new MemoryItem(name, MemoryPlan.Page(bytes), from, until));
        }

        // The mesh, as UploadMesh, EnsureNormalMaps and EnsureMaterials make it.
        TerrainMesh mesh = scene.Mesh;
        Add("meshPositions", (long)mesh.Positions.Length * sizeof(float), MemoryStage.Mesh);
        Add("meshNormals", (long)mesh.Normals.Length * sizeof(float), MemoryStage.Mesh);
        Add("meshIndices", (long)mesh.Indices.Length * sizeof(uint), MemoryStage.Mesh);
        Add("meshMapNormals", (long)mesh.MapNormals.Length * sizeof(uint), MemoryStage.Mesh);
        foreach (NormalMap map in mesh.NormalMaps.Take(2))
            Add("normalMaps", (long)map.Packed.Length * sizeof(uint), MemoryStage.Mesh);
        if (mesh.Materials is { } field)
        {
            Add("materialMap", (long)field.Packed.Length * sizeof(uint), MemoryStage.Mesh);
            Add("bedTable", (long)field.Beds.Length * sizeof(float), MemoryStage.Mesh);
        }

        // The acceleration structures, as BuildAccelerationStructures makes them: their scratch is freed when the build ends.
        RaytracingAccelerationStructurePrebuildInfo blas = _device.GetRaytracingAccelerationStructurePrebuildInfo(BlasInputs(mesh, 0, 0));
        RaytracingAccelerationStructurePrebuildInfo tlas = _device.GetRaytracingAccelerationStructurePrebuildInfo(TlasInputs(0));
        Add("blas", (long)blas.ResultDataMaxSizeInBytes, MemoryStage.Acceleration);
        Add("tlas", (long)tlas.ResultDataMaxSizeInBytes, MemoryStage.Acceleration);
        Add("blasScratch", (long)blas.ScratchDataSizeInBytes, MemoryStage.Acceleration, MemoryStage.Acceleration);
        Add("tlasScratch", (long)tlas.ScratchDataSizeInBytes, MemoryStage.Acceleration, MemoryStage.Acceleration);

        // The air: its wavelength tables, the valleys' pools with their volumes, and the scattering orders' scratch (fixed LUTs are
        // in the base).
        WavelengthTable wavelengths = scene.Wavelengths();
        AtmosphereTables air = AtmosphereTables.Build(scene.Air, wavelengths);
        Add("atmosphereCoefficients", (long)air.Coefficients.Length * sizeof(float), MemoryStage.Air);
        Add("atmosphereSun", (long)air.Sun.Length * sizeof(float), MemoryStage.Air);
        if (scene.LowAir is { } low)
            Add("lowAir", (long)low.BufferElements * 16, MemoryStage.Air);
        long items4d = AtmosphereScattering.FloatsPerChannel, itemsGround = AtmosphereScattering.IrradianceFloats;
        Add("ordersSingle", items4d * 2 * sizeof(float), MemoryStage.Air, MemoryStage.Air);
        Add("ordersDensity", items4d * 2 * sizeof(float), MemoryStage.Air, MemoryStage.Air);
        Add("ordersRadiance", items4d * sizeof(float), MemoryStage.Air, MemoryStage.Air);
        Add("ordersIrradiance", itemsGround * sizeof(float), MemoryStage.Air, MemoryStage.Air);

        // The trace: the surface tables, the image buffers (EnsureImageBuffers), and the sky's distribution with its measurement.
        IntegratorTables tables = IntegratorTables.Build(scene.Materials, wavelengths);
        Add("cie", (long)tables.Cie.Length * sizeof(float), MemoryStage.Trace);
        Add("reflectance", (long)tables.Reflectance.Length * sizeof(float), MemoryStage.Trace);
        Add("albedo", (long)tables.Albedo.Length * sizeof(float), MemoryStage.Trace);
        long pixels = settings.Rendered.Pixels;
        uint mask = MaskFor(aovs);
        Add("imageXyz", pixels * IntegratorReadback.XyzSumStride * sizeof(float), MemoryStage.Trace);
        if ((mask & AovVariance) != 0)
            Add("imageVariance", pixels * IntegratorReadback.LumaStatsStride * sizeof(float), MemoryStage.Trace);
        if ((mask & AovAlbedo) != 0)
            Add("imageAlbedo", pixels * IntegratorReadback.VectorSumStride * sizeof(float), MemoryStage.Trace);
        if ((mask & AovNormal) != 0)
            Add("imageNormal", pixels * IntegratorReadback.VectorSumStride * sizeof(float), MemoryStage.Trace);
        if ((mask & AovFirstHit) != 0)
            Add("imageFirstHit", pixels * IntegratorReadback.FirstHitStride * sizeof(float), MemoryStage.Trace);
        if (settings.LightSampling == LightSampling.Mis && settings.Atmosphere == AtmosphereMode.Luts)
        {
            Add("skyDistribution", (long)SkyDistribution.FloatCount * sizeof(float), MemoryStage.Trace);
            Add("skyLuminance", (long)SkyDistribution.Cells * AtmosphereLuts.SkyTerrainSlices * sizeof(float), MemoryStage.Trace, MemoryStage.Trace);
        }
        return new MemoryPlan(_baseVideoMemory, VideoMemoryBudget, items);
    }
}
