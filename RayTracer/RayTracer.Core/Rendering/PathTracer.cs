using System.Numerics;

namespace RayTracer;

public partial class JobSystem
{
    static uint Hash2D(int x, int y)
    {
        uint h = (uint)(x * 374761393 + y * 668265263);
        h = (h ^ (h >> 13)) * 1274126177u;
        return h ^ (h >> 16);
    }

    /// <summary>
    /// Number of evenly-spaced companion wavelengths (including the hero)
    /// evaluated per BVH hit. More companions reduce spectral noise at
    /// the cost of extra single-primitive Intersect calls per trace.
    /// </summary>
    private const int CompanionCount = 4;

    internal void TraceCore(Camera camera, int y, int x)
    {
        RenderBuffers buffers = _buffers;
        Vector3[] hitPointWorld = buffers.HitPointWorld;
        float[] lumaM2 = buffers.LumaM2;
        float[] lumaVariance = buffers.LumaVariance;
        float[] lumaDirectM2 = buffers.LumaDirectM2;
        float[] lumaIndirectM2 = buffers.LumaIndirectM2;
        float[] lumaDirectVariance = buffers.LumaDirectVariance;
        float[] lumaIndirectVariance = buffers.LumaIndirectVariance;
        float[] clampAmount = buffers.ClampAmount;
        bool[] clampHitFrame = buffers.ClampHitFrame;
        float[] depthDistance = buffers.DepthDistance;
        float[] albedoScalar = buffers.AlbedoScalar;
        Vector3[] normalWorld = buffers.NormalWorld;
        Vector3[] directLightingXYZ = buffers.DirectLightingXYZ;
        Vector3[] indirectLightingXYZ = buffers.IndirectLightingXYZ;
        Vector3[] emissiveLightingXYZ = buffers.EmissiveLightingXYZ;
        Vector3[] bounce0XYZ = buffers.Bounce0XYZ;
        Vector3[] bounce1XYZ = buffers.Bounce1XYZ;
        Vector3[] bounce2PlusXYZ = buffers.Bounce2PlusXYZ;
        int[] lastUpdatedFrame = buffers.LastUpdatedFrame;
        byte[] diffuseCacheState = buffers.DiffuseCacheState;

        var ix = y * Width + x;

        float jx, jy;
        if (SubPixelJitter)
        {
            uint baseHash = Hash2D(x, y);
            uint seed = baseHash + (uint)(WavelengthCounter[ix] * 747796405u) + 2891336453u;
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
        Vector3 directLighting = Vector3.Zero;
        Vector3 indirectLighting = Vector3.Zero;
        Vector3 emissiveLighting = Vector3.Zero;
        Vector3 bounce0 = Vector3.Zero;
        Vector3 bounce1 = Vector3.Zero;
        Vector3 bounce2plus = Vector3.Zero;
        if (hit)
        {
            float ambientTerm = 1f;
            float directTerm = 0f;
            if (Lighting != LightingMode.None && _lights.Length > 0)
            {
                ambientTerm = AmbientLevel;
                uint rngLight = pixelHash + (uint)(sampleIdx * 747796405u) + 2891336453u;

                int nLights = _lights.Length;

                int SelectLight(ref uint rng, Vector3 samplePoint, Vector3 normal, out float outP, out Vector3 outDir, out float outDistSq, out float outCos)
                {
                    Span<float> weights = stackalloc float[nLights];
                    float totalW = 0f;
                    for (int li = 0; li < nLights; li++)
                    {
                        var light = _lights[li];
                        Vector3 toLight = light.Position - samplePoint;
                        float dsq = Vector3.Dot(toLight, toLight);
                        float dist = MathF.Sqrt(dsq);
                        Vector3 ldir = toLight / dist;
                        float cos = Vector3.Dot(normal, ldir);
                        float w = cos > 0f ? 1f / MathF.Max(dsq, 1e-6f) : 0f;
                        weights[li] = w;
                        totalW += w;
                    }

                    rng = rng * 747796405u + 2891336453u;

                    if (totalW > 0f)
                    {
                        float u = rng / 4294967296f * totalW;
                        float acc = 0f;
                        for (int li = 0; li < nLights; li++)
                        {
                            acc += weights[li];
                            if (u <= acc)
                            {
                                var selected = _lights[li];
                                Vector3 toSelected = selected.Position - samplePoint;
                                outDistSq = MathF.Max(Vector3.Dot(toSelected, toSelected), 1e-6f);
                                float dist = MathF.Sqrt(outDistSq);
                                outDir = toSelected / dist;
                                outCos = Vector3.Dot(normal, outDir);
                                outP = weights[li] / totalW;
                                return li;
                            }
                        }

                        int last = nLights - 1;
                        var lastLight = _lights[last];
                        Vector3 toLast = lastLight.Position - samplePoint;
                        outDistSq = MathF.Max(Vector3.Dot(toLast, toLast), 1e-6f);
                        float lastDist = MathF.Sqrt(outDistSq);
                        outDir = toLast / lastDist;
                        outCos = Vector3.Dot(normal, outDir);
                        outP = weights[last] / totalW;
                        return last;
                    }

                    int idx = (int)(rng % (uint)nLights);
                    var fallback = _lights[idx];
                    Vector3 toFallback = fallback.Position - samplePoint;
                    outDistSq = MathF.Max(Vector3.Dot(toFallback, toFallback), 1e-6f);
                    float fallbackDist = MathF.Sqrt(outDistSq);
                    outDir = toFallback / fallbackDist;
                    outCos = Vector3.Dot(normal, outDir);
                    outP = 1f / nLights;
                    return idx;
                }

                float lightP;
                Vector3 lightDir;
                float lightDistSq;
                float lightCos;
                _ = SelectLight(ref rngLight, hitPoint, hitNormal, out lightP, out lightDir, out lightDistSq, out lightCos);

                if (lightCos > 0f)
                {
                    bool visible = true;
                    if (Lighting == LightingMode.NEE)
                    {
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
                        directTerm += lightCos / lightDistSq * LightIntensity / Math.Max(lightP, 1e-9f);
                }
            }

            int deterCount = WavelengthLookup.DeterministicCount;
            int heroIdx = (int)((pixelHash + sampleIdx) % deterCount);
            int stride = deterCount / CompanionCount;

            if (WavelengthLookup.TryGet(heroWavelength, out var heroXyz))
                xyz = heroXyz * reflectance;

            int evaluated = 1;
            for (int c = 1; c < CompanionCount; c++)
            {
                int compIdx = (heroIdx + c * stride) % deterCount;
                int compWl = WavelengthLookup.GetDeterministicWavelength(compIdx);

                if (WavelengthLookup.TryGet(compWl, out var compXyz))
                {
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

            if (Lighting != LightingMode.None && _lights.Length > 0)
            {
                directLighting = baseXyz * directTerm;
                xyz = baseXyz * (ambientTerm + directTerm);

                Vector3 localBounce0 = baseXyz * directTerm;
                Vector3 localBounce1 = Vector3.Zero;
                Vector3 localBounce2plus = Vector3.Zero;

                uint rng = pixelHash + (uint)(sampleIdx * 747796405u) + 2891336453u;
                rng = rng * 747796405u + 2891336453u;
                float r1 = (rng & 0xFFFF) / 65536f;
                rng = rng * 747796405u + 2891336453u;
                float r2 = (rng & 0xFFFF) / 65536f;

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
                    Vector3 secBounce2Plus = Vector3.Zero;
                    bool cacheHit = false;

                    if (EnableDiffuseCache)
                        cacheHit = _irradianceCache.TryLookup(secHitPoint, secHitNormal, out secBounce2Plus);

                    if (!cacheHit)
                    {
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
                            secBounce2Plus = secBaseXyz * tertIncoming;
                        }
                    }

                    if (EnableDiffuseCache)
                    {
                        _irradianceCache.Accumulate(secHitPoint, secHitNormal, secBounce2Plus);
                        diffuseCacheState[ix] = cacheHit ? (byte)1 : (byte)2;
                    }
                    else
                    {
                        diffuseCacheState[ix] = 0;
                    }

                    localBounce1 = baseXyz * secIncoming;
                    localBounce2plus = baseXyz * secBounce2Plus;

                    indirectLighting = localBounce1 + localBounce2plus;
                    bounce0 = localBounce0;
                    bounce1 = localBounce1;
                    bounce2plus = localBounce2plus;
                    xyz += indirectLighting;
                }
                else
                {
                    diffuseCacheState[ix] = 0;
                }
            }
            else
            {
                directLighting = baseXyz;
                xyz = baseXyz;
                bounce0 = baseXyz;
                bounce1 = Vector3.Zero;
                bounce2plus = Vector3.Zero;
                diffuseCacheState[ix] = 0;
            }
        }
        else
        {
            diffuseCacheState[ix] = 0;
        }

        if (hit != LastHit[ix])
        {
            _buffers.AccumXYZ[ix] = Vector3.Zero;
            SampleCount[ix] = 0;
            lumaM2[ix] = 0f;
            lumaVariance[ix] = 0f;
            lumaDirectM2[ix] = 0f;
            lumaIndirectM2[ix] = 0f;
            lumaDirectVariance[ix] = 0f;
            lumaIndirectVariance[ix] = 0f;
            clampAmount[ix] = 0f;
            depthDistance[ix] = 0f;
            albedoScalar[ix] = 0f;
            normalWorld[ix] = Vector3.Zero;
            directLightingXYZ[ix] = Vector3.Zero;
            indirectLightingXYZ[ix] = Vector3.Zero;
            emissiveLightingXYZ[ix] = Vector3.Zero;
            diffuseCacheState[ix] = 0;
            LastHit[ix] = hit;
        }

        if (hit)
        {
            hitPointWorld[ix] = hitPoint;
            depthDistance[ix] = Vector3.Distance(camera.Position, hitPoint);
            albedoScalar[ix] = Math.Clamp(reflectance, 0f, 1f);
            normalWorld[ix] = hitNormal;
        }

        var correctedXYZ = xyz * WavelengthLookup.DeterministicCorrection;
        var correctedDirect = bounce0 * WavelengthLookup.DeterministicCorrection;
        var correctedIndirect = indirectLighting * WavelengthLookup.DeterministicCorrection;
        var correctedBounce2Plus = bounce2plus * WavelengthLookup.DeterministicCorrection;
        var correctedEmissive = emissiveLighting * WavelengthLookup.DeterministicCorrection;

        if (SampleClamp > 0f)
        {
            Vector3 unclamped = correctedXYZ;
            correctedXYZ = Vector3.Clamp(correctedXYZ, Vector3.Zero, _sampleClampVec);
            float clampDelta = MathF.Abs(unclamped.X - correctedXYZ.X) +
                MathF.Abs(unclamped.Y - correctedXYZ.Y) +
                MathF.Abs(unclamped.Z - correctedXYZ.Z);
            if (clampDelta > 0f)
            {
                clampAmount[ix] += clampDelta;
                clampHitFrame[ix] = true;
                System.Threading.Interlocked.Increment(ref _clampEventCount);
            }
        }

        if (SampleCount[ix] < MaxSampleCount)
            SampleCount[ix] += 1;
        uint count = SampleCount[ix];
        _buffers.AccumXYZ[ix] += (correctedXYZ - _buffers.AccumXYZ[ix]) / count;
        directLightingXYZ[ix] += (correctedDirect - directLightingXYZ[ix]) / count;
        indirectLightingXYZ[ix] += (correctedIndirect - indirectLightingXYZ[ix]) / count;
        emissiveLightingXYZ[ix] += (correctedEmissive - emissiveLightingXYZ[ix]) / count;
        bounce0XYZ[ix] += (correctedDirect - bounce0XYZ[ix]) / count;
        bounce1XYZ[ix] += (correctedIndirect - bounce1XYZ[ix]) / count;
        bounce2PlusXYZ[ix] += (correctedBounce2Plus - bounce2PlusXYZ[ix]) / count;

        float ySample = correctedXYZ.Y;
        float yMean = _buffers.AccumXYZ[ix].Y;
        float delta = ySample - yMean;
        lumaM2[ix] += delta * (ySample - yMean);
        lumaVariance[ix] = count > 1 ? lumaM2[ix] / (count - 1) : 0f;

        float directY = correctedDirect.Y;
        float indirectY = correctedIndirect.Y;
        float totalContrib = directY + indirectY;
        float dFrac = totalContrib > 0f ? directY / totalContrib : 0f;
        float iFrac = totalContrib > 0f ? indirectY / totalContrib : 0f;

        float yDirectSample = ySample * dFrac;
        float directMean = directLightingXYZ[ix].Y;
        float dDelta = yDirectSample - directMean;
        lumaDirectM2[ix] += dDelta * (yDirectSample - directMean);
        lumaDirectVariance[ix] = count > 1 ? lumaDirectM2[ix] / (count - 1) : 0f;

        float yIndirectSample = ySample * iFrac;
        float indirectMean = indirectLightingXYZ[ix].Y;
        float iDelta = yIndirectSample - indirectMean;
        lumaIndirectM2[ix] += iDelta * (yIndirectSample - indirectMean);
        lumaIndirectVariance[ix] = count > 1 ? lumaIndirectM2[ix] / (count - 1) : 0f;

        lastUpdatedFrame[ix] = FrameIndex;
    }

    private sealed class PathTracer(JobSystem owner)
    {
        private readonly JobSystem _owner = owner;

        public void Trace(Camera camera, int y, int x)
        {
            _owner.TraceCore(camera, y, x);
        }
    }
}
