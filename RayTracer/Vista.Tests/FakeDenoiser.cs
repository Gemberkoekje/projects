using Vista.Output;

namespace Vista.Tests;

/// <summary>
/// A stand-in for Open Image Denoise: records what it is given and returns the colour it was given halved (so a test can tell
/// the denoised image from the raw one), or fails with <see cref="Failure"/>.
/// </summary>
internal sealed class FakeDenoiser : IDenoiser
{
    public const string Library = "Fake Denoise";

    /// <summary>When set, every call fails with this problem.</summary>
    public string Failure { get; init; } = string.Empty;

    /// <summary>Every input, in order.</summary>
    public List<DenoiseInput> Inputs { get; } = [];

    /// <summary>Every call's settings, in order.</summary>
    public List<DenoiseSettings> Settings { get; } = [];

    public DenoiseResult Denoise(DenoiseInput input, DenoiseSettings settings)
    {
        Inputs.Add(input);
        Settings.Add(settings);
        if (Failure.Length > 0)
            return DenoiseResult.Failed(Failure);
        float[] color = [.. input.Color.Select(c => 0.5f * c)];
        return DenoiseResult.Denoised(color, new DenoiserRun(Library, "1.0.0", settings.Device + ": fake", "halves the colour",
            float.IsFinite(input.InputScale) ? input.InputScale : null, 0.001, 0.002));
    }
}
