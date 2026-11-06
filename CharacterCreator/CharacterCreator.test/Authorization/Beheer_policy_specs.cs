using System.Security.Claims;
using CharacterCreator.WebApp.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace CharacterCreator.test.Authorization;

/// <summary>Who may import and publish rulesets: SL or EC, by name (DESIGN.md decision 15).</summary>
public sealed class Beheer_policy_specs
{
    [TestCase(EruPermissions.VoidReferee)]
    [TestCase(EruPermissions.VoidEventControl)]
    [TestCase(EruPermissions.GrantAll)]
    public async Task an_sl_or_ec_may(string permission)
        => (await Allowed(SignedIn(permission))).Should().BeTrue();

    [Test]
    public async Task infobalie_alone_may_not_although_it_is_above_referee_in_voids_ladder()
        => (await Allowed(SignedIn(EruPermissions.VoidInfobalie))).Should().BeFalse();

    [Test]
    public async Task a_signed_in_player_without_a_role_may_not()
        => (await Allowed(SignedIn())).Should().BeFalse();

    [Test]
    public async Task an_anonymous_visitor_may_not()
        => (await Allowed(new ClaimsPrincipal(new ClaimsIdentity()))).Should().BeFalse();

    private static ClaimsPrincipal SignedIn(params string[] permissions)
        => new (new ClaimsIdentity(
            permissions.Select(permission => new Claim(EruClaims.Permission, permission)).Append(new Claim(EruClaims.Name, "Test")),
            authenticationType: "Test"));

    private static async Task<bool> Allowed(ClaimsPrincipal user)
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddAuthorization(options => options.AddPolicy(CharacterCreatorPolicies.Beheer, CharacterCreatorPolicies.BeheerPolicy()));
        await using var provider = services.BuildServiceProvider();

        var result = await provider.GetRequiredService<IAuthorizationService>().AuthorizeAsync(user, CharacterCreatorPolicies.Beheer);
        return result.Succeeded;
    }
}
