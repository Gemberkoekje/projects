namespace CharacterCreator.WebApp.Authorization;

/// <summary>
/// The claim types on a signed-in principal. The first three are ERU's own JWT claim names, kept as
/// they are (<c>MapInboundClaims = false</c>); the last is minted at sign-in from the access token.
/// </summary>
public static class EruClaims
{
    /// <summary>The stable Keycloak user id.</summary>
    public const string Subject = "sub";

    /// <summary>The display name.</summary>
    public const string Name = "name";

    /// <summary>The e-mail address.</summary>
    public const string Email = "email";

    /// <summary>
    /// One ERU permission (a <c>vortex-api</c> client role such as <c>void.referee</c>), one claim
    /// per permission. Policies test these; nothing else decides access.
    /// </summary>
    public const string Permission = "eru_permission";
}
