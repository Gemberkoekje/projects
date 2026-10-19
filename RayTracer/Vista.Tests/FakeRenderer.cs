using Vista.Rendering;

namespace Vista.Tests;

/// <summary>
/// A stand-in for the GPU: drives the planner like the real host must and returns synthetic buffers of the
/// rendered window's size. A pixel's XYZ is a function of its place in the full frame, so a loupe's pixels
/// equal the same pixels of a full render, as the real integrator's do.
/// </summary>
internal sealed class FakeRenderer : IVistaRenderer
{
    public const string Name = "Fake GPU";

    public Aov? Skip { get; init; }

    public BatchPlanner? Planner { get; private set; }

    public IReadOnlySet<Aov>? Requested { get; private set; }

    public VistaScene? Scene { get; private set; }

    public RenderSettings? Settings { get; private set; }

    /// <summary>Every scene rendered, in order.</summary>
    public List<VistaScene> Scenes { get; } = [];

    public string DeviceName => Name;

    public RenderOutput Render(VistaScene scene, RenderSettings settings, IReadOnlySet<Aov> aovs, BatchPlanner planner)
    {
        Scene = scene;
        Scenes.Add(scene);
        Settings = settings;
        Planner = planner;
        Requested = aovs;
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
        for (int p = 0; p < pixels; p++)
        {
            int frameX = window.X + p % window.Width;
            int frameY = window.Y + p / window.Width;
            float t = (frameY * settings.Width + frameX) / (float)(settings.Width * settings.Height);
            xyz[p * 3] = 0.95f * t;
            xyz[p * 3 + 1] = t;
            xyz[p * 3 + 2] = 1.09f * t;
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
        });
    }
}
