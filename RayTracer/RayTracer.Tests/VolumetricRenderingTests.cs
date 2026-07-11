using System.Numerics;
using RayTracer;

namespace RayTracer.Tests;

[TestClass]
public class VolumetricRenderingTests
{
    [TestMethod]
    public void IntegrateVolumetricSegment_SmokeModeNone_BypassesExactly()
    {
        VolumetricOptions options = VolumetricOptions.FromQuality(VolumetricQuality.High, SmokeMode.None);

        VolumetricSample sample = JobSystem.IntegrateVolumetricSegment(
            Vector3.Zero,
            new Vector3(0f, 1f, 8f),
            Vector3.UnitZ,
            options);

        Assert.AreEqual(1f, sample.Transmittance);
        Assert.AreEqual(Vector3.Zero, sample.Inscatter);
    }

    [TestMethod]
    public void IntegrateVolumetricSegment_ZeroDensity_ReturnsUnchangedRadiance()
    {
        VolumetricOptions options = VolumetricOptions.FromQuality(VolumetricQuality.Medium, SmokeMode.AlwaysGroundSmoke);

        VolumetricSample sample = JobSystem.IntegrateVolumetricSegment(
            new Vector3(0f, 12f, 0f),
            new Vector3(0f, 12f, 4f),
            Vector3.UnitZ,
            options);

        Assert.IsTrue(sample.Transmittance > 0.999f);
        Assert.IsTrue(sample.Inscatter.Length() < 0.001f);
    }

    [TestMethod]
    public void IntegrateVolumetricSegment_DenserMediumAttenuatesMore()
    {
        // Along the *same* segment, scaling the extinction coefficient up must
        // transmit no more light: every step's optical depth scales, so the
        // product of step transmittances can only fall. This holds for any
        // density field — including the spatially non-uniform (turbulent) smoke —
        // whereas comparing two rays of different length/direction does not, since
        // a longer or differently-aimed ray can legitimately pass through sparser
        // gaps between billows.
        var origin = new Vector3(0f, 1f, 0f);
        var hit = new Vector3(0f, 1f, 8f);
        VolumetricOptions thin = VolumetricOptions.FromQuality(VolumetricQuality.High, SmokeMode.AlwaysFog) with
        {
            InscatterStrength = 0f,
            SigmaScaleFog = 1f,
        };
        VolumetricOptions thick = thin with { SigmaScaleFog = 3f };

        VolumetricSample thinSample = JobSystem.IntegrateVolumetricSegment(origin, hit, Vector3.UnitZ, thin);
        VolumetricSample thickSample = JobSystem.IntegrateVolumetricSegment(origin, hit, Vector3.UnitZ, thick);

        Assert.IsTrue(thickSample.Transmittance < thinSample.Transmittance,
            $"denser medium should attenuate more (thin={thinSample.Transmittance}, thick={thickSample.Transmittance})");
    }

    [TestMethod]
    public void IntegrateVolumetricSegment_HighDensityLongPath_StronglyAttenuates()
    {
        // Large sigma so the path attenuates strongly even where the turbulent
        // density thins to its floor (a gap between billows) along this ray.
        VolumetricOptions options = VolumetricOptions.FromQuality(VolumetricQuality.Ultra, SmokeMode.AlwaysFog) with
        {
            SigmaScaleFog = 20f,
            InscatterStrength = 0f,
        };

        VolumetricSample sample = JobSystem.IntegrateVolumetricSegment(
            Vector3.Zero,
            new Vector3(0f, 1f, 12f),
            Vector3.UnitZ,
            options);

        Assert.IsTrue(sample.Transmittance < 0.05f);
    }

    [TestMethod]
    public void RenderPreset_MapsExpectedVolumetricStepCounts()
    {
        Assert.AreEqual(0, RenderPreset.Low.Volumetrics.MarchSteps);
        Assert.AreEqual(3, RenderPreset.Playable.Volumetrics.MarchSteps);
        Assert.AreEqual(8, RenderPreset.Medium.Volumetrics.MarchSteps);
        Assert.AreEqual(16, RenderPreset.High.Volumetrics.MarchSteps);
        Assert.AreEqual(24, RenderPreset.Ultra.Volumetrics.MarchSteps);
        Assert.AreEqual(0, RenderPreset.Playable.Volumetrics.ShadowStepInterval);
        Assert.AreEqual(SmokeMode.AlwaysFog, RenderPreset.FogDebug.Volumetrics.SmokeMode);
        Assert.AreEqual(SmokeMode.AlwaysGroundSmoke, RenderPreset.GroundSmokeDebug.Volumetrics.SmokeMode);
    }
}
