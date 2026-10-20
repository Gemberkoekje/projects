using System.Numerics;
using Vista.Atmosphere;
using Vista.Rendering;
using Vortice.Direct3D12;

namespace Vista.App.Gpu;

/// <summary>The sky distribution of one build, for <c>--gpu-selftest</c>'s parity checks.</summary>
/// <param name="Luminance">What <c>CSSkyLuminance</c> measured, per (cell, terrain slice): item <c>cell · 12 + slice − 1</c>.</param>
/// <param name="Distribution">The distribution built from it.</param>
internal sealed record SkyLightData(float[] Luminance, SkyDistribution Distribution);

internal sealed partial class D3D12VistaRenderer
{
    private ID3D12PipelineState _skyLuminancePipeline = null!;
    private ID3D12PipelineState? _skyProbePipeline;

    // The sky distribution (vista-plan §10 M1.5): rebuilt with the sky view, i.e. when the frame changes.
    private ID3D12Resource? _skyDistribution;
    private SkyLightData? _skyLight;
    private AtmosphereFrame? _skyLightFrame;

    // The measurement's output, alive only during it.
    private ID3D12Resource? _skyLuminance;

    private void CreateSkyLightPipelines() =>
        _skyLuminancePipeline = CompilePipeline(_rootSignature, ShaderSource.Load(ShaderFile), "CSSkyLuminance", ShaderFile);

    /// <summary>
    /// Makes the sky distribution match <paramref name="frame"/>, whose sky view must be built and whose integrator tables
    /// (the CIE ȳ) uploaded: <c>CSSkyLuminance</c> measures each cell in each terrain slice, the CPU averages the slices and
    /// builds the CDFs (<see cref="SkyDistribution.Build"/>), and they are uploaded. Returns false when the distribution was
    /// already this frame's; <paramref name="seconds"/> is the GPU time of the measurement (0 when there was none).
    /// </summary>
    private bool EnsureSkyLight(AtmosphereFrame frame, out double seconds)
    {
        seconds = 0.0;
        if (_skyDistribution is not null && Equals(_skyLightFrame, frame))
            return false;
        int slices = AtmosphereLuts.SkyTerrainSlices;
        int items = SkyDistribution.Cells * slices;
        float[] luminance;
        using (ID3D12Resource output = CreateUavBuffer((ulong)items * sizeof(float)))
        {
            _skyLuminance = output;
            try
            {
                seconds = Timed(_skyLuminancePipeline, _rootSignature, list =>
                {
                    BindIntegrator(list);
                    list.Dispatch((uint)((items + LutGroupSize - 1) / LutGroupSize), 1, 1);
                });
                luminance = ReadFloats(output, items);
            }
            finally
            {
                _skyLuminance = null;
            }
        }

        var means = new double[SkyDistribution.Cells];
        for (int cell = 0; cell < means.Length; cell++)
        {
            double sum = 0.0;
            for (int s = 0; s < slices; s++)
                sum += luminance[cell * slices + s];
            means[cell] = sum / slices;
        }
        SkyDistribution distribution = SkyDistribution.Build(means);
        _skyDistribution?.Dispose();
        _skyDistribution = CreateDefaultBuffer<float>(distribution.Floats);
        _skyLight = new SkyLightData(luminance, distribution);
        _skyLightFrame = frame;
        return true;
    }

    /// <summary>The sky distribution of the last render that sampled the sky (null before one).</summary>
    public SkyLightData? SkyLight => _skyLight;

    /// <summary>
    /// The sky distribution's sampling on the GPU (<c>CSSkyProbe</c>), for the self-test's parity with
    /// <see cref="SkyDistribution"/>: the distribution of the last render that sampled the sky, the direction it draws for
    /// each triple of random numbers and the pdf it gives each direction. Returns, per probe, (sampled direction, its pdf,
    /// the direction's pdf, its band, its column, 0).
    /// </summary>
    public float[] RunSkyProbe(IReadOnlyList<(Vector3 Random, Vector3 Direction)> probes)
    {
        ArgumentNullException.ThrowIfNull(probes);
        if (probes.Count == 0)
            throw new ArgumentException("No probes.", nameof(probes));
        if (_skyDistribution is null)
            throw new InvalidOperationException("No sky distribution yet: render with light sampling first.");
        _skyProbePipeline ??= CompilePipeline(_rootSignature, ShaderSource.Load(ShaderFile), "CSSkyProbe", ShaderFile);

        var packed = new float[probes.Count * 8];
        for (int i = 0; i < probes.Count; i++)
        {
            (Vector3 u, Vector3 d) = probes[i];
            (packed[i * 8], packed[i * 8 + 1], packed[i * 8 + 2]) = (u.X, u.Y, u.Z);
            (packed[i * 8 + 4], packed[i * 8 + 5], packed[i * 8 + 6]) = (d.X, d.Y, d.Z);
        }
        Write(_checkConstants, new CheckConstants { Directions = (uint)probes.Count });
        try
        {
            _checkDirections = CreateDefaultBuffer<float>(packed);
            _checkSums = CreateUavBuffer((ulong)packed.Length * sizeof(float));
            Timed(_skyProbePipeline, _rootSignature, list =>
            {
                BindIntegrator(list);
                list.Dispatch((uint)((probes.Count + LutGroupSize - 1) / LutGroupSize), 1, 1);
            });
            return ReadFloats(_checkSums, packed.Length);
        }
        finally
        {
            _checkSums?.Dispose();
            _checkDirections?.Dispose();
            (_checkSums, _checkDirections) = (null, null);
        }
    }

    private void DisposeSkyLight()
    {
        _skyDistribution?.Dispose();
        _skyLuminance?.Dispose();
        _skyProbePipeline?.Dispose();
        _skyLuminancePipeline?.Dispose();
        (_skyDistribution, _skyLuminance, _skyLight, _skyLightFrame) = (null, null, null, null);
    }
}
