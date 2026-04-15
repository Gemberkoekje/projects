using System;
using System.Collections.Concurrent;
using System.Numerics;
using System.Reflection.PortableExecutable;
using System.Threading.Channels;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace RayTracer;

public class JobSystem
{
    WavelengthLookup WavelengthLookup { get; init; } = new();

    public Vector3[] AccumXYZ { get; init; }
    public uint[] SampleCount { get; init; }
    public long[] WavelengthCounter { get; init; }
    public bool[] LastHit { get; init; }
    private Vector3[] HitPointWorld { get; init; }
    private readonly float[] _lumaM2;
    private readonly float[] _lumaVariance;
    private readonly float[] _lumaDirectM2;
    private readonly float[] _lumaIndirectM2;
    private readonly float[] _lumaDirectVariance;
    private readonly float[] _lumaIndirectVariance;
    private readonly float[] _historyWeight;
    private readonly byte[] _historyRejected;
    private readonly float[] _clampAmount;
    private readonly bool[] _clampHitFrame;
    private readonly float[] _depthDistance;
    private readonly float[] _albedoScalar;
    private readonly Vector3[] _normalWorld;
    private readonly Vector3[] _directLightingXYZ;
    private readonly Vector3[] _indirectLightingXYZ;
    private readonly Vector3[] _emissiveLightingXYZ;
    private readonly Vector3[] _bounce0XYZ;
    private readonly Vector3[] _bounce1XYZ;
    private readonly Vector3[] _bounce2PlusXYZ;
    private readonly float[] _diffCurrentVsAccum;
    private readonly float[] _diffUnfilteredVsFiltered;
    private readonly float[] _diffReprojectedVsCurrent;
    private readonly int[] _lastUpdatedFrame;

    public int Width { get; init; }
    public int Height { get; init; }
    public int TileSize { get; init; }

    public int SppPerJob { get; init; } = 4;

    public uint MaxSampleCount { get; init; } = 500;

    public Channel<Tile> Jobs { get; init; } = Channel.CreateBounded<Tile>(1000);

    public Tracable[] Scene { get; init; }

    private BVH _bvh;

    private volatile int _checkerPhase;

    private Light[] _lights = [];

    /// <summary>Base ambient illumination applied to every hit point.</summary>
    private const float AmbientLevel = 0.05f;

    /// <summary>
    /// Intensity of each point light. Scaled so that the floor
    /// directly below a ceiling light receives roughly unit illumination.
    /// </summary>
    private const float LightIntensity = 4.0f;

    public Camera Camera { get; set; }

    public long TotalRays { get; private set; }

    /// <summary>
    /// Number of individual tile render completions since startup.
    /// Divide by <see cref="TotalTiles"/> to get full-screen passes.
    /// </summary>
    public long TotalTileCompletions { get; private set; }

    /// <summary>
    /// Total number of tiles that cover the full screen.
    /// </summary>
    public int TotalTiles { get; private set; }

    private float Correction { get; init; } = 0;

    public int Stride { get; init; } = 0;

    public byte[] DisplayBuffer { get; init; }

    /// <summary>
    /// Set by the render loop when the camera is actively moving.
    /// Workers read this to decide whether to apply spatial denoising.
    /// </summary>
    public bool IsMoving { get; set; }

    /// <summary>
    /// During motion, sample counts are capped to this value so new
    /// samples quickly replace stale data. Kept high enough for
    /// reasonable spectral coverage; the 3뿯½3 spatial filter in
    /// <see cref="Render"/> provides the remaining noise reduction.
    /// </summary>
    public uint MotionSampleCap { get; init; } = 20;

    /// <summary>
    /// When true, each sample is jittered to a random sub-pixel position
    /// instead of the pixel centre. Converts aliasing into noise that
    /// the accumulator averages away.
    /// </summary>
    public bool SubPixelJitter { get; init; }

    /// <summary>
    /// Radius of the spatial averaging filter applied during camera
    /// motion.  0 = disabled, 1 = 3뿯½3, 2 = 5뿯½5.
    /// </summary>
    public int FilterRadius { get; init; } = 1;

    /// <summary>
    /// When true the spatial filter uses luminance-based bilateral
    /// weighting: neighbours with similar brightness are averaged
    /// (denoised) while large luminance jumps (edges) are preserved.
    /// </summary>
    public bool EdgeAwareFilter { get; init; }

    /// <summary>
    /// When true, only half the pixels are traced per tile during motion,
    /// alternating in a checkerboard pattern each soft-reset. The spatial
    /// filter fills the gaps, effectively doubling throughput while moving.
    /// </summary>
    public bool CheckerboardMotion { get; init; }

    /// <summary>
    /// Blend factor for exponential moving average during camera motion.
    /// 0 = disabled (uses running mean). Values around 0.2 give stable,
    /// responsive results. Ignored when the camera is stationary.
    /// </summary>
    public float TemporalBlendAlpha { get; init; }
    public bool EnableTaa { get; init; }

    /// <summary>
    /// Per-component XYZ clamp applied to each sample before accumulation.
    /// Suppresses firefly artifacts from extreme spectral contributions.
    /// 0 = disabled.
    /// </summary>
    public float SampleClamp { get; init; }

    /// <summary>
    /// Controls how surfaces are shaded: <see cref="LightingMode.None"/>
    /// for raw albedo, <see cref="LightingMode.Direct"/> for ambient +
    /// Lambertian without shadows, or <see cref="LightingMode.NEE"/> for
    /// full next event estimation with stochastic shadow rays.
    /// </summary>
    public LightingMode Lighting { get; init; }

    /// <summary>
    /// Controls how many worker threads are spawned and at what OS priority they run.
    /// See <see cref="CpuThrottle"/> for the available levels.
    /// </summary>
    public CpuThrottle ThrottleCpu { get; init; }

    /// <summary>
    /// Controls how aggressively the bilateral filter preserves edges.
    /// Higher = more edge-preserving.  25 works well for normalised
    /// XYZ Y values in the 0-1 range.
    /// </summary>
    private const float BilateralSharpness = 25f;

    // Precomputed projection constants (depend only on Fov/Aspect/resolution, not position/rotation)
    private readonly float _tanHalfFov;
    private readonly float _aspectTanHalfFov;
    private readonly float _invWidth;
    private readonly float _invHeight;

    // TAA history and camera state (resolved once per frame on UI thread).
    private readonly Vector3[] _taaHistoryXYZ;
    private readonly Vector3[] _taaHistoryHitPoint;
    private readonly bool[] _taaHistoryValid;
    private readonly Vector3[] _taaNextXYZ;
    private readonly Vector3[] _taaNextHitPoint;
    private readonly bool[] _taaNextValid;
    private Vector3 _taaPrevCamPos;
    private Quaternion _taaPrevCamRot;
    private bool _taaHasPrevCamera;
    private long _clampEventCount;

    public double RejectedHistoryPercent { get; private set; }
    public double ClampedPixelPercent { get; private set; }
    public double AverageVariance { get; private set; }
    public double AverageHistoryWeight { get; private set; }
    public double AverageEffectiveSpp { get; private set; }
    public uint MaxObservedSampleCount { get; private set; }
    public int FrameIndex { get; private set; }

