using System.Threading.Channels;

namespace RayTracer;

public partial class JobSystem
{
    private sealed class TileScheduler(JobSystem owner, PathTracer pathTracer, DisplayResolver displayResolver)
    {
        private readonly JobSystem _owner = owner;
        private readonly PathTracer _pathTracer = pathTracer;
        private readonly DisplayResolver _displayResolver = displayResolver;

        public void SetupJobs(CancellationToken cancellationToken)
        {
            int n = Environment.ProcessorCount;
            int workerCount = _owner.ThrottleCpu switch
            {
                CpuThrottle.BelowNormal => n,
                CpuThrottle.MinusOne => Math.Max(1, n - 1),
                CpuThrottle.Half => Math.Max(1, n / 2),
                CpuThrottle.One => 1,
                _ => n
            };

            bool lowerPriority = _owner.ThrottleCpu != CpuThrottle.Normal;
            for (int worker = 0; worker < workerCount; worker++)
            {
                Task.Run(async () =>
                {
                    while (!cancellationToken.IsCancellationRequested)
                    {
                        var job = await _owner.Jobs.Reader.ReadAsync(cancellationToken);
                        if (lowerPriority)
                            Thread.CurrentThread.Priority = ThreadPriority.BelowNormal;

                        int phase = _owner._checkerPhase;
                        for (var y = job.Y; y < job.Y + job.Height; y++)
                        {
                            for (var x = job.X; x < job.X + job.Width; x++)
                            {
                                if (_owner.CheckerboardMotion && _owner.IsMoving && ((x + y) & 1) != phase)
                                {
                                    _displayResolver.Render(_owner.Stride, _owner.DisplayBuffer, y, x);
                                    continue;
                                }

                                for (var s = 0; s < _owner.SppPerJob; s++)
                                    _pathTracer.Trace(_owner.Camera, y, x);

                                _displayResolver.Render(_owner.Stride, _owner.DisplayBuffer, y, x);
                            }
                        }

                        var pixels = job.Width * job.Height;
                        var spp = _owner.SppPerJob;
                        _owner.TotalRays += pixels * spp;
                        _owner.TotalTileCompletions++;
                        _owner.Jobs.Writer.TryWrite(job);
                    }
                }, cancellationToken);
            }
        }

        public async Task AddJobs()
        {
            for (int y = 0; y < _owner.Height; y += _owner.TileSize)
            {
                for (int x = 0; x < _owner.Width; x += _owner.TileSize)
                {
                    int w = Math.Min(_owner.TileSize, _owner.Width - x);
                    int h = Math.Min(_owner.TileSize, _owner.Height - y);
                    await _owner.Jobs.Writer.WriteAsync(new Tile(x, y, w, h));
                }
            }
        }
    }
}
