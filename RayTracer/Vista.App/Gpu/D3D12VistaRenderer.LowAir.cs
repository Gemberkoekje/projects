using System.Numerics;
using System.Runtime.InteropServices;
using Vista.Atmosphere;
using Vista.Rendering;
using Vista.Weather;
using Vista.World;
using Vortice.Direct3D12;

namespace Vista.App.Gpu;

/// <summary>
/// The valleys' pools on the GPU (vista-plan §10 M1.13b): one buffer holding the pool cells of both levels of the map
/// (<see cref="LowAir.Cells"/>), uploaded when the pools change, and after them the two sunlight volumes, built once a render by
/// <c>CSSunlightVolume</c> (<c>VistaLowAir.hlsl</c>, its own root signature) when the pools, the terrain or the sun moved. The
/// integrator reads the buffer through its last root slot (t24) and the pools' layout through the atmosphere's constants.
/// </summary>
internal sealed partial class D3D12VistaRenderer
{
    private const string LowAirShaderFile = "VistaLowAir.hlsl";

    /// <summary>The low air's bindings, by root parameter slot (registers in the comments).</summary>
    private enum LowSlot
    {
        Tlas,       // t0
        Buffer,     // u0
        ProbeOut,   // u1
        ProbeIn,    // t14
        Build,      // b0
        Atmosphere, // b1
    }

    /// <summary>Voxel pairs per volume dispatch: a pair's 48-step march and two rays; fewer on a shared GPU (<see cref="GpuShare"/>).</summary>
    private static readonly int VolumePairsPerDispatch = GpuShare.Capped ? 1 << 15 : 1 << 18;

    private ID3D12RootSignature _lowRootSignature = null!;
    private ID3D12PipelineState? _volumePipeline;
    private ID3D12PipelineState? _lowProbePipeline;
    private ID3D12Resource _lowBuildConstants = null!;
    private ID3D12Resource? _lowAirBuffer;
    private LowAir? _lowAir;
    private (LowAirKey Low, TerrainMesh Mesh, Vector3 Sun, double Ground)? _volumeKey;

    [StructLayout(LayoutKind.Sequential)]
    private struct LowAirBuildConstants
    {
        public float CamWorldX, CamWorldZ;
        public uint Level, FirstPair;
        public uint PairCount, SunSteps;
        public float SunReach;
        public uint UpSteps;
        public uint ProbeCount, Pad0, Pad1, Pad2;
    }

    private void CreateLowAirPipelines()
    {
        var rootParameters = new[] { Srv(0), Uav(0), Uav(1), Srv(14), Cbv(0), Cbv(1) }; // see LowSlot
        _lowRootSignature = _device.CreateRootSignature(new RootSignatureDescription1(RootSignatureFlags.None, rootParameters));
        _lowBuildConstants = CreateBuffer(HeapType.Upload, ConstantBufferBytes, ResourceStates.GenericRead);
    }

    /// <summary>Puts <paramref name="low"/>'s cells on the GPU when they are not there yet (null: no pools); the volumes follow later.</summary>
    private void EnsureLowAirCells(LowAir? low)
    {
        if (ReferenceEquals(low, _lowAir) || (low is not null && _lowAir is not null && low.Key == _lowAir.Key))
            return;
        DisposeLowAir();
        if (low is null)
            return;
        _lowAirBuffer = CreateUavBuffer((ulong)low.BufferElements * 16);
        using ID3D12Resource staging = CreateUploadBuffer<uint>(low.Cells);
        using ID3D12Resource ceilings = CreateUploadBuffer<float>(low.Ceilings);
        _allocator.Reset();
        _commandList.Reset(_allocator, null);
        _commandList.CopyBufferRegion(_lowAirBuffer, 0, staging, 0, (ulong)low.Cells.Length * sizeof(uint));
        _commandList.CopyBufferRegion(_lowAirBuffer, (ulong)low.CeilingOffset * 16, ceilings, 0, (ulong)low.Ceilings.Length * sizeof(float));
        _commandList.Close();
        _queue.ExecuteCommandList(_commandList);
        WaitForGpu();
        _lowAir = low;
    }

