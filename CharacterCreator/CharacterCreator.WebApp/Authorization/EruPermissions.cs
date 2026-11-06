using System.Text.Json;

namespace CharacterCreator.WebApp.Authorization;

/// <summary>
/// The Vortex ERU permissions this app knows, and how to read them out of a Keycloak access token.
/// They ride in <c>resource_access.vortex-api.roles</c> as plain strings, the same convention
/// landerijen, plotboek and werkplaats read.
/// </summary>
public static class EruPermissions
{
    /// <summary>VOID's referee role: every SL holds it.</summary>
    public const string VoidReferee = "void.referee";

    /// <summary>VOID's infobalie role. Above the referee in VOID's ladder, but not an SL or EC here.</summary>
    public const string VoidInfobalie = "void.infobalie";

    /// <summary>VOID's event control role: the EC.</summary>
    public const string VoidEventControl = "void.eventcontrol";

    /// <summary>The ERU grant-all (superuser) permission; satisfies every check.</summary>
    public const string GrantAll = "*_*";

    /// <summary>The Keycloak client the permissions are granted on.</summary>
    private const string VortexApiClient = "vortex-api";

    /// <summary>Reads every ERU permission out of an access token.</summary>
    /// <param name="accessToken">The raw JWT access token; may be empty.</param>
    /// <returns>The permissions, sorted; empty when the token is missing or unreadable.</returns>
    /// <remarks>
    /// The signature is not checked and does not need to be: the token came straight out of the
    /// OIDC handler, which already validated it, and this only reads a claim out of it.
    /// </remarks>
    public static IReadOnlyList<string> Read(string accessToken)
    {
        var permissions = new SortedSet<string>(StringComparer.Ordinal);
        using var document = ParsePayload(accessToken);

        if (document is not null &&
            document.RootElement.ValueKind == JsonValueKind.Object &&
            document.RootElement.TryGetProperty("resource_access", out var resourceAccess) &&
            resourceAccess.ValueKind == JsonValueKind.Object &&
            resourceAccess.TryGetProperty(VortexApiClient, out var client) &&
            client.ValueKind == JsonValueKind.Object &&
            client.TryGetProperty("roles", out var roles) &&
            roles.ValueKind == JsonValueKind.Array)
        {
            // Indexed rather than foreach: JsonElement's enumerator is a disposable struct, and
            // walking it with `foreach` trips IDISP004.
            for (var i = 0; i < roles.GetArrayLength(); i++)
            {
                if (roles[i].ValueKind == JsonValueKind.String)
                {
                    permissions.Add(roles[i].GetString()!);
                }
            }
        }

        return [.. permissions];
    }

    /// <summary>Base64url-decodes and parses a JWT payload.</summary>
    /// <param name="token">The raw JWT.</param>
    /// <returns>The payload, or <c>null</c> when the token is absent or malformed.</returns>
    private static JsonDocument? ParsePayload(string token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return null;
        }

        var parts = token.Split('.');
        if (parts.Length < 2)
        {
            return null;
        }

        try
        {
            return JsonDocument.Parse(Base64UrlDecode(parts[1]));
        }
        catch (FormatException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Decodes a base64url segment, re-adding the padding a JWT strips.</summary>
    /// <param name="input">The segment.</param>
    /// <returns>The decoded bytes.</returns>
    private static byte[] Base64UrlDecode(string input)
    {
        var value = input.Replace('-', '+').Replace('_', '/');
        value += (value.Length % 4) switch
        {
            2 => "==",
            3 => "=",
            _ => string.Empty,
        };
        return Convert.FromBase64String(value);
    }
}
