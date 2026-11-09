using Microsoft.Extensions.Configuration;

namespace DatumPrikker.WebApp.Auth;

/// <summary>An external sign-in provider.</summary>
public sealed record LoginProvider
{
    /// <summary>The authentication scheme, which is also the configuration section name.</summary>
    public string Scheme { get; init; } = string.Empty;

    /// <summary>What the button says.</summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>The prefix of the account ids it hands out, as in <c>google:1234</c>.</summary>
    public string Prefix { get; init; } = string.Empty;
}

/// <summary>Which providers are offered: only those with both a client id and a client secret (D5).</summary>
public static class LoginProviders
{
    /// <summary>Sign in with Google.</summary>
    public static readonly LoginProvider Google = new LoginProvider { Scheme = "Google", Label = "Google", Prefix = "google" };

    /// <summary>Sign in with a Microsoft account.</summary>
    public static readonly LoginProvider Microsoft = new LoginProvider { Scheme = "Microsoft", Label = "Microsoft", Prefix = "microsoft" };

    /// <summary>Every provider this app knows, configured or not.</summary>
    public static readonly IReadOnlyList<LoginProvider> All = [Google, Microsoft];

    /// <summary>The providers whose <c>Authentication:{Scheme}:ClientId</c> and <c>ClientSecret</c> are both set.</summary>
    /// <param name="configuration">The app's configuration.</param>
    /// <returns>The configured providers, Google first.</returns>
    public static IReadOnlyList<LoginProvider> Configured(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return [.. All.Where(provider =>
            !string.IsNullOrWhiteSpace(configuration[$"Authentication:{provider.Scheme}:ClientId"])
            && !string.IsNullOrWhiteSpace(configuration[$"Authentication:{provider.Scheme}:ClientSecret"]))];
    }
}
