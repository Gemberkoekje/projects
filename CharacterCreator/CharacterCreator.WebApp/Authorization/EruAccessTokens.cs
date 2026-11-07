using System.Globalization;
using System.Net.Http;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;
using Qowaiv;
using Qowaiv.Validation.Abstractions;

namespace CharacterCreator.WebApp.Authorization;

/// <summary>
/// The signed-in person's ERU access token, for calling VOID on their behalf (DESIGN.md §6). Keycloak
/// hands out access tokens that live minutes, so an expired one is refreshed with the stored refresh
/// token, and the sign-in cookie is updated with the new pair.
/// </summary>
/// <remarks>Scoped: one per request.</remarks>
/// <param name="oidc">The OpenID Connect options, for Keycloak's token endpoint and the client id.</param>
/// <param name="http">The HTTP client factory, for the refresh call.</param>
/// <param name="logger">The log.</param>
internal sealed class EruAccessTokens(IOptionsMonitor<OpenIdConnectOptions> oidc, IHttpClientFactory http, ILogger<EruAccessTokens> logger)
{
    /// <summary>The named HTTP client the refresh goes through.</summary>
    public const string HttpClientName = "keycloak";

    private static readonly TimeSpan Margin = TimeSpan.FromSeconds(30);

    private readonly IOptionsMonitor<OpenIdConnectOptions> oidc = oidc ?? throw new ArgumentNullException(nameof(oidc));
    private readonly IHttpClientFactory http = http ?? throw new ArgumentNullException(nameof(http));
    private readonly ILogger<EruAccessTokens> logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>A valid access token for the person signed in on this request, refreshed if need be.</summary>
    /// <param name="context">The request.</param>
    /// <returns>The token, or why there is none (not signed in through ERU, or the refresh failed).</returns>
    public Task<Result<string>> ForAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return TokenAsync(context);
    }

    /// <summary>Whether an <c>expires_at</c> value, as the OIDC handler stores it, is past or nearly so.</summary>
    /// <param name="expiresAt">The stored value; empty when unknown.</param>
    /// <returns>Whether to refresh first.</returns>
    internal static bool Expired(string expiresAt)
        => !DateTimeOffset.TryParse(expiresAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at)
            || at - Margin <= new DateTimeOffset(Clock.UtcNow(), TimeSpan.Zero);

    private async Task<Result<string>> TokenAsync(HttpContext context)
    {
        var signIn = await context.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        if (!signIn.Succeeded || signIn.Principal is not { } principal || signIn.Properties is not { } properties)
        {
            return Result.WithMessages<string>(ValidationMessage.Error("Not signed in through ERU."));
        }

        var access = properties.GetTokenValue("access_token") ?? string.Empty;
        if (access.Length > 0 && !Expired(properties.GetTokenValue("expires_at") ?? string.Empty))
        {
            return Result.For(access);
        }

        var refresh = properties.GetTokenValue("refresh_token") ?? string.Empty;
        if (refresh.Length == 0)
        {
            return Result.WithMessages<string>(ValidationMessage.Error("The ERU sign-in has expired; sign in again."));
        }

        return await RefreshAsync(context, principal, properties, refresh);
    }

    private async Task<Result<string>> RefreshAsync(HttpContext context, ClaimsPrincipal principal, AuthenticationProperties properties, string refreshToken)
    {
        var options = oidc.Get(EruSignIn.Scheme);
        if (options.ConfigurationManager is null)
        {
            return Result.WithMessages<string>(ValidationMessage.Error("ERU sign-in is not configured here."));
        }

        try
        {
            var configuration = await options.ConfigurationManager.GetConfigurationAsync(context.RequestAborted);
            var form = new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken,
                ["client_id"] = options.ClientId ?? string.Empty,
            };
            if (!string.IsNullOrEmpty(options.ClientSecret))
            {
                form["client_secret"] = options.ClientSecret;
            }

            using var client = http.CreateClient(HttpClientName);
            using var body = new FormUrlEncodedContent(form);
            using var response = await client.PostAsync(new Uri(configuration.TokenEndpoint), body, context.RequestAborted);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Refreshing the ERU token failed with {StatusCode}", (int)response.StatusCode);
                return Result.WithMessages<string>(ValidationMessage.Error("The ERU sign-in has expired; sign in again."));
            }

            using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(context.RequestAborted), cancellationToken: context.RequestAborted);
            var root = json.RootElement;
            var access = root.GetProperty("access_token").GetString() ?? string.Empty;
            var expiresIn = root.TryGetProperty("expires_in", out var seconds) ? seconds.GetInt32() : 60;

            properties.UpdateTokenValue("access_token", access);
            properties.UpdateTokenValue("expires_at", new DateTimeOffset(Clock.UtcNow(), TimeSpan.Zero).AddSeconds(expiresIn).ToString("o", CultureInfo.InvariantCulture));
            if (root.TryGetProperty("refresh_token", out var newRefresh) && newRefresh.GetString() is { Length: > 0 } rotated)
            {
                properties.UpdateTokenValue("refresh_token", rotated);
            }

            await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, properties);
            return Result.For(access);
        }
        catch (HttpRequestException exception)
        {
            logger.LogError(exception, "Refreshing the ERU token failed");
            return Result.WithMessages<string>(ValidationMessage.Error("ERU could not be reached to renew the sign-in."));
        }
    }
}
