namespace RayTracer;

public partial class JobSystem
{
    private sealed class PathTracer(JobSystem owner)
    {
        private readonly JobSystem _owner = owner;

        public void Trace(Camera camera, int y, int x)
        {
            _owner.TraceCore(camera, y, x);
        }
    }
}
