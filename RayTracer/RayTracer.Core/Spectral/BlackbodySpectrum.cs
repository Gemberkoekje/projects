namespace RayTracer;

/// <summary>
/// Planckian (blackbody) emission spectrum for light sources (outdoor-area-plan.md §O9, spectral §2.4).
/// Gives a light a physically-grounded colour from a temperature: a ~1800 K torch is warm/orange, a
/// ~5500 K sun is daylight-neutral, so the indoor-warm / outdoor-cool contrast is emergent from the Planck
/// curve × the CIE integration rather than a hand-picked RGB tint.
///
/// <para><see cref="Relative"/> returns a per-wavelength <b>multiplier</b> on a light's contribution,
/// normalised to 1 at 550 nm (the same convention as the sky's solar shape), so it changes the light's
/// <i>colour</i> without redefining its intensity unit. A temperature of 0 (or less) means "no blackbody" —
/// a flat white light — so an un-set light is byte-identical to the pre-§O9 behaviour.</para>
/// </summary>
public static class BlackbodySpectrum
{
    /// <summary>Second radiation constant <c>hc/k</c> in nm·K.</summary>
    private const float C2NmK = 1.438777e7f;

    /// <summary>Reference wavelength (nm) the spectrum is normalised to 1 at.</summary>
    private const float ReferenceNm = 550f;

    /// <summary>
    /// Relative spectral radiance at <paramref name="wavelengthNm"/> for a blackbody at
    /// <paramref name="tempK"/>, normalised to 1 at 550 nm. <paramref name="tempK"/> ≤ 0 → 1 (flat white).
    /// </summary>
    public static float Relative(float wavelengthNm, float tempK)
    {
        if (tempK <= 0f) return 1f;
        float r = ReferenceNm / wavelengthNm;
        float denom = MathF.Exp(C2NmK / (wavelengthNm * tempK)) - 1f;
        float denomRef = MathF.Exp(C2NmK / (ReferenceNm * tempK)) - 1f;
        return r * r * r * r * r * (denomRef / denom); // (550/λ)^5 · (denomRef/denom) — Planck ratio, 550-normalised
    }
}
