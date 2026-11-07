using System.Security.Claims;
using System.Text.Encodings.Web;
using CharacterCreator.WebApp.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CharacterCreator.test.TestHelpers;

/// <summary>
/// Signs a spec's request in as whoever its <see cref="Header"/> names, with the ERU permissions it
/// lists: <c>Name;perm1,perm2</c>. Without the header the request stays anonymous. Challenge and
/// forbid stay with the app's cookie, so redirects are the app's own.
/// </summary>
internal sealed class TestSignIn(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    /// <summary>The scheme's name.</summary>
    public const string Name = "Test";

    /// <summary>The request header that says who is signed in.</summary>
    public const string Header = "X-Test-User";

    /// <summary>The header value for a person with these permissions.</summary>
    /// <param name="name">Their name.</param>
    /// <param name="permissions">Their ERU permissions, such as <c>void.referee</c>.</param>
    /// <returns>The value.</returns>
    public static string As(string name, params string[] permissions) => $"{name};{string.Join(',', permissions)}";

    /// <inheritdoc />
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(Header, out var value) || string.IsNullOrEmpty(value))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var parts = value.ToString().Split(';', 2);
        var claims = new List<Claim> { new (EruClaims.Name, parts[0]), new (EruClaims.Subject, parts[0]) };
        claims.AddRange((parts.Length > 1 ? parts[1] : string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(permission => new Claim(EruClaims.Permission, permission)));

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Name, EruClaims.Name, "role"));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Name)));
    }
}
