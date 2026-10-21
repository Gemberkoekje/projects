using System.Diagnostics;
using RayTracer.Gpu;
using Vista.Rendering;
using Vista.World;
using Vortice.Direct3D12;

namespace Vista.App.Gpu;

/// <summary>
/// The far field as height-field tiles (vista-plan §10 M1.10, the measured alternative to meshing it): the integrator compiled with
/// <c>HEIGHT_FIELD</c>, the world's two far grids as float buffers (kept while the meshes rendered share them), and per view the
/// tiles' frames and boxes, the boxes' procedural acceleration structure beside the mesh's, and a two-instance TLAS.
/// </summary>
internal sealed partial class D3D12VistaRenderer
{
    private const string HeightFieldDefine = "HEIGHT_FIELD";

    /// <summary>The render stage that uploads a world's far grids (once per world).</summary>
    internal const string HeightFieldGridsStage = "heightFieldGridUpload";

    private ID3D12PipelineState? _heightFieldPipeline;

    // The world's far grids: kept while the meshes rendered share them.
    private HeightFieldGrids? _heightFieldGrids;
    private ID3D12Resource? _heightsLevel1;
    private ID3D12Resource? _heightsLevel2;

    // The view's tiles: part of the scene.
    private ID3D12Resource? _heightFieldTiles;
    private ID3D12Resource? _heightFieldAabbs;
    private ID3D12Resource? _heightFieldBlas;

    /// <summary>The integrator with the height-field far field, compiled the first time it is asked for.</summary>
    private ID3D12PipelineState HeightFieldPipeline(SortedDictionary<string, double> stages)
    {
        if (_heightFieldPipeline is null)
        {
            var stopwatch = Stopwatch.StartNew();
            _heightFieldPipeline = CompilePipeline(_rootSignature, ShaderSource.Load(ShaderFile, HeightFieldDefine), "CSMain", ShaderFile);
            stages[RenderStageKeys.ReferenceShaderCompile] = stopwatch.Elapsed.TotalSeconds;
        }
        return _heightFieldPipeline;
    }

    /// <summary>
    /// Uploads the far grids of <paramref name="field"/> unless they are the ones on the GPU, and says whether it did; frees them for a
    /// mesh without a height field, so a mesh's video memory is its own.
    /// </summary>
    private bool EnsureHeightFieldGrids(FarHeightField field)
    {
        if (!field.Any)
        {
            DisposeHeightFieldGrids();
            return false;
        }
        if (ReferenceEquals(field.Grids, _heightFieldGrids))
            return false;
        DisposeHeightFieldGrids();
        _heightsLevel1 = CreateDefaultBuffer<float>(field.Grids.Level1);
        _heightsLevel2 = CreateDefaultBuffer<float>(field.Grids.Level2);
        _heightFieldGrids = field.Grids;
        return true;
    }

    /// <summary>Uploads the view's tiles and their boxes.</summary>
    private void UploadHeightFieldTiles(FarHeightField field)
    {
        if (!field.Any)
            return;
        _heightFieldTiles = CreateDefaultBuffer<float>(field.Tiles);
        _heightFieldAabbs = CreateDefaultBuffer<float>(field.Aabbs);
    }

    /// <summary>The procedural bottom-level build of the tiles' boxes at this address (zero for a size query).</summary>
    private static BuildRaytracingAccelerationStructureInputs HeightFieldBlasInputs(FarHeightField field, ulong aabbs) => new()
    {
        Type = RaytracingAccelerationStructureType.BottomLevel,
        Layout = ElementsLayout.Array,
        Flags = RaytracingAccelerationStructureBuildFlags.PreferFastTrace,
        DescriptorsCount = 1,
        GeometryDescriptions =
        [
            new RaytracingGeometryDescription
            {
                Type = RaytracingGeometryType.ProceduralPrimitiveAabbs,
                Flags = RaytracingGeometryFlags.Opaque,
                AABBs = new RaytracingGeometryAabbsDescription((ulong)field.TileCount,
                    new GpuVirtualAddressAndStride(aabbs, FarHeightField.AabbStride * sizeof(float))),
            },
        ],
    };

    /// <summary>The memory the view's tiles add: their buffers, their acceleration structure and its scratch, and the grids if not resident.</summary>
    private long HeightFieldBytesNeeded(FarHeightField field)
    {
        if (!field.Any)
            return 0;
        RaytracingAccelerationStructurePrebuildInfo info = _device.GetRaytracingAccelerationStructurePrebuildInfo(HeightFieldBlasInputs(field, 0));
        long grids = ReferenceEquals(field.Grids, _heightFieldGrids) ? 0 : field.Grids.ByteSize;
        return field.ByteSize + (long)info.ResultDataMaxSizeInBytes + (long)info.ScratchDataSizeInBytes + grids;
    }

    /// <summary>Records the tiles' acceleration-structure build (its scratch is returned, to be freed after the submission).</summary>
    private ID3D12Resource? RecordHeightFieldBlas(FarHeightField field)
    {
        if (!field.Any)
            return null;
        BuildRaytracingAccelerationStructureInputs inputs = HeightFieldBlasInputs(field, _heightFieldAabbs!.GPUVirtualAddress);
        RaytracingAccelerationStructurePrebuildInfo info = _device.GetRaytracingAccelerationStructurePrebuildInfo(inputs);
        ID3D12Resource scratch = CreateUavBuffer(info.ScratchDataSizeInBytes);
        _heightFieldBlas = CreateUavBuffer(info.ResultDataMaxSizeInBytes, ResourceStates.RaytracingAccelerationStructure);
        _blasBytes += (long)info.ResultDataMaxSizeInBytes;
        _commandList.BuildRaytracingAccelerationStructure(new BuildRaytracingAccelerationStructureDescription
        {
            Inputs = inputs,
            ScratchAccelerationStructureData = scratch.GPUVirtualAddress,
            DestinationAccelerationStructureData = _heightFieldBlas.GPUVirtualAddress,
        });
        _commandList.ResourceBarrierUnorderedAccessView(_heightFieldBlas);
        return scratch;
    }

    private void DisposeHeightFieldTiles()
    {
        _heightFieldBlas?.Dispose();
        _heightFieldAabbs?.Dispose();
        _heightFieldTiles?.Dispose();
        (_heightFieldBlas, _heightFieldAabbs, _heightFieldTiles) = (null, null, null);
    }

    private void DisposeHeightFieldGrids()
    {
        _heightsLevel2?.Dispose();
        _heightsLevel1?.Dispose();
        (_heightsLevel2, _heightsLevel1, _heightFieldGrids) = (null, null, null);
    }
}
