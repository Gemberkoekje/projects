using DatumPrikker.WebApp.Auth;
using Microsoft.Extensions.Configuration;
using System.Security.Claims;

namespace DatumPrikker.test.Web;

public sealed class Auth_specs
{
    [Test]
    public void no_provider_is_offered_without_configuration()
    {
        LoginProviders.Configured(Config([])).Should().BeEmpty();
    }

    [Test]
    public void a_provider_is_offered_only_with_both_its_id_and_its_secret()
    {
        var configuration = Config(new Dictionary<string, string?>
        {
            ["Authentication:Google:ClientId"] = "id",
            ["Authentication:Google:ClientSecret"] = "secret",
            ["Authentication:Microsoft:ClientId"] = "id",
            ["Authentication:Microsoft:ClientSecret"] = " ",
        });

        LoginProviders.Configured(configuration).Should().Equal(LoginProviders.Google);
    }

    [Test]
    public void an_external_sign_in_gets_a_provider_qualified_account()
    {
        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, "1234"),
                new Claim(ClaimTypes.Name, "Arie Hofland"),
                new Claim(ClaimTypes.Email, "Arie@Example.com"),
            ],
            "Google");

        AccountClaims.Stamp(identity, LoginProviders.Google);
        var account = AccountClaims.From(new ClaimsPrincipal(identity));

        account.Should().BeEquivalentTo(new Account { Id = "google:1234", Name = "Arie Hofland", Email = "arie@example.com" });
    }

    [Test]
    public void without_a_stamped_account_you_are_anonymous()
    {
        var unstamped = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "1234")], "Google");

        AccountClaims.From(new ClaimsPrincipal(unstamped)).IsAnonymous.Should().BeTrue();
        AccountClaims.From(new ClaimsPrincipal(new ClaimsIdentity())).IsAnonymous.Should().BeTrue();
    }

    [Test]
    public void the_dev_login_makes_a_dev_account()
    {
        var account = AccountClaims.From(AccountClaims.ForDev("Dev", "dev@example.com", "Cookies"));

        account.Id.Should().Be("dev:dev@example.com");
        account.Email.Should().Be("dev@example.com");
    }

    private static IConfiguration Config(Dictionary<string, string?> values)
        => new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