    /// <summary>The low air's rows of the atmosphere's constants: the pools' layout, their noise and the march's settings; all zero without pools.</summary>
    private void FillLowAirConstants(ref AtmosphereConstants c)
    {
        LowAir? low = _lowAir;
        if (low is null)
            return;
        LowAirSettings s = low.Settings;
        PoolLevel l0 = low.Level0;
        PoolLevel? l1 = low.Level1;
        c.LowMode = 1u;
        c.LowLevels = l1 is null ? 1u : 2u;
        c.LowNoiseSeed = low.NoiseSeed;
        (c.LowVolumeLayers, c.VolumePoolLayers) = ((uint)s.VolumeLayers, (uint)s.VolumePoolLayers);
        (c.Level0OriginX, c.Level0OriginZ, c.Level0Cell, c.Level0Size) = ((float)l0.OriginX, (float)l0.OriginZ, (float)l0.CellMetres, l0.Size);
        if (l1 is not null)
            (c.Level1OriginX, c.Level1OriginZ, c.Level1Cell, c.Level1Size) = ((float)l1.OriginX, (float)l1.OriginZ, (float)l1.CellMetres, l1.Size);
        c.Level1Offset = (uint)low.Level1Offset;
        c.Volume0Offset = (uint)low.VolumeOffset(0);
        c.Volume1Offset = (uint)low.VolumeOffset(1);
        c.VolumeRatio = (uint)s.VolumeRatio;
        (c.SlabTop, c.SlabBottom, c.HazeMax, c.MistMax) = (low.SlabTop, low.SlabBottom, low.HazeMax, low.MistMax);
        (c.BlendMetres, c.HazeSoftness, c.VolumeMargin, c.MistG) = ((float)s.BlendMetres, (float)s.HazeSoftnessMetres, (float)s.VolumeMarginMetres, (float)s.MistAsymmetry);
        (c.WispAmplitude, c.WispScale, c.WispVertical, c.PatchStrength) =
            ((float)s.WispAmplitudeMetres, (float)s.WispScaleMetres, (float)s.WispVerticalMetres, (float)s.PatchStrength);
        (c.PatchScale, c.MistWaterLimit, c.MistAlbedo, c.MarchStep) =
            ((float)s.PatchScaleMetres, (float)ValleyPools.MistWaterLimit, (float)s.MistAlbedo, (float)s.MarchStepMetres);
        (c.MsA, c.MsB, c.MsC, c.MsOctaves) = ((float)s.OctaveA, (float)s.OctaveB, (float)s.OctaveC, (uint)s.Octaves);
        c.MarchMaxSteps = (uint)s.MarchMaxSteps;
        c.Ambient = (float)s.Ambient;
        c.CeilingOffset = (uint)low.CeilingOffset;
        c.CeilingBlock = (uint)s.CeilingBlock;
        (c.MarchGrowth, c.BounceSteps, c.BounceStep) = ((float)s.MarchGrowth, (uint)s.BounceMaxSteps, (float)s.BounceStepMetres);
        c.MistSoftness = (float)s.MistSoftnessMetres;
        (c.MsForward, c.MsForwardPower) = ((float)s.ForwardLobeWeight, (float)s.ForwardLobeSunPower);
        (c.MsDiffuse, c.MsDiffusePower) = ((float)s.DiffuseWeight, (float)s.DiffuseSunPower);
        (c.SkySteps, c.SkyGrowth) = ((uint)s.SkyMaxSteps, (float)s.SkyGrowth);
        (c.MistStep, c.MistBandStep) = ((float)s.MistStepDepth, (float)s.MistBandStepMetres);
        (c.Fray, c.ShadowedScattering) = ((float)s.FrayMetres, (float)s.ShadowedScattering);
    }

