using System.Numerics;
using System.Runtime.CompilerServices;

namespace RayTracer;

public partial class JobSystem
{
    static uint Hash2D(int x, int y)
    {
        uint h = (uint)(x * 374761393 + y * 668265263);
        h = (h ^ (h >> 13)) * 1274126177u;
        return h ^ (h >> 16);
    }

    private static uint HashCell(int x, int y)
    {
        uint h = (uint)(x * 374761393 + y * 668265263);
        h ^= h >> 16;
        h *= 2246822519u;
        h ^= h >> 13;
        h *= 3266489917u;
        return h ^ (h >> 16);
    }

    private static bool IsSmokeBiome(int biomeX, int biomeY)
        => (biomeX + biomeY) % 3 == 1;

    private static bool IsFogBiome(int biomeX, int biomeY)
    {
        uint biomeHash = HashCell(biomeX, biomeY);
        return (biomeHash & 1u) == 0u;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float SmokeCoverage(float x, float z)
    {
        float bands = 0.5f + 0.5f * MathF.Sin(x * 0.37f + z * 0.53f);
        float swirl = 0.5f + 0.5f * MathF.Sin(x * 0.19f - z * 0.29f + bands * 3.1f);
        return 0.35f + 0.65f * (0.6f * bands + 0.4f * swirl);
    }

    // Non-turbulence density profiles shared by the Always* modes and the biome
    // sub-types, so a "fog" biome is exactly corridor fog and a "ground" biome is
    // exactly ground smoke — the same strength you get from AlwaysFog /
    // AlwaysGroundSmoke, which is what the ceiling indicator's amber/green promise.

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float FogProfile(Vector3 p, float coverage)
    {
        float midHeight = MathF.Exp(-MathF.Abs(p.Y - 1.0f) * 1.35f);
        float thickCoverage = 0.72f + 0.28f * coverage;
        float heightWeight = 0.70f + 0.30f * midHeight;
        return 0.50f * thickCoverage * heightWeight;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float GroundProfile(Vector3 p, float coverage)
    {
        float thickCoverage = 0.72f + 0.28f * coverage;
        float groundLayer = MathF.Exp(-MathF.Max(p.Y, 0f) * 2.35f);
        return 0.50f * thickCoverage * groundLayer;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float GetBiomeCellDensity(int biomeX, int biomeY, Vector3 p, float coverage)
    {
        if (!IsSmokeBiome(biomeX, biomeY))
            return 0f;

        return IsFogBiome(biomeX, biomeY)
            ? FogProfile(p, coverage)
            : GroundProfile(p, coverage);
    }

    private static float SmoothStep01(float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    // ── Smoke turbulence (texture-free 3D value-noise fBm) ─────────────
    // Gives the medium a wispy, clumpy 3D shape — dense billows separated by
    // thin gaps — instead of a uniform depth haze. Pure integer-hash math so it
    // ports verbatim to HLSL and stays deterministic across CPU/GPU.

    private static float Hash3ToUnit(int x, int y, int z)
    {
        uint h = (uint)(x * 374761393 + y * 668265263 + z * 1013904223);
        h ^= h >> 16;
        h *= 2246822519u;
        h ^= h >> 13;
        h *= 3266489917u;
        h ^= h >> 16;
        return h * (1f / 4294967296f);
    }

    // Trilinearly interpolated hashed lattice noise in [0,1], smoothstep-faded.
    private static float ValueNoise3D(Vector3 p)
    {
        float fx = MathF.Floor(p.X), fy = MathF.Floor(p.Y), fz = MathF.Floor(p.Z);
        int ix = (int)fx, iy = (int)fy, iz = (int)fz;
        float tx = p.X - fx, ty = p.Y - fy, tz = p.Z - fz;
        float ux = tx * tx * (3f - 2f * tx);
        float uy = ty * ty * (3f - 2f * ty);
        float uz = tz * tz * (3f - 2f * tz);

        float c000 = Hash3ToUnit(ix, iy, iz);
        float c100 = Hash3ToUnit(ix + 1, iy, iz);
        float c010 = Hash3ToUnit(ix, iy + 1, iz);
        float c110 = Hash3ToUnit(ix + 1, iy + 1, iz);
        float c001 = Hash3ToUnit(ix, iy, iz + 1);
        float c101 = Hash3ToUnit(ix + 1, iy, iz + 1);
        float c011 = Hash3ToUnit(ix, iy + 1, iz + 1);
        float c111 = Hash3ToUnit(ix + 1, iy + 1, iz + 1);

        float x00 = c000 + (c100 - c000) * ux;
        float x10 = c010 + (c110 - c010) * ux;
        float x01 = c001 + (c101 - c001) * ux;
        float x11 = c011 + (c111 - c011) * ux;
        float y0 = x00 + (x10 - x00) * uy;
        float y1 = x01 + (x11 - x01) * uy;
        return y0 + (y1 - y0) * uz;
    }

    // Fractal Brownian motion: three octaves of value noise at halving amplitude.
    private static float SmokeFbm(Vector3 p)
    {
        float sum = 0f;
        float amp = 0.5f;
        for (int o = 0; o < 3; o++)
        {
            sum += amp * ValueNoise3D(p);
            p *= 2.02f;
            amp *= 0.5f;
        }
        return sum; // ~[0, 0.875]
    }

    // Density multiplier in ~[0.3, 1.6]: a low baseline haze that swells into
    // dense clumps and thins toward gaps. Sampled anisotropically (stretched
    // horizontally, finer vertically) so the smoke billows rather than tiling.
    private static float SmokeTurbulence(Vector3 p)
    {
        // Low horizontal frequency => billows several world-units across (so
        // neighbouring rays integrate visibly different amounts, rather than the
        // detail averaging out into a smooth depth haze).
        var q = new Vector3(p.X * 0.22f + 11.3f, p.Y * 0.40f + 4.7f, p.Z * 0.22f + 19.1f);
        float f = SmokeFbm(q);
        float n = Math.Clamp((f - 0.33f) * 2.6f, 0f, 1f); // carve near-clear gaps
        n = n * n * (3f - 2f * n);                        // soft billow edges
        return 0.05f + 2.15f * n;                         // [0.05, 2.2]: gaps -> dense clumps
    }

    // Confines biome blending to a narrow band around the boundary (f = 0.5) so a
    // biome's interior is purely its own smoke type and only feathers into the
    // neighbour near the edge, instead of blending across the whole cell.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float BiomeEdgeBlend(float f)
    {
        const float halfBand = 0.2f;
        float t = Math.Clamp((f - (0.5f - halfBand)) / (2f * halfBand), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    private static float GetDensityBiome(Vector3 p)
    {
        const int biomeSizeCells = 4;
        float biomeWorldSize = MazeGeometryBuilder.CellSize * biomeSizeCells;

        // Centre the interpolation on biome centres (shift by half a cell) so a
        // biome's interior samples purely itself; the previous origin-aligned blend
        // made the centre of every biome a 50/50 mix with its +X/+Y neighbour,
        // which bled smoke a full half-biome outside where it belonged.
        float gx = p.X / biomeWorldSize - 0.5f;
        float gy = p.Z / biomeWorldSize - 0.5f;

        int bx0 = (int)MathF.Floor(gx);
        int by0 = (int)MathF.Floor(gy);
        int bx1 = bx0 + 1;
        int by1 = by0 + 1;

        float tx = BiomeEdgeBlend(gx - bx0);
        float ty = BiomeEdgeBlend(gy - by0);

        float coverage = SmokeCoverage(p.X, p.Z);
        float d00 = GetBiomeCellDensity(bx0, by0, p, coverage);
        float d10 = GetBiomeCellDensity(bx1, by0, p, coverage);
        float d01 = GetBiomeCellDensity(bx0, by1, p, coverage);
        float d11 = GetBiomeCellDensity(bx1, by1, p, coverage);

        float dx0 = d00 + (d10 - d00) * tx;
        float dx1 = d01 + (d11 - d01) * tx;
        // Biome banding sets where smoke lives (dry biomes stay at zero); the
        // turbulence sculpts wisps and clumps within the smoky regions.
        return (dx0 + (dx1 - dx0) * ty) * SmokeTurbulence(p);
    }

    private static float GetDensityFog(Vector3 p)
    {
        float coverage = SmokeCoverage(p.X, p.Z);
        return FogProfile(p, coverage) * SmokeTurbulence(p);
    }

    private static float GetDensityGround(Vector3 p)
    {
        float coverage = SmokeCoverage(p.X, p.Z);
        return GroundProfile(p, coverage) * SmokeTurbulence(p);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float GetDensity(Vector3 p, SmokeMode smokeMode)
        => smokeMode switch
        {
            SmokeMode.None => 0f,
            SmokeMode.Biome => GetDensityBiome(p),
            SmokeMode.AlwaysFog => GetDensityFog(p),
            SmokeMode.AlwaysGroundSmoke => GetDensityGround(p),
            _ => GetDensityBiome(p)
        };

    /// <summary>
    /// Number of evenly-spaced companion wavelengths (including the hero)
    /// evaluated per BVH hit. More companions reduce spectral noise at
    /// the cost of extra single-primitive Intersect calls per trace.
    /// </summary>
    private const int CompanionCount = 4;

    /// <summary>
    /// Maximum number of specular bounces followed through mirror and dielectric
    /// surfaces. Caps the cost (and terminates the infinite regress) of facing
    /// mirrors or nested glass (spectral-effects-plan.md §1.1–§1.2).
    /// </summary>
    private const int MaxSpecularBounces = 8;

    /// <summary>
    /// Follows a specular ray through zero or more mirror/dielectric interactions and
    /// returns the (CIE-weighted, uncorrected) XYZ radiance arriving back along it at
    /// <paramref name="wavelength"/>. The first diffuse (or bounce-capped) hit is shaded
    /// diffusely; a miss returns zero. Participating media is integrated along every
    /// segment, so smoke shows in reflections and refractions.
    /// <paramref name="inGlass"/> tracks whether the ray is inside a dielectric medium.
    /// </summary>
    private Vector3 TraceSpecularRadiance(Vector3 origin, Vector3 dir, float wavelength, ref uint rng, int depth, bool inGlass, float mediumSigma)
    {
        var ray = new Ray { Origin = origin, Direction = dir, Wavelength = wavelength, Intensity = 1f };
        var (reflectance, hitPoint, hitNormal, _, hit, _, surface, ior, extinction) = _bvh.FindClosest(ray);
        if (!hit)
            return Vector3.Zero;

        Vector3 radiance;
        if (Optics.IsSpecular(surface) && depth < MaxSpecularBounces)
        {
            radiance = SpecularRadiance(surface, reflectance, ior, extinction, hitPoint, hitNormal, dir, wavelength, ref rng, depth, inGlass, mediumSigma);
        }
        else
        {
            float scalar = ShadeDiffuseScalar(reflectance, hitPoint, hitNormal, wavelength, ref rng);
            radiance = WavelengthLookup.TryGet((int)wavelength, out Vector3 cie) ? cie * scalar : Vector3.Zero;
        }

        // Beer–Lambert absorption for the segment just travelled inside a coloured medium
        // (§2.3): the radiance arriving back along it is attenuated by exp(−σ(λ)·distance).
        if (mediumSigma > 0f)
            radiance *= MathF.Exp(-mediumSigma * Vector3.Distance(origin, hitPoint));

        // Smoke/fog along this specular segment (identity when volumetrics are off, so the
        // no-smoke path stays bit-identical to the pre-media reflection).
        return IntegrateVolumetricSegment(origin, hitPoint, dir).Apply(radiance);
    }

    /// <summary>
    /// One specular interaction at a hit, returning the XYZ radiance arriving back along
    /// <paramref name="incidentDir"/>. A mirror reflects and multiplies in its spectral
    /// reflectance. A dielectric computes the Fresnel reflectance and stochastically
    /// reflects or refracts (Russian roulette on Fresnel) via <see cref="Optics"/>,
    /// toggling <paramref name="inGlass"/> on transmission (spectral-effects-plan.md §1.2).
    /// Then it recurses through <see cref="TraceSpecularRadiance"/>.
    /// </summary>
    private Vector3 SpecularRadiance(
        SurfaceKind surface, float reflectance, float ior, float hitExtinction,
        Vector3 hitPoint, Vector3 hitNormal, Vector3 incidentDir,
        float wavelength, ref uint rng, int depth, bool inGlass, float mediumSigma)
    {
        if (surface == SurfaceKind.Mirror)
        {
            Vector3 reflectDir = Vector3.Reflect(incidentDir, hitNormal);
            Vector3 reflectOrigin = hitPoint + hitNormal * 1e-3f;
            return reflectance * TraceSpecularRadiance(reflectOrigin, reflectDir, wavelength, ref rng, depth + 1, inGlass, mediumSigma);
        }

        if (surface == SurfaceKind.Bubble)
        {
            // Thin-shell bubble (§2.6): the film reflectance is the reflect probability (colour is
            // which wavelengths reflect most; `reflectance` carries base × film with the gravity
            // gradient, folded in FromMaterial), boosted so the rings read across the whole bubble;
            // the rest transmits straight through (mostly see-through, no lens warp).
            float cosB = MathF.Abs(Vector3.Dot(Vector3.Normalize(incidentDir), hitNormal));
            float rReflect = Optics.BubbleReflectProbability(cosB, ior, reflectance);
            rng = rng * 747796405u + 2891336453u;
            if (rng / 4294967296f < rReflect)
            {
                Vector3 reflectDir = Vector3.Reflect(incidentDir, hitNormal);
                Vector3 reflectOrigin = hitPoint + hitNormal * 1e-3f;
                return TraceSpecularRadiance(reflectOrigin, reflectDir, wavelength, ref rng, depth + 1, false, 0f);
            }
            Vector3 throughOrigin = hitPoint + incidentDir * 1e-3f; // straight through the thin shell
            return TraceSpecularRadiance(throughOrigin, incidentDir, wavelength, ref rng, depth + 1, false, 0f);
        }

        // Dielectric glass: reflect vs. refract by Russian roulette on Fresnel.
        float iorFrom = inGlass ? ior : 1f;
        float iorTo = inGlass ? 1f : ior;
        float cosI = MathF.Abs(Vector3.Dot(Vector3.Normalize(incidentDir), hitNormal));
        float fresnel = Optics.FresnelDielectric(cosI, iorFrom, iorTo);
        bool transmits = Optics.Refract(incidentDir, hitNormal, iorFrom, iorTo, out Vector3 refractDir);

        rng = rng * 747796405u + 2891336453u;
        float u = rng / 4294967296f;
        if (!transmits || u < fresnel)
        {
            Vector3 reflectDir = Vector3.Reflect(incidentDir, hitNormal);
            Vector3 reflectOrigin = hitPoint + hitNormal * 1e-3f;
            // Reflection stays in the current medium → its absorption is unchanged.
            return TraceSpecularRadiance(reflectOrigin, reflectDir, wavelength, ref rng, depth + 1, inGlass, mediumSigma);
        }

        Vector3 refractOrigin = hitPoint - hitNormal * 1e-3f;
        bool newInGlass = !inGlass;
        // Entering this dielectric → travel through its absorption (§2.3); exiting → clear air.
        float newMediumSigma = newInGlass ? hitExtinction : 0f;
        return TraceSpecularRadiance(refractOrigin, refractDir, wavelength, ref rng, depth + 1, newInGlass, newMediumSigma);
    }

    /// <summary>
    /// Scalar (CIE-weight-free) diffuse radiance leaving <paramref name="point"/>
    /// toward the viewer: <c>reflectance × (ambient + direct)</c>, matching the
    /// primary path's ambient/direct model so a surface seen in a mirror shades
    /// like the same surface seen directly.
    /// </summary>
    private float ShadeDiffuseScalar(float reflectance, Vector3 point, Vector3 normal, float wavelength, ref uint rng)
    {
        float ambient = 1f;
        float direct = 0f;
        if (Lighting != LightingMode.None && _lights.Length > 0)
        {
            ambient = AmbientLevel;
            direct = DirectTermAt(point, normal, wavelength, ref rng);
        }

        return reflectance * (ambient + direct);
    }

    /// <summary>
    /// Shadow-ray visibility in [0,1] from <paramref name="origin"/> toward a light
    /// <paramref name="lightDist"/> away along <paramref name="lightDir"/>: the geometry
    /// transmittance (opaque → 0; bubbles and glass attenuate rather than fully block, via
    /// <see cref="BVH.Transmittance"/>) times, when <paramref name="includeFog"/> is set, the
    /// participating-medium transmittance along the same segment (<see cref="SegmentTransmittance(Vector3,Vector3,Vector3)"/>).
    /// This replaces the former binary occlusion so bubbles and glass cast soft, tinted shadows
    /// and moving fog casts shadows onto lit surfaces (shadows-and-caustics-plan §A). For an
    /// opaque, fog-free shadow ray it returns exactly 0 or 1, so such scenes shade bit-for-bit
    /// as before. <paramref name="wavelength"/> is the hero wavelength the shadow ray carries, so
    /// a coloured glass or thin-film tint enters the shadow per wavelength as accumulation builds
    /// the spectrum.
    /// </summary>
    private float ShadowVisibility(Vector3 origin, Vector3 lightDir, float lightDist, float wavelength, bool includeFog)
    {
        var shadowRay = new Ray
        {
            Origin = origin,
            Direction = lightDir,
            Wavelength = wavelength,
            Intensity = 1f,
        };

        float vis = _bvh.Transmittance(shadowRay, lightDist);
        if (vis <= 0f)
            return 0f;

        if (includeFog)
            vis *= SegmentTransmittance(origin, origin + lightDir * lightDist, lightDir);

        return vis;
    }

    /// <summary>
    /// Next-event-estimation direct-lighting term at a surface point, using the
    /// same 1/d²·cos importance selection and shadow-ray occlusion as the primary
    /// path. Returns the unshadowed contribution for <see cref="LightingMode.Direct"/>.
    /// </summary>
    private float DirectTermAt(Vector3 point, Vector3 normal, float wavelength, ref uint rng)
    {
        int nLights = _lights.Length;
        if (nLights == 0)
            return 0f;

        Span<float> weights = stackalloc float[nLights];
        float totalW = 0f;
        for (int li = 0; li < nLights; li++)
        {
            Vector3 toLight = _lights[li].Position - point;
            float dsq = Vector3.Dot(toLight, toLight);
            float dist = MathF.Sqrt(dsq);
            Vector3 ldir = toLight / dist;
            float cos = Vector3.Dot(normal, ldir);
            float w = cos > 0f ? 1f / MathF.Max(dsq, 1e-6f) : 0f;
            weights[li] = w;
            totalW += w;
        }

        if (totalW <= 0f)
            return 0f;

        rng = rng * 747796405u + 2891336453u;
        float u = rng / 4294967296f * totalW;
        float acc = 0f;
        int chosen = nLights - 1;
        for (int li = 0; li < nLights; li++)
        {
            acc += weights[li];
            if (u <= acc)
            {
                chosen = li;
                break;
            }
        }

        Vector3 toSelected = _lights[chosen].Position - point;
        float distSq = MathF.Max(Vector3.Dot(toSelected, toSelected), 1e-6f);
        float d = MathF.Sqrt(distSq);
        Vector3 lightDir = toSelected / d;
        float lightCos = Vector3.Dot(normal, lightDir);
        if (lightCos <= 0f)
            return 0f;

        float lightP = weights[chosen] / totalW;

        float vis = 1f;
        if (Lighting == LightingMode.NEE)
        {
            vis = ShadowVisibility(point + normal * 1e-3f, lightDir, d - 2e-3f, wavelength, includeFog: true);
            if (vis <= 0f)
                return 0f;
        }

        return vis * (lightCos / distSq * LightIntensity / MathF.Max(lightP, 1e-9f));
    }

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
        var (reflectance, hitPoint, hitNormal, _, hit, hitPrimitive, heroSurface, heroIor, heroExtinction) = _bvh.FindClosest(ray);
        Vector3 xyz = Vector3.Zero;
        Vector3 directLighting = Vector3.Zero;
        Vector3 indirectLighting = Vector3.Zero;
        Vector3 emissiveLighting = Vector3.Zero;
        Vector3 bounce0 = Vector3.Zero;
        Vector3 bounce1 = Vector3.Zero;
        Vector3 bounce2plus = Vector3.Zero;
        if (hit && Optics.IsSpecular(heroSurface))
        {
            // Specular surface — mirror reflection (§1.1) or dielectric reflect/refract
            // (§1.2). The interaction is achromatic in *direction* for clear glass, so
            // companion wavelengths would share this geometry; we sample the hero
            // wavelength and let accumulation build the spectrum. A mirror's spectral
            // reflectance curve colours the reflection (gold warm, silver neutral);
            // glass splits reflect vs. refract by Fresnel.
            uint specularRng = pixelHash + (uint)(sampleIdx * 2654435761u) + 1013904223u;
            // SpecularRadiance returns CIE-weighted XYZ (with smoke integrated along the
            // reflected/refracted segments); the camera→surface segment's fog is applied
            // by the shared volumetric step below.
            xyz = SpecularRadiance(
                heroSurface, reflectance, heroIor, heroExtinction, hitPoint, hitNormal, dir,
                heroWavelength, ref specularRng, 0, inGlass: false, mediumSigma: 0f);

            // The specular radiance behaves like a direct view of the reflected/refracted
            // surface; book it as bounce-0/direct for the debug decomposition.
            directLighting = xyz;
            bounce0 = xyz;
            diffuseCacheState[ix] = 0;
        }
        else if (hit)
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
                    float vis = 1f;
                    if (Lighting == LightingMode.NEE)
                        vis = ShadowVisibility(
                            hitPoint + hitNormal * 1e-3f, lightDir,
                            MathF.Sqrt(lightDistSq) - 2e-3f, ray.Wavelength, includeFog: true);

                    if (vis > 0f)
                        directTerm += vis * (lightCos / lightDistSq * LightIntensity / Math.Max(lightP, 1e-9f));
                }
            }

            int deterCount = WavelengthLookup.DeterministicCount;
            int heroIdx = (int)((pixelHash + sampleIdx) % deterCount);
            int stride = deterCount / CompanionCount;

            if (WavelengthLookup.TryGet(heroWavelength, out var heroXyz))
                xyz = heroXyz * reflectance;

            // Companion wavelengths reuse the hero ray's geometry and only
            // re-look-up reflectance. That reuse is valid only where the path is
            // wavelength-independent (diffuse/mirror). On a dispersive dielectric,
            // thin-film, grating, or fluorescent surface each wavelength takes a
            // different path or shifts colour, so we fall back to hero-only
            // sampling and let accumulation build the spectrum over frames
            // (spectral-effects-plan.md §0.3).
            bool wavelengthDependentPath = Optics.IsWavelengthDependent(heroSurface);

            int evaluated = 1;
            if (!wavelengthDependentPath)
            {
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
                            xyz += compXyz * compHit.Value.Reflectance;
                            evaluated++;
                        }
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

                var (secReflectance, secHitPoint, secHitNormal, _, secHit, secPrimitive, _, _, _) = _bvh.FindClosest(secRay);
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
                            float vis2 = ShadowVisibility(
                                secHitPoint + secHitNormal * 1e-3f, lightDir2,
                                dist2 - 2e-3f, secRay.Wavelength, includeFog: false);
                            if (vis2 > 0f)
                                secDirectTerm += vis2 * (cosTheta2 / distSq2 * LightIntensity * _lights.Length);
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

                        var (tertReflectance, tertHitPoint, tertHitNormal, _, tertHit, tertPrimitive, _, _, _) = _bvh.FindClosest(tertRay);
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
                                    float vis3 = ShadowVisibility(
                                        tertHitPoint + tertHitNormal * 1e-3f, lightDir3,
                                        dist3 - 2e-3f, tertRay.Wavelength, includeFog: false);
                                    if (vis3 > 0f)
                                        tertDirectTerm += vis3 * (cosTheta3 / distSq3 * LightIntensity * _lights.Length);
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

            // Caustics (shadows-and-caustics-plan §B): add the forward photon map's density estimate
            // at diffuse receivers — the light→specular→diffuse paths NEE cannot connect. The XYZ
            // estimate is scaled by the receiver's albedo/π (its diffuse response) and the tunable
            // strength, then folded into the accumulated radiance and the indirect decomposition. It is
            // added before the volumetric step, so fog attenuates it, and before the deterministic
            // correction, so it is normalised spectrally like the direct term. Skipped on specular
            // hero hits (caustics land on diffuse surfaces) and whenever the map is absent.
            if (_causticMap is not null && !Optics.IsSpecular(heroSurface))
            {
                Vector3 caustic = _causticMap.EstimateXyz(hitPoint, hitNormal, Caustics.GatherRadius, WavelengthLookup)
                    * (reflectance * Caustics.Strength * (1f / MathF.PI));
                xyz += caustic;
                indirectLighting += caustic;
                bounce2plus += caustic;
            }

            VolumetricSample volume = IntegrateVolumetricSegment(camera.Position, hitPoint, dir);
            xyz = volume.Apply(xyz);
            directLighting *= volume.Transmittance;
            indirectLighting *= volume.Transmittance;
            bounce0 *= volume.Transmittance;
            bounce1 *= volume.Transmittance;
            bounce2plus *= volume.Transmittance;
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
