namespace RayTracer;

public partial class JobSystem
{
    private sealed class DebugBufferRenderer(JobSystem owner)
    {
        private readonly JobSystem _owner = owner;

        public string GetDebugLegend(DebugViewMode mode) => _owner.GetDebugLegendCore(mode);

        public void RenderDebugModeToBuffer(DebugViewMode mode, byte[] targetBuffer, int targetStride)
            => _owner.RenderDebugModeToBufferCore(mode, targetBuffer, targetStride);
    }
}