    /// <summary>
    /// Builds the sunlight volumes for <paramref name="scene"/> when its pools, its terrain or its sun changed since the last build (the
    /// atmosphere's constants and the acceleration structure must be this scene's); returns the GPU seconds (0 when nothing was built).
    /// </summary>
    private double EnsureSunlightVolume(VistaScene scene, AtmosphereFrame frame)
    {
        LowAir? low = _lowAir;
        if (low is null || _tlas is null)
            return 0.0;
        var key = (low.Key, scene.Mesh, new Vector3(frame.ToSun.X, frame.ToSun.Y, frame.ToSun.Z), frame.GroundAltitudeMetres);
        if (_volumeKey == key)
            return 0.0;
        _volumePipeline ??= CompilePipeline(_lowRootSignature, ShaderSource.Load(LowAirShaderFile), "CSSunlightVolume", LowAirShaderFile);
        double seconds = 0.0;
        for (int level = 0; level < (low.Level1 is null ? 1 : 2); level++)
        {
            int pairs = (low.VolumeVoxels(level == 0 ? low.Level0 : low.Level1!) + 1) / 2;
            for (int first = 0; first < pairs; first += VolumePairsPerDispatch)
            {
                int count = Math.Min(VolumePairsPerDispatch, pairs - first);
                Write(_lowBuildConstants, new LowAirBuildConstants
                {
                    CamWorldX = (float)scene.Mesh.Frame.TangentX,
                    CamWorldZ = (float)scene.Mesh.Frame.TangentZ,
                    Level = (uint)level,
                    FirstPair = (uint)first,
                    PairCount = (uint)count,
                    SunSteps = (uint)low.Settings.SunSteps,
                    SunReach = (float)low.Settings.SunReachMetres,
                    UpSteps = (uint)low.Settings.UpSteps,
                });
                seconds += Timed(_volumePipeline, _lowRootSignature, list =>
                {
                    BindLowAir(list, null, null);
                    list.Dispatch((uint)((count + LutGroupSize - 1) / LutGroupSize), 1, 1);
                });
            }
        }
        _volumeKey = key;
        return seconds;
    }

    private void BindLowAir(ID3D12GraphicsCommandList4 list, ID3D12Resource? probeOut, ID3D12Resource? probeIn)
    {
        list.SetComputeRootShaderResourceView((uint)LowSlot.Tlas, (_tlas ?? _dummyUav).GPUVirtualAddress);
        list.SetComputeRootUnorderedAccessView((uint)LowSlot.Buffer, (_lowAirBuffer ?? _dummyUav).GPUVirtualAddress);
        list.SetComputeRootUnorderedAccessView((uint)LowSlot.ProbeOut, (probeOut ?? _dummyUav).GPUVirtualAddress);
        list.SetComputeRootShaderResourceView((uint)LowSlot.ProbeIn, (probeIn ?? _dummySrv).GPUVirtualAddress);
        list.SetComputeRootConstantBufferView((uint)LowSlot.Build, _lowBuildConstants.GPUVirtualAddress);
        list.SetComputeRootConstantBufferView((uint)LowSlot.Atmosphere, _atmosphereConstants.GPUVirtualAddress);
    }

