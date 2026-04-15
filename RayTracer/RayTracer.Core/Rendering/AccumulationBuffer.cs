namespace RayTracer;

public partial class JobSystem
{
    private sealed class AccumulationBuffer(JobSystem owner)
    {
        private readonly JobSystem _owner = owner;

        public void ResetAccumulation()
        {
            _owner.ResetAccumulationCore();
        }

        public void SoftResetAccumulation()
        {
            _owner.SoftResetAccumulationCore();
        }
    }
}
