using Vista.Rendering;
using Vista.World;
using Vortice.Direct3D12;

namespace Vista.App.Gpu;

/// <summary>
/// The ground's materials on the GPU (vista-plan §10 M1.12): the world's material field (<see cref="MaterialField"/>: the packed
/// maps, two words a sample, and the bed table), uploaded once per world like the normal maps, and the rules' constants, written
/// per render (<see cref="MaterialConstants"/>, the shader's <c>MaterialConstants</c> cbuffer).
/// </summary>
internal sealed partial class D3D12VistaRenderer
{
    private ID3D12Resource _materialConstants = null!;
    private MaterialField? _materialField;
    private ID3D12Resource? _materialMap;
    private ID3D12Resource? _bedTable;
    private ID3D12PipelineState? _materialProbePipeline;

    /// <summary>The material constants' buffer: two 256-byte blocks, since the forest's rows (vista-plan §10 M1.12b) took it past one.</summary>
    private const int MaterialConstantBytes = 512;

    private void CreateMaterialResources() =>
        _materialConstants = CreateBuffer(HeapType.Upload, MaterialConstantBytes, ResourceStates.GenericRead);

    /// <summary>Uploads the world's material field when it is not the one already on the GPU; none for M0's single material.</summary>
    private void EnsureMaterials(MaterialField? field)
    {
        if (ReferenceEquals(field, _materialField))
            return;
        DisposeMaterials();
        if (field is null)
            return;
        _materialMap = CreateDefaultBuffer<uint>(field.Packed);
        _bedTable = CreateDefaultBuffer<float>(field.Beds);
        _materialField = field;
    }

    /// <summary>
    /// The material constants of <paramref name="scene"/> on a frame <paramref name="frameHeight"/> pixels high: the rules under its
    /// grass line, tree line and pole, its world's map and beds, the camera's place and pixel for the bump, rough diffuse surfaces when
    /// <paramref name="diffuse"/> asks for them and the canopy's kernels when <paramref name="canopy"/> does; M0's single Lambert
    /// material without a field.
    /// </summary>
    internal static MaterialConstants MaterialConstantsFor(VistaScene scene, DiffuseModel diffuse, CanopyModel canopy, int frameHeight)
    {
        MaterialField? field = scene.Mesh.Materials;
        if (field is null)
            return new MaterialConstants();
        MaterialConstants k = field.Apply(MaterialRules.Default.Constants((float)scene.Ground.GrassLineMetres, scene.Ground.Poleward, field.NoiseSeed,
            (float)scene.Ground.TreeLineMetres, scene.Ground.Forest));
        double pixelAngle = 2.0 * Math.Tan(0.5 * scene.Camera.VerticalFovRadians()) / frameHeight;
        return k.ForCamera(scene.Mesh.Frame.TangentX, scene.Mesh.Frame.TangentZ, pixelAngle) with
        {
            Mode = 1u,
            Diffuse = diffuse == DiffuseModel.OrenNayar ? 1u : 0u,
            Canopy = canopy == CanopyModel.Kernels ? 1u : 0u,
        };
    }

    /// <summary>
    /// The ground's materials at world places on the GPU (<c>CSMaterialProbe</c>), for the self-test's parity with
    /// <see cref="MaterialField.Inputs"/> and <see cref="MaterialRules.Evaluate"/>: at each place's x, z and height, with the world
    /// grid's normal there, the weights of rock, scree, soil and grass, then the wetness, the stoniness, the bed's hardness and the
    /// slope, then the forest's weight and three zeros, twelve floats a place.
    /// </summary>
    public float[] RunMaterialProbe(VistaScene scene, IReadOnlyList<(float X, float Z, float Height)> places)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(places);
        if (places.Count == 0)
            throw new ArgumentException("No places.", nameof(places));
        if (scene.Mesh.Materials is null)
            throw new ArgumentException("The scene's mesh carries no material field.", nameof(scene));
        EnsureAtmosphere(scene.AirFrame(), scene.Wavelengths());
        EnsureNormalMaps(scene.Mesh.NormalMaps);
        EnsureMaterials(scene.Mesh.Materials);
        _materialProbePipeline ??= CompilePipeline(_rootSignature, ShaderSource.Load(ShaderFile), "CSMaterialProbe", ShaderFile);

        var packed = new float[places.Count * 4];
        for (int i = 0; i < places.Count; i++)
            (packed[i * 4], packed[i * 4 + 1], packed[i * 4 + 2]) = (places[i].X, places[i].Height, places[i].Z);
        Write(_constants, new IntegratorConstants
        {
            MapCount = (uint)Math.Min(scene.Mesh.NormalMaps.Count, 2),
            Map0OriginX = Map(scene, 0).OriginX, Map0OriginZ = Map(scene, 0).OriginZ, Map0Cell = Map(scene, 0).Cell, Map0Size = Map(scene, 0).Size,
            Map1OriginX = Map(scene, 1).OriginX, Map1OriginZ = Map(scene, 1).OriginZ, Map1Cell = Map(scene, 1).Cell, Map1Size = Map(scene, 1).Size,
        });
        Write(_materialConstants, MaterialConstantsFor(scene, DiffuseModel.OrenNayar, CanopyModel.Kernels, 1080));
        Write(_checkConstants, new CheckConstants { Directions = (uint)places.Count });
        try
        {
            _checkDirections = CreateDefaultBuffer<float>(packed);
            _checkSums = CreateUavBuffer((ulong)places.Count * 12 * sizeof(float));
            Timed(_materialProbePipeline, _rootSignature, list =>
            {
                BindIntegrator(list);
                list.Dispatch((uint)((places.Count + LutGroupSize - 1) / LutGroupSize), 1, 1);
            });
            return ReadFloats(_checkSums, places.Count * 12);
        }
        finally
        {
            _checkSums?.Dispose();
            _checkDirections?.Dispose();
            (_checkSums, _checkDirections) = (null, null);
        }
    }

    private void DisposeMaterials()
    {
        _bedTable?.Dispose();
        _materialMap?.Dispose();
        (_bedTable, _materialMap, _materialField) = (null, null, null);
    }
}
