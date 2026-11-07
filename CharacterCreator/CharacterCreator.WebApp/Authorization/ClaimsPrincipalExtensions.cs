using System.Security.Claims;

namespace CharacterCreator.WebApp.Authorization;

/// <summary>What the crest bar shows of a signed-in player.</summary>
public static class ClaimsPrincipalExtensions
{
    /// <summary>The player's name as ERU gives it, or the e-mail address when there is no name.</summary>
    /// <param name="principal">The signed-in principal.</param>
    /// <returns>The name; empty for an anonymous visitor.</returns>
    public static string DisplayName(this ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        var name = principal.FindFirst(EruClaims.Name)?.Value;
        return string.IsNullOrWhiteSpace(name)
            ? principal.FindFirst(EruClaims.Email)?.Value ?? string.Empty
            : name.Trim();
    }

    /// <summary>The initials for the seal in the crest bar: first and last word, at most two letters.</summary>
    /// <param name="principal">The signed-in principal.</param>
    /// <returns>The initials, upper case; empty when there is no name.</returns>
    public static string Initials(this ClaimsPrincipal principal)
    {
        var words = principal.DisplayName().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Length switch
        {
            0 => string.Empty,
            1 => words[0][..1].ToUpperInvariant(),
            _ => string.Concat(words[0][..1], words[^1][..1]).ToUpperInvariant(),
        };
    }
}
