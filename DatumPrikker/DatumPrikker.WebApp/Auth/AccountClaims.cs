using System.Security.Claims;

namespace DatumPrikker.WebApp.Auth;

/// <summary>Turns a signed-in user's claims into an <see cref="Account"/> (D6).</summary>
public static class AccountClaims
{
    /// <summary>The claim holding the provider-qualified account id, added when the ticket is made.</summary>
    public const string AccountIdType = "datumprikker:account";

    /// <summary>The prefix of dev-login account ids (Development only).</summary>
    public const string DevPrefix = "dev";

    /// <summary>The account behind a user; <see cref="Account.Anonymous"/> when nobody is signed in.</summary>
    /// <param name="user">The request's user.</param>
    /// <returns>The account.</returns>
    public static Account From(ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);

        var id = user.FindFirst(AccountIdType)?.Value ?? string.Empty;
        if (user.Identity?.IsAuthenticated != true || id.Length == 0)
        {
            return Account.Anonymous;
        }

        var rawEmail = user.FindFirst(ClaimTypes.Email)?.Value ?? string.Empty;
        return new Account
        {
            Id = id,
            Name = user.FindFirst(ClaimTypes.Name)?.Value ?? string.Empty,
            Email = Email.TryNormalise(rawEmail, out var email) ? email : string.Empty,
        };
    }

    /// <summary>Adds the account-id claim to a fresh external sign-in.</summary>
    /// <param name="identity">The identity the provider made; nothing happens without one.</param>
    /// <param name="provider">The provider it came from.</param>
    public static void Stamp(ClaimsIdentity? identity, LoginProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        var subject = identity?.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? string.Empty;
        if (identity is not null && subject.Length > 0 && !identity.HasClaim(claim => claim.Type == AccountIdType))
        {
            identity.AddClaim(new Claim(AccountIdType, $"{provider.Prefix}:{subject}"));
        }
    }

    /// <summary>A signed-in user without any provider, for the Development-only dev login.</summary>
    /// <param name="name">Any name.</param>
    /// <param name="email">A normalised email address; it is also the account's subject.</param>
    /// <param name="scheme">The cookie scheme that will carry it.</param>
    /// <returns>The principal to sign in.</returns>
    public static ClaimsPrincipal ForDev(string name, string email, string scheme)
    {
        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, email),
                new Claim(ClaimTypes.Name, name),
                new Claim(ClaimTypes.Email, email),
                new Claim(AccountIdType, $"{DevPrefix}:{email}"),
            ],
            scheme);

        return new ClaimsPrincipal(identity);
    }
}
