using System.Text;
using CharacterCreator.WebApp.Authorization;
using CharacterCreator.WebApp.Endpoints;
using Microsoft.AspNetCore.Http;

namespace CharacterCreator.test.Authorization;

public sealed class EruPermissions_specs
{
    [Test]
    public void reads_the_vortex_api_roles_out_of_an_access_token()
    {
        var token = Token("""{"resource_access":{"vortex-api":{"roles":["void.referee","plot.write"]},"account":{"roles":["manage-account"]}}}""");

        EruPermissions.Read(token).Should().Equal("plot.write", "void.referee");
    }

    [TestCase("")]
    [TestCase("not-a-jwt")]
    [TestCase("a.!!!.c")]
    public void an_unreadable_token_has_no_permissions(string token)
        => EruPermissions.Read(token).Should().BeEmpty();

    [Test]
    public void a_token_without_vortex_api_roles_has_no_permissions()
        => EruPermissions.Read(Token("""{"sub":"123"}""")).Should().BeEmpty();

    [TestCase("/voorbeelden", "/voorbeelden")]
    [TestCase("/vragen?x=1", "/vragen?x=1")]
    [TestCase(null, "/")]
    [TestCase("", "/")]
    [TestCase("https://evil.example/", "/")]
    [TestCase("//evil.example/", "/")]
    [TestCase("/\\evil.example/", "/")]
    [TestCase("voorbeelden", "/")]
    public void a_return_url_stays_inside_this_app(string? returnUrl, string expected)
        => AccountEndpoints.LocalReturnUrl(PathString.Empty, returnUrl)
            .Should().Be(expected, "a sign-in must never bounce anyone to another site");

    private static string Token(string payload)
    {
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(payload)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return $"eyJhbGciOiJub25lIn0.{encoded}.signature";
    }
}
