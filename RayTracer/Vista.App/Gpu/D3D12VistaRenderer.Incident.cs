using System.Diagnostics;
using Vista.Atmosphere;
using Vista.Output;
using Vista.Rendering;
using Vortice.Direct3D12;

namespace Vista.App.Gpu;

internal sealed partial class D3D12VistaRenderer
{
    private ID3D12PipelineState _incidentPipeline = null!;

    private void CreateIncidentPipeline() =>
        _incidentPipeline = CompilePipeline(_rootSignature, ShaderSource.Load(ShaderFile), "CSIncidentMeter", ShaderFile);

    /// <summary>What <c>CSIncidentMeter</c> wrote the last time it ran (<see cref="IncidentMeter.Floats"/> floats), for the self-test's parity check.</summary>
    public float[]? IncidentRaw { get; private set; }

    /// <inheritdoc />
    public IncidentLight MeasureIncidentLight(VistaScene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        var stages = new SortedDictionary<string, double>(_initSeconds, StringComparer.Ordinal);
        _initSeconds.Clear();
        var stopwatch = Stopwatch.StartNew();

        // The camera's sky view and the columns LUT of this frame: the render before it has usually built them.
        AtmosphereFrame frame = scene.AirFrame();
        LutBuild luts = EnsureAtmosphere(frame, scene.Wavelengths());
        if (luts.Any)
        {
            stages["atmosphereLuts"] = Lap(stopwatch);
            if (luts.Tables)
                stages[RenderStageKeys.AtmosphereTables] = luts.TablesSeconds;
            if (luts.Orders)
                stages[RenderStageKeys.ScatteringOrders] = luts.OrdersSeconds;
            if (luts.SkyView)
                stages[RenderStageKeys.SkyView] = luts.SkyViewSeconds;
        }

        float[] raw;
        using (ID3D12Resource output = CreateUavBuffer(IncidentMeter.Floats * sizeof(float)))
        {
            _skyLuminance = output;
            try
            {
                stages[RenderStageKeys.IncidentMeter] = Timed(_incidentPipeline, _rootSignature, list =>
                {
                    BindIntegrator(list);
                    list.Dispatch((uint)((IncidentMeter.Items + LutGroupSize - 1) / LutGroupSize), 1, 1);
                });
                raw = ReadFloats(output, IncidentMeter.Floats);
            }
            finally
            {
                _skyLuminance = null;
            }
        }
        IncidentRaw = raw;
        stages["incidentMeter"] = Lap(stopwatch);
        return IncidentMeter.FromKernel(raw) with { StageSeconds = stages };
    }
}
