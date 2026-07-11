using System.Numerics;

namespace RayTracer;

/// <summary>
/// Forward light transport for caustics (shadows-and-caustics-plan §B): shoots photons from the
/// lights toward the caustic casters, bends them through the specular surfaces (dielectric
/// dispersion, bubble thin-shell, mirror), and deposits the ones that land on a diffuse receiver
/// into a <see cref="PhotonMap"/>. This is the transport a backward path tracer with point-light
/// NEE cannot do, and it is what puts a prism's rainbow or a glass sphere's bright focus onto the
/// floor. The refraction/Fresnel/absorption maths are the same <see cref="Optics"/> helpers the
/// eye-side specular chain uses — only the direction of travel differs.
/// </summary>
public static class PhotonTracer
{
    /// <summary>
    /// Emits <paramref name="photonsPerLight"/> photons from each light toward random points in
    /// <paramref name="targetBounds"/> (the casters' region — importance-directing emission there
    /// instead of into the whole sphere, since caustics come only from the casters), traces each
    /// forward through up to <paramref name="maxBounces"/> specular interactions, and returns the
    /// caustic photons deposited on the first diffuse surface reached after ≥1 specular bounce.
    /// </summary>
    /// <param name="lights">Emitters. Each photon starts at a light and carries power
    /// <c>1/photonsPerLight</c>, so a light's total emitted power toward the casters is ~1 (the
    /// absolute scale is tuned when the map is composited into the render).</param>
    /// <param name="bvh">Scene acceleration structure the photons are traced against.</param>
    /// <param name="targetBounds">World-space box the photons are aimed at (the casters' bounds).</param>
    /// <param name="wavelengths">Provides the deterministic wavelength set each photon samples.</param>
    /// <param name="photonsPerLight">Photons emitted per light.</param>
    /// <param name="gatherRadius">Density-estimate radius the returned map is sized for.</param>
    /// <param name="maxBounces">Cap on specular interactions before a photon is abandoned.</param>
    /// <param name="seed">Deterministic RNG seed (reproducible maps for tests and accumulation).</param>
    /// <param name="wavelengthOverride">When &gt; 0, forces every photon to this wavelength instead
    /// of cycling the deterministic set (used to probe a single wavelength, e.g. dispersion tests).</param>
    public static PhotonMap Emit(
        Light[] lights,
        BVH bvh,
        AABB targetBounds,
        WavelengthLookup wavelengths,
        int photonsPerLight,
        float gatherRadius,
        int maxBounces = 8,
        uint seed = 1u,
        int wavelengthOverride = 0)
    {
        System.ArgumentNullException.ThrowIfNull(lights);
        System.ArgumentNullException.ThrowIfNull(bvh);
        System.ArgumentNullException.ThrowIfNull(wavelengths);

        var map = new PhotonMap(gatherRadius);
        if (lights.Length == 0 || photonsPerLight <= 0)
            return map;

        Vector3 boundsMin = targetBounds.Min;
        Vector3 boundsSize = targetBounds.Max - targetBounds.Min;
        float power = 1f / photonsPerLight;
        uint rng = seed == 0u ? 1u : seed;

        for (int li = 0; li < lights.Length; li++)
        {
            Vector3 lightPos = lights[li].Position;
            for (int p = 0; p < photonsPerLight; p++)
            {
                rng = rng * 747796405u + 2891336453u;
                float rx = rng / 4294967296f;
                rng = rng * 747796405u + 2891336453u;
                float ry = rng / 4294967296f;
                rng = rng * 747796405u + 2891336453u;
                float rz = rng / 4294967296f;

                Vector3 target = boundsMin + new Vector3(boundsSize.X * rx, boundsSize.Y * ry, boundsSize.Z * rz);
                Vector3 dir = target - lightPos;
                float len = dir.Length();
                if (len < 1e-6f)
                    continue;
                dir /= len;

                int wavelength = wavelengthOverride > 0
                    ? wavelengthOverride
                    : wavelengths.GetDeterministicWavelength(p);

                TracePhoton(bvh, lightPos, dir, power, wavelength, maxBounces, ref rng, map);
            }
        }

        return map;
    }

    /// <summary>
    /// Traces one photon forward: specular surfaces reflect/refract it (dielectric with dispersion
    /// and Beer–Lambert absorption, bubble thin-shell split, mirror reflectance); the first diffuse
    /// hit after at least one specular bounce deposits a caustic photon. Fresnel and the bubble
    /// split use Russian roulette, so the photon keeps its full power along whichever branch it
    /// takes (unbiased in expectation).
    /// </summary>
    private static void TracePhoton(
        BVH bvh, Vector3 origin, Vector3 dir, float power, int wavelength, int maxBounces, ref uint rng, PhotonMap map)
    {
        bool inGlass = false;
        float mediumSigma = 0f;
        int specularBounces = 0;

        for (int depth = 0; depth < maxBounces; depth++)
        {
            var ray = new Ray { Origin = origin, Direction = dir, Wavelength = wavelength, Intensity = 1f };
            var (reflectance, hitPoint, hitNormal, _, hit, _, surface, ior, extinction) = bvh.FindClosest(ray);
            if (!hit)
                return; // escaped the scene

            // Beer–Lambert absorption for the segment just travelled inside a coloured medium.
            if (mediumSigma > 0f)
                power *= MathF.Exp(-mediumSigma * Vector3.Distance(origin, hitPoint));

            if (surface == SurfaceKind.Mirror)
            {
                power *= reflectance;
                dir = Vector3.Reflect(dir, hitNormal);
                origin = hitPoint + hitNormal * 1e-3f;
                specularBounces++;
                continue;
            }

            if (surface == SurfaceKind.Bubble)
            {
                float cos = MathF.Abs(Vector3.Dot(Vector3.Normalize(dir), hitNormal));
                float reflectProb = Optics.BubbleReflectProbability(cos, ior, reflectance);
                rng = rng * 747796405u + 2891336453u;
                if (rng / 4294967296f < reflectProb)
                {
                    dir = Vector3.Reflect(dir, hitNormal);
                    origin = hitPoint + hitNormal * 1e-3f;
                }
                else
                {
                    origin = hitPoint + dir * 1e-3f; // transmit straight through the thin shell
                }

                specularBounces++;
                continue;
            }

            if (surface == SurfaceKind.Dielectric)
            {
                float iorFrom = inGlass ? ior : 1f;
                float iorTo = inGlass ? 1f : ior;
                float cos = MathF.Abs(Vector3.Dot(Vector3.Normalize(dir), hitNormal));
                float fresnel = Optics.FresnelDielectric(cos, iorFrom, iorTo);
                bool transmits = Optics.Refract(dir, hitNormal, iorFrom, iorTo, out Vector3 refracted);

                rng = rng * 747796405u + 2891336453u;
                float u = rng / 4294967296f;
                if (!transmits || u < fresnel)
                {
                    dir = Vector3.Reflect(dir, hitNormal);
                    origin = hitPoint + hitNormal * 1e-3f;
                }
                else
                {
                    dir = refracted;
                    origin = hitPoint - hitNormal * 1e-3f;
                    inGlass = !inGlass;
                    mediumSigma = inGlass ? extinction : 0f;
                }

                specularBounces++;
                continue;
            }

            // Diffuse (or any other opaque) receiver: deposit only if the photon has been bent by a
            // specular surface — otherwise this is ordinary direct light that NEE already accounts
            // for, and storing it would double-count.
            if (specularBounces > 0)
                map.Store(new Photon(hitPoint, hitNormal, power, wavelength));

            return;
        }
    }
}