    /// <summary>
    /// The low air at world places on the GPU (<c>CSLowAirProbe</c>), for the self-test's parity with <see cref="LowAir"/>: for each
    /// place (x, altitude, z), the pool there (floor, depth, mist, haze at the floor), the density and the noise (haze, mist, wisp,
    /// patch), the sunlight volume there (shadow, haze delta, mist toward the sun, mist up) and the volume's columns from there (haze
    /// delta, mist toward the sun, mist up, 0): sixteen floats a place. The scene's pools, atmosphere and volume are made first.
    /// </summary>
    public float[] RunLowAirProbe(VistaScene scene, IReadOnlyList<(float X, float Altitude, float Z)> places)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(places);
        if (places.Count == 0)
            throw new ArgumentException("No places.", nameof(places));
        if (scene.LowAir is null)
            throw new ArgumentException("The scene has no pools.", nameof(scene));
        PrepareLowAir(scene);
        _lowProbePipeline ??= CompilePipeline(_lowRootSignature, ShaderSource.Load(LowAirShaderFile), "CSLowAirProbe", LowAirShaderFile);
        var packed = new float[places.Count * 4];
        for (int i = 0; i < places.Count; i++)
            (packed[i * 4], packed[i * 4 + 1], packed[i * 4 + 2], packed[i * 4 + 3]) = (places[i].X, places[i].Altitude, places[i].Z, -1f);
        Write(_lowBuildConstants, new LowAirBuildConstants
        {
            CamWorldX = (float)scene.Mesh.Frame.TangentX,
            CamWorldZ = (float)scene.Mesh.Frame.TangentZ,
            SunSteps = (uint)scene.LowAir.Settings.SunSteps,
            SunReach = (float)scene.LowAir.Settings.SunReachMetres,
            UpSteps = (uint)scene.LowAir.Settings.UpSteps,
            ProbeCount = (uint)places.Count,
        });
        using ID3D12Resource input = CreateDefaultBuffer<float>(packed);
        using ID3D12Resource output = CreateUavBuffer((ulong)places.Count * 16 * sizeof(float));
        Timed(_lowProbePipeline, _lowRootSignature, list =>
        {
            BindLowAir(list, output, input);
            list.Dispatch((uint)((places.Count + LutGroupSize - 1) / LutGroupSize), 1, 1);
        });
        return ReadFloats(output, places.Count * 16);
    }

    /// <summary>The scene's mesh, pools, atmosphere and sunlight volume on the GPU, as a render makes them, for the probes.</summary>
    private void PrepareLowAir(VistaScene scene)
    {
        if (!ReferenceEquals(scene.Mesh, _mesh))
        {
            DisposeScene();
            CheckFits(scene.Mesh);
            UploadMesh(scene.Mesh);
            EnsureNormalMaps(scene.Mesh.NormalMaps);
            EnsureMaterials(scene.Mesh.Materials);
            BuildAccelerationStructures(scene.Mesh);
            _mesh = scene.Mesh;
        }
        EnsureLowAirCells(scene.LowAir);
        AtmosphereFrame frame = scene.AirFrame();
        EnsureAtmosphere(frame, scene.Wavelengths());
        EnsureSunlightVolume(scene, frame);
    }

    /// <summary>The sunlight volume over <paramref name="level"/> as built, four floats a voxel (shadow, haze delta, mist toward the sun, mist up), for the self-test.</summary>
    public float[] ReadSunlightVolume(VistaScene scene, int level)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (scene.LowAir is null)
            throw new ArgumentException("The scene has no pools.", nameof(scene));
        PrepareLowAir(scene);
        LowAir low = _lowAir!;
        int voxels = low.VolumeVoxels(level == 0 ? low.Level0 : low.Level1 ?? throw new ArgumentOutOfRangeException(nameof(level)));
        int elements = (voxels + 1) / 2;
        ulong bytes = (ulong)elements * 16;
        using ID3D12Resource readback = CreateBuffer(HeapType.Readback, bytes, ResourceStates.CopyDest);
        _allocator.Reset();
        _commandList.Reset(_allocator, null);
        _commandList.CopyBufferRegion(readback, 0, _lowAirBuffer!, (ulong)low.VolumeOffset(level) * 16, bytes);
        _commandList.Close();
        _queue.ExecuteCommandList(_commandList);
        WaitForGpu();
        var words = new uint[elements * 4];
        readback.Map<uint>(0, words.Length).CopyTo(words);
        readback.Unmap(0);
        var result = new float[voxels * 4];
        for (int v = 0; v < voxels; v++)
        {
            uint a = words[v / 2 * 4 + (v % 2) * 2], b = words[v / 2 * 4 + (v % 2) * 2 + 1];
            result[v * 4] = (float)BitConverter.UInt16BitsToHalf((ushort)(a & 0xFFFF));
            result[v * 4 + 1] = (float)BitConverter.UInt16BitsToHalf((ushort)(a >> 16));
            result[v * 4 + 2] = (float)BitConverter.UInt16BitsToHalf((ushort)(b & 0xFFFF));
            result[v * 4 + 3] = (float)BitConverter.UInt16BitsToHalf((ushort)(b >> 16));
        }
        return result;
    }

    private void DisposeLowAir()
    {
        _lowAirBuffer?.Dispose();
        (_lowAirBuffer, _lowAir, _volumeKey) = (null, null, null);
    }

    private void DisposeLowAirPipelines()
    {
        DisposeLowAir();
        _lowProbePipeline?.Dispose();
        _volumePipeline?.Dispose();
        _lowBuildConstants?.Dispose();
        _lowRootSignature?.Dispose();
    }
}
