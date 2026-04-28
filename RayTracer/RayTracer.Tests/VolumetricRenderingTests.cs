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
    public void IntegrateVolumetricSegment_AttenuationDecreasesWithDistance()
    {
        VolumetricOptions options = VolumetricOptions.FromQuality(VolumetricQuality.High, SmokeMode.AlwaysFog) with
        {
            InscatterStrength = 0f,
        };

        VolumetricSample near = JobSystem.IntegrateVolumetricSegment(
            Vector3.Zero,
            new Vector3(0f, 1f, 2f),
            Vector3.UnitZ,
            options);
        VolumetricSample far = JobSystem.IntegrateVolumetricSegment(
            Vector3.Zero,
            new Vector3(0f, 1f, 8f),
            Vector3.UnitZ,
            options);

        Assert.IsTrue(far.Transmittance < near.Transmittance);
    }

    [TestMethod]
    public void IntegrateVolumetricSegment_HighDensityLongPath_StronglyAttenuates()
    {
        VolumetricOptions options = VolumetricOptions.FromQuality(VolumetricQuality.Ultra, SmokeMode.AlwaysFog) with
        {
            SigmaScaleFog = 3f,
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
