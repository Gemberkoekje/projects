using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace RayTracer;

public partial class JobSystem
{
    private sealed class TaaResolver(JobSystem owner)
    {
        private readonly JobSystem _owner = owner;
        private readonly Stopwatch _resolveStopwatch = new();
        private const float InvGamma = 1.0f / 2.4f;

        public void ResolveDisplayBufferWithTaa()
        {
            _resolveStopwatch.Restart();

            bool useTaa = _owner.EnableTaa && _owner.TemporalBlendAlpha > 0f && _owner._taaHasPrevCamera;
            float alpha = Math.Clamp(_owner.TemporalBlendAlpha, 0.01f, 1f);
            if (_owner.IsMoving)
                alpha = Math.Clamp(alpha * 10f, 0.01f, 1f);

            Quaternion invPrevRot = useTaa ? Quaternion.Inverse(_owner._taaPrevCamRot) : default;

            long rejected = 0;
            double histWeightSum = 0;
            double varianceSum = 0;
            double sppSum = 0;
            uint maxSpp = 0;
            long clampedPixelCount = 0;

            for (int y = 0; y < _owner.Height; y++)
            {
                int row = y * _owner.Width;
                for (int x = 0; x < _owner.Width; x++)
                {
                    int ix = row + x;
                    Vector3 current = _owner.ResolveFilteredXYZ(y, x);
                    Vector3 unfiltered = _owner.AccumXYZ[ix];
                    Vector3 resolved = current;
                    float historyWeight = 0f;
                    bool rejectedThisPixel = false;
                    float reprojectionDelta = 0f;

                    bool currentHit = _owner.LastHit[ix];
                    Vector3 currentHitPoint = _owner.HitPointWorld[ix];

                    if (useTaa && currentHit && TryProjectToPrevPixel(currentHitPoint, invPrevRot, out float pxf, out float pyf))
                    {
                        int ix0 = (int)MathF.Floor(pxf);
                        int iy0 = (int)MathF.Floor(pyf);
                        float wx = pxf - ix0;
                        float wy = pyf - iy0;

                        int x0 = Math.Clamp(ix0, 0, _owner.Width - 1);
                        int y0 = Math.Clamp(iy0, 0, _owner.Height - 1);
                        int x1 = Math.Clamp(ix0 + 1, 0, _owner.Width - 1);
                        int y1 = Math.Clamp(iy0 + 1, 0, _owner.Height - 1);

                        int i00 = y0 * _owner.Width + x0;
                        int i10 = y0 * _owner.Width + x1;
                        int i01 = y1 * _owner.Width + x0;
                        int i11 = y1 * _owner.Width + x1;

                        float w00 = (1f - wx) * (1f - wy);
                        float w10 = wx * (1f - wy);
                        float w01 = (1f - wx) * wy;
                        float w11 = wx * wy;

                        float validWeight = (_owner._taaHistoryValid[i00] ? w00 : 0f) + (_owner._taaHistoryValid[i10] ? w10 : 0f) + (_owner._taaHistoryValid[i01] ? w01 : 0f) + (_owner._taaHistoryValid[i11] ? w11 : 0f);

                        if (validWeight > 0.25f)
                        {
                            Vector3 history = _owner._taaHistoryXYZ[i00] * w00 + _owner._taaHistoryXYZ[i10] * w10 + _owner._taaHistoryXYZ[i01] * w01 + _owner._taaHistoryXYZ[i11] * w11;
                            Vector3 historyHit = _owner._taaHistoryHitPoint[i00] * w00 + _owner._taaHistoryHitPoint[i10] * w10 + _owner._taaHistoryHitPoint[i01] * w01 + _owner._taaHistoryHitPoint[i11] * w11;

                            float reprojErr = Vector3.DistanceSquared(historyHit, currentHitPoint);
                            float reprojThreshold = _owner.IsMoving ? 0.0625f : 0.25f;
                            bool accept = reprojErr < reprojThreshold;

                            var histNormal = Vector3.Zero;
                            histNormal += _owner._normalWorld[i00] * w00;
                            histNormal += _owner._normalWorld[i10] * w10;
                            histNormal += _owner._normalWorld[i01] * w01;
                            histNormal += _owner._normalWorld[i11] * w11;
                            var currNormal = _owner._normalWorld[ix];
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

                    _owner._taaNextXYZ[ix] = resolved;
                    _owner._taaNextHitPoint[ix] = currentHitPoint;
                    _owner._taaNextValid[ix] = currentHit;
                    _owner._historyWeight[ix] = historyWeight;
                    _owner._historyRejected[ix] = rejectedThisPixel ? (byte)1 : (byte)0;
                    _owner._diffReprojectedVsCurrent[ix] = reprojectionDelta;
                    _owner._diffCurrentVsAccum[ix] = Vector3.Distance(current, resolved);
                    _owner._diffUnfilteredVsFiltered[ix] = Vector3.Distance(unfiltered, current);

                    if (rejectedThisPixel)
                        rejected++;

                    histWeightSum += historyWeight;
                    varianceSum += _owner._lumaVariance[ix];
                    uint spp = _owner.SampleCount[ix];
                    sppSum += spp;
                    if (spp > maxSpp)
                        maxSpp = spp;
                    if (_owner._clampHitFrame[ix])
                        clampedPixelCount++;

                    WriteColorToBuffer(y, x, resolved);
                }
            }

            if (useTaa || !_owner._taaHasPrevCamera)
            {
                Array.Copy(_owner._taaNextXYZ, _owner._taaHistoryXYZ, _owner._taaNextXYZ.Length);
                Array.Copy(_owner._taaNextHitPoint, _owner._taaHistoryHitPoint, _owner._taaNextHitPoint.Length);
                Array.Copy(_owner._taaNextValid, _owner._taaHistoryValid, _owner._taaNextValid.Length);
            }
            else
            {
                for (int i = 0; i < _owner._taaHistoryXYZ.Length; i++)
                {
                    _owner._taaHistoryXYZ[i] = _owner._taaNextXYZ[i];
                    _owner._taaHistoryHitPoint[i] = _owner._taaNextHitPoint[i];
                    _owner._taaHistoryValid[i] = _owner._taaNextValid[i];
                }
            }

            _owner._taaPrevCamPos = _owner.Camera.Position;
            _owner._taaPrevCamRot = _owner.Camera.Rotation;
            _owner._taaHasPrevCamera = true;
            _owner.FrameIndex++;

            int pixels = _owner.Width * _owner.Height;
            _owner.RejectedHistoryPercent = pixels > 0 ? (double)rejected / pixels * 100.0 : 0.0;
            _owner.ClampedPixelPercent = pixels > 0 ? (double)clampedPixelCount / pixels * 100.0 : 0.0;
            _owner.AverageVariance = pixels > 0 ? varianceSum / pixels : 0.0;
            _owner.AverageHistoryWeight = pixels > 0 ? histWeightSum / pixels : 0.0;
            _owner.AverageEffectiveSpp = pixels > 0 ? sppSum / pixels : 0.0;
            _owner.MaxObservedSampleCount = maxSpp;
            Array.Clear(_owner._clampHitFrame);

            _resolveStopwatch.Stop();
            _owner.LastFrameDiagnostics = new FrameDiagnostics
            {
                ResolveTaaMs = _resolveStopwatch.Elapsed.TotalMilliseconds,
                FrameIndex = _owner.FrameIndex,
                RejectedHistoryPercent = _owner.RejectedHistoryPercent,
                AverageHistoryWeight = _owner.AverageHistoryWeight,
                AverageVariance = _owner.AverageVariance,
                AverageEffectiveSpp = _owner.AverageEffectiveSpp,
                ClampedPixelPercent = _owner.ClampedPixelPercent,
                TotalRays = _owner.TotalRays,
                TotalTileCompletions = _owner.TotalTileCompletions
            };
        }

        private void ComputeNeighborhoodMinMax(int y, int x, out Vector3 nMin, out Vector3 nMax)
        {
            nMin = new Vector3(float.MaxValue);
            nMax = new Vector3(float.MinValue);
            int yMin = Math.Max(y - 1, 0);
            int yMax = Math.Min(y + 1, _owner.Height - 1);
            int xMin = Math.Max(x - 1, 0);
            int xMax = Math.Min(x + 1, _owner.Width - 1);
            for (int ny = yMin; ny <= yMax; ny++)
            {
                int rowOff = ny * _owner.Width;
                for (int nx = xMin; nx <= xMax; nx++)
                {
                    Vector3 v = _owner.ResolveFilteredXYZ(ny, nx);
                    nMin = Vector3.Min(nMin, v);
                    nMax = Vector3.Max(nMax, v);
                }
            }
        }

        private bool TryProjectToPrevPixel(Vector3 worldPoint, Quaternion invPrevRot, out float px, out float py)
        {
            Vector3 local = Vector3.Transform(worldPoint - _owner._taaPrevCamPos, invPrevRot);
            if (local.Z <= 1e-4f)
            {
                px = py = 0f;
                return false;
            }

            float ndcX = local.X / (local.Z * _owner._aspectTanHalfFov);
            float ndcY = local.Y / (local.Z * _owner._tanHalfFov);

            float fx = ((ndcX + 1f) * 0.5f) * _owner.Width - 0.5f;
            float fy = ((1f - ndcY) * 0.5f) * _owner.Height - 0.5f;

            px = fx;
            py = fy;
            return px >= 0f && px < _owner.Width && py >= 0f && py < _owner.Height;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void WriteColorToBuffer(int y, int x, Vector3 xyz)
        {
            DisplayResolver.WritePixel(_owner.DisplayBuffer.AsSpan(y * _owner.Stride + x * 4, 4), xyz);
        }
    }
}
