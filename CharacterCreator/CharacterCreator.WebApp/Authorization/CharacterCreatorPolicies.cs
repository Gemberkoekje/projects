namespace CharacterCreator.WebApp.Authorization;

/// <summary>
/// The authorization policies. Each names what it grants, not who holds it, so a page reads
/// <c>@attribute [Authorize(Policy = CharacterCreatorPolicies.Beheer)]</c>.
/// </summary>
/// <remarks>
/// There is no fallback policy: the build screen, the presets and the FAQ are for anyone, signed in
/// or not (DESIGN.md §8). A page that needs more says so.
/// </remarks>
public static class CharacterCreatorPolicies
{
    /// <summary>Importing and publishing rulesets, and from v4 editing the content: SL or EC.</summary>
    public const string Beheer = "Beheer";

    /// <summary>Builds the <see cref="Beheer"/> policy.</summary>
    /// <returns>The policy.</returns>
    /// <remarks>
    /// The permissions are listed by name, not taken as VOID's ladder, so <c>void.infobalie</c> on
    /// its own does not qualify (DESIGN.md decision 15; whether it should is open question 1).
    /// </remarks>
    public static AuthorizationPolicy BeheerPolicy()
        => new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .RequireClaim(EruClaims.Permission, EruPermissions.VoidReferee, EruPermissions.VoidEventControl, EruPermissions.GrantAll)
            .Build();
}