    public JobSystem(int width, int height, int tilesize, Tracable[] scene, Camera camera, int stride, Light[]? lights = null)
    {
        AccumXYZ = new Vector3[width * height];
        SampleCount = new uint[width * height];
        WavelengthCounter = new long[width * height];
        LastHit = new bool[width * height];
        HitPointWorld = new Vector3[width * height];
        _lumaM2 = new float[width * height];
        _lumaVariance = new float[width * height];
        _lumaDirectM2 = new float[width * height];
        _lumaIndirectM2 = new float[width * height];
        _lumaDirectVariance = new float[width * height];
        _lumaIndirectVariance = new float[width * height];
        _historyWeight = new float[width * height];
        _historyRejected = new byte[width * height];
        _clampAmount = new float[width * height];
        _clampHitFrame = new bool[width * height];
        _depthDistance = new float[width * height];
        _albedoScalar = new float[width * height];
        _normalWorld = new Vector3[width * height];
        _directLightingXYZ = new Vector3[width * height];
        _indirectLightingXYZ = new Vector3[width * height];
        _emissiveLightingXYZ = new Vector3[width * height];
        _bounce0XYZ = new Vector3[width * height];
        _bounce1XYZ = new Vector3[width * height];
        _bounce2PlusXYZ = new Vector3[width * height];
        _diffCurrentVsAccum = new float[width * height];
        _diffUnfilteredVsFiltered = new float[width * height];
        _diffReprojectedVsCurrent = new float[width * height];
        _lastUpdatedFrame = new int[width * height];
        Width = width;
        Height = height;
        Scene = scene;
        _bvh = new BVH(scene);
        _lights = lights ?? [];
        Camera = camera;
        var total = new Vector3(0, 0, 0);
        for (var wl = 400; wl <= 700; wl++)
        {
            if (WavelengthLookup.TryGet(wl, out var xyz))
            {
                total += xyz;
            }
        }
        var average = (total / new Vector3(300, 300, 300));
        Correction = 1 / average.Y;
        TileSize = tilesize;
        int byteCount = stride * height;
        DisplayBuffer = new byte[byteCount];
        Stride = stride;

        _tanHalfFov = MathF.Tan(camera.Fov * 0.5f);
        _aspectTanHalfFov = camera.Aspect * _tanHalfFov;
        _invWidth = 1f / width;
        _invHeight = 1f / height;

        _taaHistoryXYZ = new Vector3[width * height];
        _taaHistoryHitPoint = new Vector3[width * height];
        _taaHistoryValid = new bool[width * height];
        _taaNextXYZ = new Vector3[width * height];
        _taaNextHitPoint = new Vector3[width * height];
        _taaNextValid = new bool[width * height];

        // Compute total tile count for pass tracking
        int tilesX = (width + tilesize - 1) / tilesize;
        int tilesY = (height + tilesize - 1) / tilesize;
        TotalTiles = tilesX * tilesY;
    }

    public void SetupJobs(CancellationToken cancellationToken)
    {
        int n = Environment.ProcessorCount;
        int workerCount = ThrottleCpu switch
        {
            CpuThrottle.BelowNormal => n,
            CpuThrottle.MinusOne    => Math.Max(1, n - 1),
            CpuThrottle.Half        => Math.Max(1, n / 2),
            CpuThrottle.One         => 1,
            _                       => n
        };
        bool lowerPriority = ThrottleCpu != CpuThrottle.Normal;
        for (int x = 0; x < workerCount; x++)
        {
            Task.Run(async () =>
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    var job = await Jobs.Reader.ReadAsync(cancellationToken);
                    if (lowerPriority)
                        Thread.CurrentThread.Priority = ThreadPriority.BelowNormal;
                    int phase = _checkerPhase;
                    for(var y = job.Y; y < job.Y + job.Height; y++)
                    {
                        for (var x = job.X; x < job.X + job.Width; x++)
                        {
                            if (CheckerboardMotion && IsMoving && ((x + y) & 1) != phase)
                            {
                                Render(Stride, DisplayBuffer, y, x);
                                continue;
                            }
                            for (var s = 0; s < SppPerJob; s++)
                                    Trace(Camera, y, x);
                            Render(Stride, DisplayBuffer, y, x);
                        }
                    }
                    var pixels = job.Width * job.Height;
                    var spp = SppPerJob;
                    TotalRays += pixels * spp;
                    TotalTileCompletions++;
                    Jobs.Writer.TryWrite(job);
                }
            }, cancellationToken);
        }
    }

    private void Render(int stride, byte[] buffer, int y, int x)
    {
        Vector3 xyz = ResolveFilteredXYZ(y, x);

        var linearColor = ToSRGB(xyz);

        // Apply gamma correction for sRGB
        var color = new Vector3(
            LinearToSRGB(linearColor.X),
            LinearToSRGB(linearColor.Y),
            LinearToSRGB(linearColor.Z)
        );

        int i = y * stride + x * 4;
        buffer[i + 0] = (byte)Math.Clamp(color.Z * 255f, 0, 255); // B
        buffer[i + 1] = (byte)Math.Clamp(color.Y * 255f, 0, 255); // G
        buffer[i + 2] = (byte)Math.Clamp(color.X * 255f, 0, 255); // R
        buffer[i + 3] = 255;                                      // A

    }

    public void ResolveDisplayBufferWithTaa()
    {
        bool useTaa = EnableTaa && TemporalBlendAlpha > 0f && _taaHasPrevCamera;
        // Base alpha comes from the preset, but make it motion-aware so
        // history is discounted while the camera is moving (reduces ghosting).
        float alpha = Math.Clamp(TemporalBlendAlpha, 0.01f, 1f);
        if (IsMoving)
        {
            // Boost the lerp towards the current frame when moving. Multiply
            // so small configured alphas (e.g. 0.04-0.05) become more aggressive
            // (0.4-0.5), reducing history influence during motion.
            alpha = Math.Clamp(alpha * 10f, 0.01f, 1f);
        }
        long rejected = 0;
        double histWeightSum = 0;
        double varianceSum = 0;
        double sppSum = 0;
        uint maxSpp = 0;
        long clampedPixelCount = 0;

        for (int y = 0; y < Height; y++)
        {
            int row = y * Width;
            for (int x = 0; x < Width; x++)
            {
                int ix = row + x;
                Vector3 current = ResolveFilteredXYZ(y, x);
                Vector3 unfiltered = AccumXYZ[ix];
                Vector3 resolved = current;
                float historyWeight = 0f;
                bool rejectedThisPixel = false;
                float reprojectionDelta = 0f;

                bool currentHit = LastHit[ix];
                Vector3 currentHitPoint = HitPointWorld[ix];

                if (useTaa && currentHit &&
                    TryProjectToPrevPixel(currentHitPoint, out float pxf, out float pyf))
                {
                    // Use fractional previous coordinates (proper motion vectors)
                    // and bilinearly sample the history buffers to avoid rounding
                    // artifacts. We require a minimum valid weight from the
                    // neighboring history pixels to accept the reprojection.
                    int ix0 = (int)MathF.Floor(pxf);
                    int iy0 = (int)MathF.Floor(pyf);
                    float wx = pxf - ix0;
                    float wy = pyf - iy0;

                    // Clamp sample indices to image bounds
                    int x0 = Math.Clamp(ix0, 0, Width - 1);
                    int y0 = Math.Clamp(iy0, 0, Height - 1);
                    int x1 = Math.Clamp(ix0 + 1, 0, Width - 1);
                    int y1 = Math.Clamp(iy0 + 1, 0, Height - 1);

                    // Fetch four neighbors
                    int i00 = y0 * Width + x0;
                    int i10 = y0 * Width + x1;
                    int i01 = y1 * Width + x0;
                    int i11 = y1 * Width + x1;

                    // Bilinear weights
                    float w00 = (1f - wx) * (1f - wy);
                    float w10 = wx * (1f - wy);
                    float w01 = (1f - wx) * wy;
                    float w11 = wx * wy;

                    // Determine how much valid history contributes
                    float validWeight = (_taaHistoryValid[i00] ? w00 : 0f) + (_taaHistoryValid[i10] ? w10 : 0f) + (_taaHistoryValid[i01] ? w01 : 0f) + (_taaHistoryValid[i11] ? w11 : 0f);

                    if (validWeight > 0.25f)
                    {
                        // Bilinear sample history color and hit point
                        Vector3 history = _taaHistoryXYZ[i00] * w00 + _taaHistoryXYZ[i10] * w10 + _taaHistoryXYZ[i01] * w01 + _taaHistoryXYZ[i11] * w11;
                        Vector3 historyHit = _taaHistoryHitPoint[i00] * w00 + _taaHistoryHitPoint[i10] * w10 + _taaHistoryHitPoint[i01] * w01 + _taaHistoryHitPoint[i11] * w11;

                        float reprojErr = Vector3.DistanceSquared(historyHit, currentHitPoint);

                        // Disocclusion + normal-consistency checks.
                        float reprojThreshold = IsMoving ? 0.0625f : 0.25f; // squared distance
                        bool accept = reprojErr < reprojThreshold;

                        // Normal agreement check (cheap): sample current normals at history neighbors
                        var histNormal = Vector3.Zero;
                        histNormal += _normalWorld[i00] * w00;
                        histNormal += _normalWorld[i10] * w10;
                        histNormal += _normalWorld[i01] * w01;
                        histNormal += _normalWorld[i11] * w11;
                        var currNormal = _normalWorld[ix];
                        if (accept && histNormal != Vector3.Zero && currNormal != Vector3.Zero)
                        {
                            float ndot = Math.Clamp(Vector3.Dot(Vector3.Normalize(histNormal), Vector3.Normalize(currNormal)), -1f, 1f);
                            if (ndot < 0.90f)
                                accept = false;
                        }

                        if (accept)
                        {
                            ComputeNeighborhoodMinMax(y, x, out Vector3 nMin, out Vector3 nMax);
                            history = Vector3.Clamp(history, nMin, nMax);
                            resolved = Vector3.Lerp(history, current, alpha);
                            historyWeight = 1f - alpha;
                            reprojectionDelta = Vector3.Distance(history, current);
                        }
                        else
                        {
                            rejectedThisPixel = true;
                        }
                    }
                    else
                    {
                        rejectedThisPixel = true;
                    }
                }
                else if (useTaa && currentHit)
                {
                    rejectedThisPixel = true;
                }

                _taaNextXYZ[ix] = resolved;
                _taaNextHitPoint[ix] = currentHitPoint;
                _taaNextValid[ix] = currentHit;
                _historyWeight[ix] = historyWeight;
                _historyRejected[ix] = rejectedThisPixel ? (byte)1 : (byte)0;
                _diffReprojectedVsCurrent[ix] = reprojectionDelta;
                _diffCurrentVsAccum[ix] = Vector3.Distance(current, resolved);
                _diffUnfilteredVsFiltered[ix] = Vector3.Distance(unfiltered, current);

                if (rejectedThisPixel)
                    rejected++;

                histWeightSum += historyWeight;
                varianceSum += _lumaVariance[ix];
                uint spp = SampleCount[ix];
                sppSum += spp;
                if (spp > maxSpp)
                    maxSpp = spp;
                if (_clampHitFrame[ix])
                {
                    clampedPixelCount++;
                }

                WriteColorToBuffer(y, x, resolved);
            }
        }

        if (useTaa || !_taaHasPrevCamera)
        {
            Array.Copy(_taaNextXYZ, _taaHistoryXYZ, _taaNextXYZ.Length);
            Array.Copy(_taaNextHitPoint, _taaHistoryHitPoint, _taaNextHitPoint.Length);
            Array.Copy(_taaNextValid, _taaHistoryValid, _taaNextValid.Length);
        }
        else
        {
            // Keep history coherent when TAA is toggled off.
            for (int i = 0; i < _taaHistoryXYZ.Length; i++)
            {
                _taaHistoryXYZ[i] = _taaNextXYZ[i];
                _taaHistoryHitPoint[i] = _taaNextHitPoint[i];
                _taaHistoryValid[i] = _taaNextValid[i];
            }
        }

        _taaPrevCamPos = Camera.Position;
        _taaPrevCamRot = Camera.Rotation;
        _taaHasPrevCamera = true;
        FrameIndex++;

        int pixels = Width * Height;
        RejectedHistoryPercent = pixels > 0 ? (double)rejected / pixels * 100.0 : 0.0;
        ClampedPixelPercent = pixels > 0 ? (double)clampedPixelCount / pixels * 100.0 : 0.0;
        AverageVariance = pixels > 0 ? varianceSum / pixels : 0.0;
        AverageHistoryWeight = pixels > 0 ? histWeightSum / pixels : 0.0;
        AverageEffectiveSpp = pixels > 0 ? sppSum / pixels : 0.0;
        MaxObservedSampleCount = maxSpp;
        Array.Clear(_clampHitFrame);
    }

    private void ComputeNeighborhoodMinMax(int y, int x, out Vector3 nMin, out Vector3 nMax)
    {
        nMin = new Vector3(float.MaxValue);
        nMax = new Vector3(float.MinValue);
        int yMin = Math.Max(y - 1, 0);
        int yMax = Math.Min(y + 1, Height - 1);
        int xMin = Math.Max(x - 1, 0);
        int xMax = Math.Min(x + 1, Width - 1);
        for (int ny = yMin; ny <= yMax; ny++)
        {
            int rowOff = ny * Width;
            for (int nx = xMin; nx <= xMax; nx++)
            {
                Vector3 v = ResolveFilteredXYZ(ny, nx);
                nMin = Vector3.Min(nMin, v);
                nMax = Vector3.Max(nMax, v);
            }
        }
    }

    private bool TryProjectToPrevPixel(Vector3 worldPoint, out float px, out float py)
    {
        Quaternion invRot = Quaternion.Inverse(_taaPrevCamRot);
        Vector3 local = Vector3.Transform(worldPoint - _taaPrevCamPos, invRot);
        if (local.Z <= 1e-4f)
        {
            px = py = 0f;
            return false;
        }

        float ndcX = local.X / (local.Z * _aspectTanHalfFov);
        float ndcY = local.Y / (local.Z * _tanHalfFov);

        float fx = ((ndcX + 1f) * 0.5f) * Width - 0.5f;
        float fy = ((1f - ndcY) * 0.5f) * Height - 0.5f;

        px = fx;
        py = fy;
        return px >= 0f && px < Width && py >= 0f && py < Height;
    }

    private void WriteColorToBuffer(int y, int x, Vector3 xyz)
    {
        var linearColor = ToSRGB(xyz);
        var color = new Vector3(
            LinearToSRGB(linearColor.X),
            LinearToSRGB(linearColor.Y),
            LinearToSRGB(linearColor.Z)
        );

        int i = y * Stride + x * 4;
        DisplayBuffer[i + 0] = (byte)Math.Clamp(color.Z * 255f, 0, 255);
        DisplayBuffer[i + 1] = (byte)Math.Clamp(color.Y * 255f, 0, 255);
        DisplayBuffer[i + 2] = (byte)Math.Clamp(color.X * 255f, 0, 255);
        DisplayBuffer[i + 3] = 255;
    }

    /// <summary>
    /// Returns a snapshot of debug values for the specified pixel coordinates.
    /// Coordinates are in render-target space (0..Width-1, 0..Height-1).
    /// </summary>
    public PixelDebugInfo GetPixelDebugInfo(int x, int y)
    {
        if (x < 0 || x >= Width || y < 0 || y >= Height)
            return new PixelDebugInfo(
                Vector3.Zero,
                Vector3.Zero,
                0f,
                0f,
                false,
                0u,
                0f,
                0,
                0f,
                0f,
                0,
                Vector3.Zero,
                Vector3.Zero,
                Vector3.Zero,
                0f,
                0f,
                Vector3.Zero,
                Vector3.Zero,
                Vector3.Zero);

        int ix = y * Width + x;
        Vector3 filtered = ResolveFilteredXYZ(y, x);
        return new PixelDebugInfo(
            AccumXYZ[ix],
            filtered,
            _diffCurrentVsAccum[ix],
            _clampAmount[ix],
            _clampHitFrame[ix],
            SampleCount[ix],
            _historyWeight[ix],
            _historyRejected[ix],
            _depthDistance[ix],
            _albedoScalar[ix],
            _lastUpdatedFrame[ix],
            _normalWorld[ix],
            _directLightingXYZ[ix],
            _indirectLightingXYZ[ix],
            _diffUnfilteredVsFiltered[ix],
            _diffReprojectedVsCurrent[ix],
            _bounce0XYZ[ix],
            _bounce1XYZ[ix],
            _bounce2PlusXYZ[ix]
        );
    }

    private Vector3 ResolveFilteredXYZ(int y, int x)
    {
        int radius = IsMoving ? FilterRadius : 0;
        if (radius <= 0)
            return AccumXYZ[y * Width + x];

        int yMin = Math.Max(y - radius, 0);
        int yMax = Math.Min(y + radius, Height - 1);
        int xMin = Math.Max(x - radius, 0);
        int xMax = Math.Min(x + radius, Width - 1);

        if (EdgeAwareFilter)
        {
            Vector3 centerXyz = AccumXYZ[y * Width + x];
            Vector3 xyz = Vector3.Zero;
            float totalWeight = 0f;
            for (int ny = yMin; ny <= yMax; ny++)
            {
                int rowOff = ny * Width;
                for (int nx = xMin; nx <= xMax; nx++)
                {
                    Vector3 neighbor = AccumXYZ[rowOff + nx];
                    float lumDiff = centerXyz.Y - neighbor.Y;
                    float w = MathF.Exp(-lumDiff * lumDiff * BilateralSharpness);
                    xyz += neighbor * w;
                    totalWeight += w;
                }
            }
            return totalWeight > 0f ? xyz / totalWeight : centerXyz;
        }

        Vector3 sum = Vector3.Zero;
        int count = 0;
        for (int ny = yMin; ny <= yMax; ny++)
        {
            int rowOff = ny * Width;
            for (int nx = xMin; nx <= xMax; nx++)
            {
                sum += AccumXYZ[rowOff + nx];
                count++;
            }
        }
        return sum / count;
    }

    private float LinearToSRGB(float linear)
    {
        if (linear <= 0.0031308f)
            return 12.92f * linear;
        else
            return 1.055f * MathF.Pow(linear, 1.0f / 2.4f) - 0.055f;
    }

    public static Matrix3x3 TosRGBMatrix => new(
         3.2406f, -1.5372f, -0.4986f,
         -0.9689f, 1.8758f, 0.0415f,
         0.0557f, -0.2040f, 1.0570f
         );

    public static Vector3 ToSRGB(Vector3 xyz)
    {
        return xyz * TosRGBMatrix;
    }

    /// <summary>
    /// Clears the accumulation buffers so the image reconverges quickly
    /// after a camera movement or rotation.
    /// </summary>
    public void ResetAccumulation()
    {
        Array.Clear(AccumXYZ);
        Array.Clear(SampleCount);
        Array.Clear(LastHit);
        Array.Clear(WavelengthCounter);
        Array.Clear(HitPointWorld);
        Array.Clear(_lumaM2);
        Array.Clear(_lumaVariance);
        Array.Clear(_historyWeight);
        Array.Clear(_historyRejected);
        Array.Clear(_clampAmount);
        Array.Clear(_clampHitFrame);
        Array.Clear(_depthDistance);
        Array.Clear(_albedoScalar);
        Array.Clear(_normalWorld);
        Array.Clear(_directLightingXYZ);
        Array.Clear(_indirectLightingXYZ);
        Array.Clear(_emissiveLightingXYZ);
        Array.Clear(_diffCurrentVsAccum);
        Array.Clear(_diffUnfilteredVsFiltered);
        Array.Clear(_diffReprojectedVsCurrent);
        Array.Clear(_lastUpdatedFrame);
        Array.Clear(_taaHistoryXYZ);
        Array.Clear(_taaHistoryHitPoint);
        Array.Clear(_taaHistoryValid);
        Array.Clear(_taaNextXYZ);
        Array.Clear(_taaNextHitPoint);
        Array.Clear(_taaNextValid);
        _taaHasPrevCamera = false;
        _clampEventCount = 0;
        RejectedHistoryPercent = 0;
        ClampedPixelPercent = 0;
        AverageVariance = 0;
        AverageHistoryWeight = 0;
        AverageEffectiveSpp = 0;
        MaxObservedSampleCount = 0;
        FrameIndex = 0;
    }

    /// <summary>
    /// Invalidate only the TAA/history buffers so reprojection is disabled
    /// for the next resolve without clearing the accumulation buffers.
    /// Useful for rotations where history should not be reused but
    /// preserving accumulated samples avoids large black holes when using
    /// checkerboard/motion optimizations.
    /// </summary>
    public void InvalidateTaaHistory()
    {
        Array.Clear(_taaHistoryValid);
        _taaHasPrevCamera = false;
    }

    /// <summary>
    /// Caps every sample count to <see cref="MotionSampleCap"/> so that
    /// new samples quickly dominate stale data. Unlike a multiplicative
    /// decay this is frame-rate-independent: the effective window is
    /// always exactly <see cref="MotionSampleCap"/> samples regardless
    /// of how often pixels are revisited.
    /// </summary>
    public void SoftResetAccumulation()
    {
        _checkerPhase ^= 1;
        for (int i = 0; i < SampleCount.Length; i++)
            if (SampleCount[i] > MotionSampleCap)
                SampleCount[i] = MotionSampleCap;
    }

    public async Task AddJobs()
    {
        for (int y = 0; y < Height; y += TileSize)
        {
            for (int x = 0; x < Width; x += TileSize)
            {
                int w = Math.Min(TileSize, Width - x);
                int h = Math.Min(TileSize, Height - y);
                await Jobs.Writer.WriteAsync(new Tile(x, y, w, h));
            }
        }
    }

    static uint Hash2D(int x, int y)
    {
        uint h = (uint)(x * 374761393 + y * 668265263); // large primes
        h = (h ^ (h >> 13)) * 1274126177u;
        return h ^ (h >> 16);
    }

    /// <summary>
    /// Number of evenly-spaced companion wavelengths (including the hero)
    /// evaluated per BVH hit. More companions reduce spectral noise at
    /// the cost of extra single-primitive Intersect calls per trace.
    /// </summary>
    private const int CompanionCount = 4;

    private void Trace(Camera camera, int y, int x)
    {
        var ix = y * Width + x;

        // Sub-pixel jitter: vary the sample position within the pixel
        // so aliased staircase edges become noise that the running
        // average smooths into clean anti-aliased lines.
        float jx, jy;
        if (SubPixelJitter)
        {
            // Use a small, cheap RNG based on pixel hash + sample index to
            // produce decorrelated subpixel offsets per-sample and per-pixel.
            uint baseHash = Hash2D(x, y);
            uint seed = baseHash + (uint)(WavelengthCounter[ix] * 747796405u) + 2891336453u;
            // Advance LCG and extract 16 bits for jitter
            seed = seed * 747796405u + 2891336453u;
            jx = (seed & 0xFFFF) / 65536f;
            seed = seed * 747796405u + 2891336453u;
            jy = (seed & 0xFFFF) / 65536f;
        }
        else
        {
            jx = 0.5f;
            jy = 0.5f;
        }

        float px = (2f * ((x + jx) * _invWidth) - 1f) * _aspectTanHalfFov;
        float py = (1f - 2f * ((y + jy) * _invHeight)) * _tanHalfFov;

        // Ray from camera through pixel (apply camera rotation)
        Vector3 localDir = new Vector3(px, py, camera.ImgPlaneZ);
        Vector3 dir = Vector3.Normalize(Vector3.Transform(localDir, camera.Rotation));
        uint pixelHash = Hash2D(x, y);
        long sampleIdx = WavelengthCounter[ix];
        var heroWavelength = WavelengthLookup.GetHeroWavelength(pixelHash, sampleIdx);
        WavelengthCounter[ix]++;
        var ray = new Ray()
        {
            Origin = camera.Position,
            Direction = dir,
            Wavelength = heroWavelength,
            Intensity = 1f
        };
        var (reflectance, hitPoint, hitNormal, _, hit, hitPrimitive) = _bvh.FindClosest(ray);
        Vector3 xyz = Vector3.Zero;
        Vector3 directLightingXYZ = Vector3.Zero;
        Vector3 indirectLightingXYZ = Vector3.Zero;
        Vector3 emissiveLightingXYZ = Vector3.Zero;
        // Bounce accumulators declared here so they are available after hit processing
        Vector3 bounce0 = Vector3.Zero;
        Vector3 bounce1 = Vector3.Zero;
        Vector3 bounce2plus = Vector3.Zero;
        if (hit)
        {
            // ── Compute lighting factor (geometric, wavelength-independent) ─
            float ambientTerm = 1f;
            float directTerm = 0f;
            if (Lighting != LightingMode.None && _lights.Length > 0)
            {
                ambientTerm = AmbientLevel;

                // Monte Carlo: pick one random light. Use distance-weighted sampling
                // (weight = 1 / dist^2) to prefer nearby lights in enclosed scenes.
                // Use the same cheap RNG seeded by pixel and sample index.
                uint rngLight = pixelHash + (uint)(sampleIdx * 747796405u) + 2891336453u;

                int nLights = _lights.Length;

                // Local helper: select a light index using distance-weighted probabilities.
                int SelectLight(ref uint rng, Vector3 samplePoint, Vector3 normal, out float outP, out Vector3 outDir, out float outDistSq, out float outCos)
                {
                    // Build weights = 1 / distSq for lights with positive cosine; zero otherwise.
                    Span<float> weights = stackalloc float[nLights];
                    float totalW = 0f;
                    for (int li = 0; li < nLights; li++)
                    {
                        var L = _lights[li];
                        Vector3 toL = L.Position - samplePoint;
                        float dsq = Vector3.Dot(toL, toL);
                        float dist = MathF.Sqrt(dsq);
                        Vector3 ldir = toL / dist;
                        float cos = Vector3.Dot(normal, ldir);
                        float w = cos > 0f ? 1f / MathF.Max(dsq, 1e-6f) : 0f;
                        weights[li] = w;
                        totalW += w;
                    }

                    // Advance RNG and draw a uniform sample
                    rng = rng * 747796405u + 2891336453u;

                    if (totalW > 0f)
                    {
                        // Use full-range uint -> [0,1) mapping
                        float u = (rng) / 4294967296f * totalW;
                        float acc = 0f;
                        for (int li = 0; li < nLights; li++)
                        {
                            acc += weights[li];
                            if (u <= acc)
                            {
                                var sel = _lights[li];
                                Vector3 toSel = sel.Position - samplePoint;
                                outDistSq = MathF.Max(Vector3.Dot(toSel, toSel), 1e-6f);
                                float dist = MathF.Sqrt(outDistSq);
                                outDir = toSel / dist;
                                outCos = Vector3.Dot(normal, outDir);
                                outP = weights[li] / totalW;
                                return li;
                            }
                        }
                        // Fallback to last
                        int last = nLights - 1;
                        var lastL = _lights[last];
                        Vector3 toLast = lastL.Position - samplePoint;
                        outDistSq = MathF.Max(Vector3.Dot(toLast, toLast), 1e-6f);
                        float lastDist = MathF.Sqrt(outDistSq);
                        outDir = toLast / lastDist;
                        outCos = Vector3.Dot(normal, outDir);
                        outP = weights[last] / totalW;
                        return last;
                    }

                    // If all weights are zero (no lights on the lit hemisphere), fall back to uniform selection.
                    int idx = (int)(rng % (uint)nLights);
                    var fallback = _lights[idx];
                    Vector3 toF = fallback.Position - samplePoint;
                    outDistSq = MathF.Max(Vector3.Dot(toF, toF), 1e-6f);
                    float fd = MathF.Sqrt(outDistSq);
                    outDir = toF / fd;
                    outCos = Vector3.Dot(normal, outDir);
                    outP = 1f / nLights;
                    return idx;
                }

                // Select primary light
                float lightP;
                Vector3 lightDir;
                float lightDistSq;
                float lightCos;
                int lightIdx = SelectLight(ref rngLight, hitPoint, hitNormal, out lightP, out lightDir, out lightDistSq, out lightCos);

                if (lightCos > 0f)
                {
                    bool visible = true;

                    if (Lighting == LightingMode.NEE)
                    {
                        // Cast a shadow ray to test occlusion
                        var shadowRay = new Ray
                        {
                            Origin = hitPoint + hitNormal * 1e-3f,
                            Direction = lightDir,
                            Wavelength = ray.Wavelength,
                            Intensity = 1f
                        };
                        visible = !_bvh.IsOccluded(shadowRay, MathF.Sqrt(lightDistSq) - 2e-3f);
                    }

                    if (visible)
                    {
                        // Unbiased single-light estimator: account for sampling probability p
                        directTerm += lightCos / lightDistSq * LightIntensity / Math.Max(lightP, 1e-9f);
                    }
                }
            }

            // ── Multi-wavelength spectral evaluation ──────────────────
            // Evaluate the hero wavelength plus evenly-spaced companions
            // at the same hit point. The BVH traversal (expensive) was
            // done once; each companion only re-evaluates one primitive.
            int deterCount = WavelengthLookup.DeterministicCount;
            int heroIdx = (int)((pixelHash + sampleIdx) % deterCount);
            int stride = deterCount / CompanionCount;

            // Hero contribution (already computed by FindClosest).
            if (WavelengthLookup.TryGet(heroWavelength, out var heroXyz))
                xyz = heroXyz * reflectance;

            // Companion contributions.
            int evaluated = 1;
            for (int c = 1; c < CompanionCount; c++)
            {
                int compIdx = (heroIdx + c * stride) % deterCount;
                int compWl = WavelengthLookup.GetDeterministicWavelength(compIdx);

                if (WavelengthLookup.TryGet(compWl, out var compXyz))
                {
                    // Re-evaluate reflectance at the companion wavelength
                    // on the same primitive (cheap, no BVH traversal).
                    var compRay = ray;
                    compRay.Wavelength = compWl;
                    var compHit = hitPrimitive!.Intersect(compRay);
                    if (compHit.HasValue)
                    {
                        xyz += compXyz * compHit.Value.reflectance;
                        evaluated++;
                    }
                }
            }

            var baseXyz = xyz / evaluated;

            // Start with ambient + direct (NEE) as before.
            // Prepare bounce accumulators so they're in scope regardless of lighting path.

            if (Lighting != LightingMode.None && _lights.Length > 0)
            {
                directLightingXYZ = baseXyz * directTerm;
                xyz = baseXyz * (ambientTerm + directTerm);

                // --- Multi-bounce indirect estimator (simple, low-cost) --------------------
                // We extend the previous one-bounce estimator by optionally tracing one
                // additional bounce to populate a "bounce2+" channel. This is still a
                // heavily simplified Lambertian-style estimator but gives a useful
                // separation: bounce0 (direct), bounce1 (single-bounce indirect),
                // bounce2+ (two-or-more bounces).
                Vector3 localBounce0 = baseXyz * directTerm; // direct lighting contribution
                Vector3 localBounce1 = Vector3.Zero;
                Vector3 localBounce2plus = Vector3.Zero;

                // Cheap RNG for hemisphere sampling: advance LCG twice to get two floats.
                uint rng = pixelHash + (uint)(sampleIdx * 747796405u) + 2891336453u;
                rng = rng * 747796405u + 2891336453u;
                float r1 = (rng & 0xFFFF) / 65536f;
                rng = rng * 747796405u + 2891336453u;
                float r2 = (rng & 0xFFFF) / 65536f;

                // First bounce (secondary)
                float sqrtR1 = MathF.Sqrt(r1);
                float theta = 2f * MathF.PI * r2;
                float sx = sqrtR1 * MathF.Cos(theta);
                float sy = sqrtR1 * MathF.Sin(theta);
                float sz = MathF.Sqrt(MathF.Max(0f, 1f - r1));

                Vector3 n = hitNormal;
                Vector3 tangent = MathF.Abs(n.X) > 0.1f ? Vector3.Normalize(new Vector3(n.Y, -n.X, 0f)) : Vector3.Normalize(new Vector3(0f, n.Z, -n.Y));
                Vector3 bitangent = Vector3.Cross(n, tangent);
                Vector3 sampleDir = Vector3.Normalize(sx * tangent + sy * bitangent + sz * n);

                var secRay = new Ray
                {
                    Origin = hitPoint + hitNormal * 1e-3f,
                    Direction = sampleDir,
                    Wavelength = ray.Wavelength,
                    Intensity = 1f
                };

                var (secReflectance, secHitPoint, secHitNormal, _, secHit, secPrimitive) = _bvh.FindClosest(secRay);
                if (secHit && secPrimitive is not null)
                {
                    Vector3 secBaseXyz = Vector3.Zero;
                    if (WavelengthLookup.TryGet((int)secRay.Wavelength, out var secHeroXyz))
                        secBaseXyz = secHeroXyz * secReflectance;

                    // Direct lighting at secondary (stochastic single-light NEE like primary)
                    float secDirectTerm = 0f;
                    if (_lights.Length > 0)
                    {
                        rng = rng * 747796405u + 2891336453u;
                        int lightIdx2 = (int)(rng % (uint)_lights.Length);
                        var light2 = _lights[lightIdx2];
                        Vector3 toLight2 = light2.Position - secHitPoint;
                        float distSq2 = Vector3.Dot(toLight2, toLight2);
                        float dist2 = MathF.Sqrt(distSq2);
                        Vector3 lightDir2 = toLight2 / dist2;
                        float cosTheta2 = Vector3.Dot(secHitNormal, lightDir2);
                        if (cosTheta2 > 0f)
                        {
                            var shadow = new Ray
                            {
                                Origin = secHitPoint + secHitNormal * 1e-3f,
                                Direction = lightDir2,
                                Wavelength = secRay.Wavelength,
                                Intensity = 1f
                            };
                            bool visible2 = !_bvh.IsOccluded(shadow, dist2 - 2e-3f);
                            if (visible2)
                                secDirectTerm += cosTheta2 / distSq2 * LightIntensity * _lights.Length;
                        }
                    }

                    Vector3 secIncoming = secBaseXyz * (AmbientLevel + secDirectTerm);
                    // Contribution from single-bounce paths (primary <- secondary <- light)
                    localBounce1 = baseXyz * secIncoming;

                    // Second bounce (tertiary) to populate bounce2+ channel
                    // Sample hemisphere around secondary normal again
                    rng = rng * 747796405u + 2891336453u;
                    float r3 = (rng & 0xFFFF) / 65536f;
                    rng = rng * 747796405u + 2891336453u;
                    float r4 = (rng & 0xFFFF) / 65536f;
                    float sqrtR3 = MathF.Sqrt(r3);
                    float theta2 = 2f * MathF.PI * r4;
                    float tx = sqrtR3 * MathF.Cos(theta2);
                    float ty = sqrtR3 * MathF.Sin(theta2);
                    float tz = MathF.Sqrt(MathF.Max(0f, 1f - r3));

                    Vector3 sn = secHitNormal;
                    Vector3 st = MathF.Abs(sn.X) > 0.1f ? Vector3.Normalize(new Vector3(sn.Y, -sn.X, 0f)) : Vector3.Normalize(new Vector3(0f, sn.Z, -sn.Y));
                    Vector3 sb = Vector3.Cross(sn, st);
                    Vector3 secSampleDir = Vector3.Normalize(tx * st + ty * sb + tz * sn);

                    var tertRay = new Ray
                    {
                        Origin = secHitPoint + secHitNormal * 1e-3f,
                        Direction = secSampleDir,
                        Wavelength = secRay.Wavelength,
                        Intensity = 1f
                    };

                    var (tertReflectance, tertHitPoint, tertHitNormal, _, tertHit, tertPrimitive) = _bvh.FindClosest(tertRay);
                    if (tertHit && tertPrimitive is not null)
                    {
                        Vector3 tertBaseXyz = Vector3.Zero;
                        if (WavelengthLookup.TryGet((int)tertRay.Wavelength, out var tertHeroXyz))
                            tertBaseXyz = tertHeroXyz * tertReflectance;

                        float tertDirectTerm = 0f;
                        if (_lights.Length > 0)
                        {
                            rng = rng * 747796405u + 2891336453u;
                            int lightIdx3 = (int)(rng % (uint)_lights.Length);
                            var light3 = _lights[lightIdx3];
                            Vector3 toLight3 = light3.Position - tertHitPoint;
                            float distSq3 = Vector3.Dot(toLight3, toLight3);
                            float dist3 = MathF.Sqrt(distSq3);
                            Vector3 lightDir3 = toLight3 / dist3;
                            float cosTheta3 = Vector3.Dot(tertHitNormal, lightDir3);
                            if (cosTheta3 > 0f)
                            {
                                var shadow3 = new Ray
                                {
                                    Origin = tertHitPoint + tertHitNormal * 1e-3f,
                                    Direction = lightDir3,
                                    Wavelength = tertRay.Wavelength,
                                    Intensity = 1f
                                };
                                bool visible3 = !_bvh.IsOccluded(shadow3, dist3 - 2e-3f);
                                if (visible3)
                                    tertDirectTerm += cosTheta3 / distSq3 * LightIntensity * _lights.Length;
                            }
                        }

                        Vector3 tertIncoming = tertBaseXyz * (AmbientLevel + tertDirectTerm);
                        // Contribution from two-bounce+ paths: primary <- secondary <- tertiary <- light
                        localBounce2plus = baseXyz * secBaseXyz * tertIncoming;
                    }

                    indirectLightingXYZ = localBounce1 + localBounce2plus;
                    // expose local bounce results to outer scope accumulators
                    bounce0 = localBounce0;
                    bounce1 = localBounce1;
                    bounce2plus = localBounce2plus;
                    xyz += indirectLightingXYZ;
                }
                // --- end multi-bounce estimator -------------------------------------------
            }
            else
            {
                // When no explicit light model is enabled, treat beauty as direct.
                directLightingXYZ = baseXyz;
                xyz = baseXyz;
                // Ensure bounce channels reflect the direct-only model.
                bounce0 = baseXyz;
                bounce1 = Vector3.Zero;
                bounce2plus = Vector3.Zero;
            }
        }

        if (hit != LastHit[ix])
        {
            AccumXYZ[ix] = Vector3.Zero;
            SampleCount[ix] = 0;
            _lumaM2[ix] = 0f;
            _lumaVariance[ix] = 0f;
            _lumaDirectM2[ix] = 0f;
            _lumaIndirectM2[ix] = 0f;
            _lumaDirectVariance[ix] = 0f;
            _lumaIndirectVariance[ix] = 0f;
            _clampAmount[ix] = 0f;
            _depthDistance[ix] = 0f;
            _albedoScalar[ix] = 0f;
            _normalWorld[ix] = Vector3.Zero;
            _directLightingXYZ[ix] = Vector3.Zero;
            _indirectLightingXYZ[ix] = Vector3.Zero;
            _emissiveLightingXYZ[ix] = Vector3.Zero;
            LastHit[ix] = hit;
        }
        if (hit)
        {
            HitPointWorld[ix] = hitPoint;
            _depthDistance[ix] = Vector3.Distance(camera.Position, hitPoint);
            _albedoScalar[ix] = Math.Clamp(reflectance, 0f, 1f);
            _normalWorld[ix] = hitNormal;
        }

        var correctedXYZ = xyz * WavelengthLookup.DeterministicCorrection;
        var correctedDirect = (bounce0) * WavelengthLookup.DeterministicCorrection;
        var correctedIndirect = (indirectLightingXYZ) * WavelengthLookup.DeterministicCorrection;
        var correctedBounce2Plus = (bounce2plus) * WavelengthLookup.DeterministicCorrection;
        var correctedEmissive = emissiveLightingXYZ * WavelengthLookup.DeterministicCorrection;

        // Clamp extreme spectral contributions to suppress fireflies.
        if (SampleClamp > 0f)
        {
            Vector3 unclamped = correctedXYZ;
            correctedXYZ = Vector3.Clamp(correctedXYZ, Vector3.Zero,
                new Vector3(SampleClamp, SampleClamp, SampleClamp));
            float clampDelta = MathF.Abs(unclamped.X - correctedXYZ.X) +
                MathF.Abs(unclamped.Y - correctedXYZ.Y) +
                MathF.Abs(unclamped.Z - correctedXYZ.Z);
            if (clampDelta > 0f)
            {
                _clampAmount[ix] += clampDelta;
                _clampHitFrame[ix] = true;
                System.Threading.Interlocked.Increment(ref _clampEventCount);
            }
        }

        if (SampleCount[ix] < MaxSampleCount)
            SampleCount[ix] += 1;
        uint count = SampleCount[ix];
        AccumXYZ[ix] += (correctedXYZ - AccumXYZ[ix]) / count;
        _directLightingXYZ[ix] += (correctedDirect - _directLightingXYZ[ix]) / count;
        _indirectLightingXYZ[ix] += (correctedIndirect - _indirectLightingXYZ[ix]) / count;
        _emissiveLightingXYZ[ix] += (correctedEmissive - _emissiveLightingXYZ[ix]) / count;
        // Bounce breakdown approximations: assign direct to bounce0, one-bounce indirect to bounce1,
        // and any remaining indirect energy to bounce2+. Use correctedDirect/Indirect as proxies.
        _bounce0XYZ[ix] += (correctedDirect - _bounce0XYZ[ix]) / count;
        _bounce1XYZ[ix] += (correctedIndirect - _bounce1XYZ[ix]) / count;
        _bounce2PlusXYZ[ix] += (correctedBounce2Plus - _bounce2PlusXYZ[ix]) / count;

        // Welford-style running variance on luminance (Y channel).
        float ySample = correctedXYZ.Y;
        float yMean = AccumXYZ[ix].Y;
        float delta = ySample - yMean;
        _lumaM2[ix] += delta * (ySample - yMean);
        _lumaVariance[ix] = count > 1 ? _lumaM2[ix] / (count - 1) : 0f;

        // Approximate variance split: attribute sample luminance to direct and indirect
        float directY = correctedDirect.Y;
        float indirectY = correctedIndirect.Y;
        // Normalize contributions to avoid double-counting when both non-zero
        float totalContrib = directY + indirectY;
        float dFrac = totalContrib > 0f ? directY / totalContrib : 0f;
        float iFrac = totalContrib > 0f ? indirectY / totalContrib : 0f;

        // Update direct variance accumulator
        float yDirectSample = ySample * dFrac;
        float directMean = _directLightingXYZ[ix].Y; // use accumulated direct as mean proxy
        float dDelta = yDirectSample - directMean;
        _lumaDirectM2[ix] += dDelta * (yDirectSample - directMean);
        _lumaDirectVariance[ix] = count > 1 ? _lumaDirectM2[ix] / (count - 1) : 0f;

        // Update indirect variance accumulator
        float yIndirectSample = ySample * iFrac;
        float indirectMean = _indirectLightingXYZ[ix].Y; // use accumulated indirect as mean proxy
        float iDelta = yIndirectSample - indirectMean;
        _lumaIndirectM2[ix] += iDelta * (yIndirectSample - indirectMean);
        _lumaIndirectVariance[ix] = count > 1 ? _lumaIndirectM2[ix] / (count - 1) : 0f;

        // Record the last frame this pixel received an update for the history-age debug view.
        // FrameIndex is incremented on the UI thread in ResolveDisplayBufferWithTaa();
        // writing it here is best-effort and races are acceptable for diagnostics.
        _lastUpdatedFrame[ix] = FrameIndex;
    }

    public string GetDebugLegend(DebugViewMode mode)
    {
        return mode switch
        {
            DebugViewMode.Beauty => "Debug: Beauty | Range: scene-referred color",
            DebugViewMode.SampleCount => $"Debug: Effective Sample Count | Range: 0 - {Math.Max(1u, MaxObservedSampleCount)}",
            DebugViewMode.Variance => $"Debug: Variance Heatmap | Range: 0.000 - {Math.Max(0.0001, AverageVariance * 8):0.000}",
            DebugViewMode.HistoryWeight => "Debug: History Weight | 0 = current only, 1 = history dominated",
            DebugViewMode.RejectionMask => "Debug: Rejection Mask | Green = reused, Red = rejected",
            DebugViewMode.ClampHeatmap => $"Debug: Clamp Heatmap | Active: {ClampedPixelPercent:0.0}%",
            DebugViewMode.Depth => "Debug: Depth | Dark = near, bright = far",
            DebugViewMode.Albedo => "Debug: Albedo | Dark = low reflectance, bright = high reflectance",
            DebugViewMode.Normal => "Debug: Normal | RGB = world normal",
            DebugViewMode.DirectLighting => "Debug: Direct lighting only",
            DebugViewMode.IndirectLighting => "Debug: Indirect lighting only (currently zero in this tracer)",
            DebugViewMode.EmissiveLighting => "Debug: Emissive lighting only (currently zero in this tracer)",
            DebugViewMode.CurrentVsAccumDiff => "Debug: abs(current - accumulated)",
            DebugViewMode.UnfilteredVsFilteredDiff => "Debug: abs(unfiltered - filtered)",
            DebugViewMode.ReprojectedVsCurrentDiff => "Debug: abs(reprojected_history - current)",
            DebugViewMode.HistoryAge => "Debug: History Age | Blue=fresh, Red=stale",
            _ => "Debug: Unknown"
        };
    }

    public void RenderDebugModeToBuffer(DebugViewMode mode, byte[] targetBuffer, int targetStride)
    {
        for (int y = 0; y < Height; y++)
        {
            int row = y * Width;
            for (int x = 0; x < Width; x++)
            {
                int ix = row + x;
                // Precompute an edge disagreement scalar only when requested to keep
                // the common path cheap. We'll still compute it on-demand below.
                float edgeDisagreement = 0f;
                if (mode == DebugViewMode.EdgeDisagreement)
                {
                    // Color disagreement: use existing diff metrics (current vs accum,
                    // unfiltered vs filtered, reprojection vs current) and take the max.
                    float colorDiff = MathF.Max(_diffCurrentVsAccum[ix], MathF.Max(_diffUnfilteredVsFiltered[ix], _diffReprojectedVsCurrent[ix]));

                    // Depth disagreement: compare with 4-neighbors and take max absolute delta
                    float depth = _depthDistance[ix];
                    float maxDepthDelta = 0f;
                    if (x > 0) maxDepthDelta = MathF.Max(maxDepthDelta, MathF.Abs(depth - _depthDistance[row + x - 1]));
                    if (x + 1 < Width) maxDepthDelta = MathF.Max(maxDepthDelta, MathF.Abs(depth - _depthDistance[row + x + 1]));
                    if (y > 0) maxDepthDelta = MathF.Max(maxDepthDelta, MathF.Abs(depth - _depthDistance[(y - 1) * Width + x]));
                    if (y + 1 < Height) maxDepthDelta = MathF.Max(maxDepthDelta, MathF.Abs(depth - _depthDistance[(y + 1) * Width + x]));
                    // Normalize depth delta by a soft scale to keep values reasonable.
                    float depthDiff = maxDepthDelta * 0.05f; // tuned scale

                    // Normal disagreement: measure angular deviation with neighbors using dot product.
                    Vector3 n = _normalWorld[ix];
                    float maxNormalDiff = 0f;
                    if (n != Vector3.Zero)
                    {
                        if (x > 0)
                        {
                            var nn = _normalWorld[row + x - 1];
                            if (nn != Vector3.Zero)
                        maxNormalDiff = MathF.Max(maxNormalDiff, 1f - Math.Clamp(Vector3.Dot(Vector3.Normalize(n), Vector3.Normalize(nn)), -1f, 1f));
                        }
                        if (x + 1 < Width)
                        {
                            var nn = _normalWorld[row + x + 1];
                            if (nn != Vector3.Zero)
                                maxNormalDiff = MathF.Max(maxNormalDiff, 1f - Math.Clamp(Vector3.Dot(Vector3.Normalize(n), Vector3.Normalize(nn)), -1f, 1f));
                        }
                        if (y > 0)
                        {
                            var nn = _normalWorld[(y - 1) * Width + x];
                            if (nn != Vector3.Zero)
                                maxNormalDiff = MathF.Max(maxNormalDiff, 1f - Math.Clamp(Vector3.Dot(Vector3.Normalize(n), Vector3.Normalize(nn)), -1f, 1f));
                        }
                        if (y + 1 < Height)
                        {
                            var nn = _normalWorld[(y + 1) * Width + x];
                            if (nn != Vector3.Zero)
                                maxNormalDiff = MathF.Max(maxNormalDiff, 1f - Math.Clamp(Vector3.Dot(Vector3.Normalize(n), Vector3.Normalize(nn)), -1f, 1f));
                        }
                    }

                    // Combine metrics with tuned weights. Color gets highest weight, normals second, depth last.
                    edgeDisagreement = MathF.Max(colorDiff, MathF.Max(maxNormalDiff * 0.9f, depthDiff * 0.6f));
                }
                Vector3 rgb = mode switch
                {
                    DebugViewMode.Beauty => ColorFromXyz(ResolveFilteredXYZ(y, x)),
                    DebugViewMode.SampleCount => PaletteSampleCount(SampleCount[ix], Math.Max(1u, MaxObservedSampleCount)),
                    DebugViewMode.Variance => PaletteVariance(_lumaVariance[ix], (float)Math.Max(0.0001, AverageVariance * 8)),
                    DebugViewMode.HistoryWeight => PaletteHistoryWeight(_historyWeight[ix]),
                    DebugViewMode.RejectionMask => _historyRejected[ix] > 0 ? new Vector3(1f, 0.1f, 0.1f) : new Vector3(0.1f, 0.9f, 0.1f),
                    DebugViewMode.ClampHeatmap => PaletteClamp(_clampAmount[ix]),
                    DebugViewMode.Depth => PaletteDepth(_depthDistance[ix]),
                    DebugViewMode.HistoryAge =>
                        // Map last-updated frame to age in frames relative to current FrameIndex.
                        PaletteDifference(MathF.Min(1f, (FrameIndex - _lastUpdatedFrame[ix]) / 120f)),
                    DebugViewMode.Albedo => PaletteAlbedo(_albedoScalar[ix]),
                    DebugViewMode.Normal => PaletteNormal(_normalWorld[ix]),
                    DebugViewMode.DirectLighting => ColorFromXyz(_directLightingXYZ[ix]),
                    DebugViewMode.IndirectLighting => ColorFromXyz(_indirectLightingXYZ[ix]),
                    DebugViewMode.EmissiveLighting => ColorFromXyz(_emissiveLightingXYZ[ix]),
                    DebugViewMode.CurrentVsAccumDiff => PaletteDifference(_diffCurrentVsAccum[ix]),
                    DebugViewMode.DirectVariance => PaletteVariance(_lumaDirectVariance[ix], (float)Math.Max(0.0001, AverageVariance * 8)),
                    DebugViewMode.IndirectVariance => PaletteVariance(_lumaIndirectVariance[ix], (float)Math.Max(0.0001, AverageVariance * 8)),
                    DebugViewMode.VarianceSplit =>
                        // composite: R = indirect, G = direct, B = average variance
                        new Vector3(Math.Clamp(_lumaIndirectVariance[ix] / (float)Math.Max(1e-6, AverageVariance * 8), 0f, 1f),
                                    Math.Clamp(_lumaDirectVariance[ix] / (float)Math.Max(1e-6, AverageVariance * 8), 0f, 1f),
                                    Math.Clamp(_lumaVariance[ix] / (float)Math.Max(1e-6, AverageVariance * 8), 0f, 1f)),
                    DebugViewMode.UnfilteredVsFilteredDiff => PaletteDifference(_diffUnfilteredVsFiltered[ix]),
                    DebugViewMode.ReprojectedVsCurrentDiff => PaletteDifference(_diffReprojectedVsCurrent[ix]),
                    DebugViewMode.Bounce0 => ColorFromXyz(_bounce0XYZ[ix]),
                    DebugViewMode.Bounce1 => ColorFromXyz(_bounce1XYZ[ix]),
                    DebugViewMode.Bounce2Plus => ColorFromXyz(_bounce2PlusXYZ[ix]),
                    DebugViewMode.BounceRGB => new Vector3(
                        Math.Clamp(_bounce2PlusXYZ[ix].Y / (float)Math.Max(1e-6, AverageVariance * 8), 0f, 1f),
                        Math.Clamp(_bounce1XYZ[ix].Y / (float)Math.Max(1e-6, AverageVariance * 8), 0f, 1f),
                        Math.Clamp(_bounce0XYZ[ix].Y / (float)Math.Max(1e-6, AverageVariance * 8), 0f, 1f)
                    ),
                    DebugViewMode.EdgeDisagreement => PaletteDifference(edgeDisagreement),
                    _ => Vector3.Zero
                };

                int i = y * targetStride + x * 4;
                targetBuffer[i + 0] = (byte)Math.Clamp(rgb.Z * 255f, 0, 255);
                targetBuffer[i + 1] = (byte)Math.Clamp(rgb.Y * 255f, 0, 255);
                targetBuffer[i + 2] = (byte)Math.Clamp(rgb.X * 255f, 0, 255);
                targetBuffer[i + 3] = 255;
            }
        }
    }

    private static Vector3 ColorFromXyz(Vector3 xyz)
    {
        var linearColor = ToSRGB(xyz);
        return new Vector3(
            LinearToSRGBStatic(linearColor.X),
            LinearToSRGBStatic(linearColor.Y),
            LinearToSRGBStatic(linearColor.Z)
        );
    }

    private static float LinearToSRGBStatic(float linear)
    {
        if (linear <= 0.0031308f)
            return 12.92f * linear;
        return 1.055f * MathF.Pow(linear, 1f / 2.4f) - 0.055f;
    }

    private static Vector3 PaletteSampleCount(uint sampleCount, uint maxSampleCount)
    {
        float t = maxSampleCount > 0 ? Math.Clamp(sampleCount / (float)maxSampleCount, 0f, 1f) : 0f;
        return MultiStop(t,
            new Vector3(0.20f, 0.00f, 0.35f),
            new Vector3(0.00f, 0.70f, 1.00f),
            new Vector3(0.10f, 0.90f, 0.20f),
            new Vector3(1.00f, 0.95f, 0.20f),
            new Vector3(1.00f, 1.00f, 1.00f));
    }

    private static Vector3 PaletteVariance(float variance, float maxVariance)
    {
        float t = maxVariance > 0f ? Math.Clamp(variance / maxVariance, 0f, 1f) : 0f;
        return MultiStop(t,
            new Vector3(0.00f, 0.00f, 0.20f),
            new Vector3(0.00f, 0.20f, 0.90f),
            new Vector3(0.00f, 0.80f, 0.30f),
            new Vector3(1.00f, 0.90f, 0.10f),
            new Vector3(1.00f, 0.20f, 0.10f));
    }

    private static Vector3 PaletteHistoryWeight(float weight)
    {
        float t = Math.Clamp(weight, 0f, 1f);
        return MultiStop(t,
            new Vector3(0.10f, 0.10f, 0.20f),
            new Vector3(0.20f, 0.80f, 1.00f),
            new Vector3(1.00f, 0.95f, 0.20f),
            new Vector3(1.00f, 0.50f, 0.10f),
            new Vector3(1.00f, 0.10f, 0.10f));
    }

    private static Vector3 PaletteClamp(float amount)
    {
        float t = 1f - MathF.Exp(-amount * 0.5f);
        return MultiStop(t,
            new Vector3(0.00f, 0.00f, 0.00f),
            new Vector3(1.00f, 0.50f, 0.10f),
            new Vector3(1.00f, 0.15f, 0.10f),
            new Vector3(1.00f, 0.80f, 0.70f),
            new Vector3(1.00f, 1.00f, 1.00f));
    }

    private static Vector3 PaletteDepth(float depth)
    {
        float t = 1f - MathF.Exp(-depth * 0.08f);
        return MultiStop(t,
            new Vector3(0.02f, 0.02f, 0.05f),
            new Vector3(0.08f, 0.15f, 0.45f),
            new Vector3(0.10f, 0.60f, 0.80f),
            new Vector3(0.90f, 0.90f, 0.45f),
            new Vector3(1.00f, 1.00f, 1.00f));
    }

    private static Vector3 PaletteDifference(float diff)
    {
        float t = 1f - MathF.Exp(-diff * 6f);
        return MultiStop(t,
            new Vector3(0.00f, 0.00f, 0.00f),
            new Vector3(0.10f, 0.15f, 0.50f),
            new Vector3(0.20f, 0.80f, 0.70f),
            new Vector3(0.95f, 0.90f, 0.20f),
            new Vector3(1.00f, 0.20f, 0.15f));
    }

    private static Vector3 PaletteAlbedo(float albedo)
    {
        float v = Math.Clamp(albedo, 0f, 1f);
        return new Vector3(v, v, v);
    }

    private static Vector3 PaletteNormal(Vector3 normal)
    {
        if (normal == Vector3.Zero)
            return Vector3.Zero;
        Vector3 n = Vector3.Normalize(normal);
        return n * 0.5f + new Vector3(0.5f, 0.5f, 0.5f);
    }

    private static Vector3 MultiStop(float t, Vector3 c0, Vector3 c1, Vector3 c2, Vector3 c3, Vector3 c4)
    {
        if (t <= 0.25f) return Vector3.Lerp(c0, c1, t / 0.25f);
        if (t <= 0.5f) return Vector3.Lerp(c1, c2, (t - 0.25f) / 0.25f);
        if (t <= 0.75f) return Vector3.Lerp(c2, c3, (t - 0.5f) / 0.25f);
        return Vector3.Lerp(c3, c4, (t - 0.75f) / 0.25f);
    }
}

/// <summary>
/// Lightweight container for per-pixel debug information exposed to the UI.
/// </summary>
public readonly record struct PixelDebugInfo(
    Vector3 AccumulatedXYZ,
    Vector3 FilteredXYZ,
    float CurrentVsAccumDiff,
    float ClampAmount,
    bool ClampHit,
    uint SampleCount,
    float HistoryWeight,
    byte HistoryRejected,
    float Depth,
    float Albedo,
    int LastUpdatedFrame,
    Vector3 Normal,
    Vector3 DirectLighting,
    Vector3 IndirectLighting,
    float UnfilteredVsFilteredDiff,
    float ReprojectedVsCurrentDiff,
    Vector3 Bounce0,
    Vector3 Bounce1,
    Vector3 Bounce2Plus
);

public class Tile
{
    public int X { get; init; }
    public int Y { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public Tile(int x, int y, int width, int height)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }
}
