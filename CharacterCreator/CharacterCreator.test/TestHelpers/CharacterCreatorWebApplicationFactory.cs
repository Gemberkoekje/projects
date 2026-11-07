using System.Net.Http;
using CharacterCreator.application.Ports;
using CharacterCreator.infrastructure.MartenDb;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CharacterCreator.test.TestHelpers;

/// <summary>
/// Boots the web application in memory for the HTTP specs: no database, no ERU, no VOID. Published
/// rulesets live in <see cref="Rulesets"/>, which the first start seeds as it would PostgreSQL.
/// </summary>
internal sealed class CharacterCreatorWebApplicationFactory : WebApplicationFactory<Program>
{
    /// <summary>A syntactically valid connection string pointing at nothing.</summary>
    /// <remarks>No spec needs a live database: Marten connects on first use, and nothing here uses it.</remarks>
    public const string UnreachableDatabase =
        "Host=localhost;Port=1;Database=charactercreator;Username=charactercreator;Password=charactercreator;Timeout=1;Command Timeout=1";

    /// <summary>The published rulesets, in place of Marten's.</summary>
    public InMemoryRulesetStore Rulesets { get; } = new ();

    /// <summary>Creates an anonymous client that does not follow redirects, so the specs can assert on them.</summary>
    /// <returns>The client.</returns>
    public HttpClient CreateAnonymousClient()
        => CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    /// <summary>Creates a client signed in as someone with these ERU permissions, not following redirects.</summary>
    /// <param name="name">Their name.</param>
    /// <param name="permissions">Their ERU permissions.</param>
    /// <returns>The client.</returns>
    public HttpClient CreateSignedInClient(string name, params string[] permissions)
    {
        var client = CreateAnonymousClient();
        client.DefaultRequestHeaders.Add(TestSignIn.Header, TestSignIn.As(name, permissions));
        return client;
    }

    /// <inheritdoc />
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseEnvironment("Testing");

        // No ERU and no VOID: the app boots anonymous only, as the demo does.
        builder.UseSetting("ConnectionStrings:Postgres", UnreachableDatabase);
        builder.UseSetting("Keycloak:Issuer", string.Empty);
        builder.UseSetting("Void:BaseUrl", string.Empty);

        builder.ConfigureTestServices(services =>
        {
            // The key ring lives in PostgreSQL in the app; in memory here, so rendering a form's
            // antiforgery token needs no database.
            services.AddDataProtection().UseEphemeralDataProtectionProvider();

            // Same lifetime as the Marten store; the startup seed writes here instead.
            services.RemoveAll<MartenRulesetStore>();
            services.RemoveAll<IRulesetStore>();
            services.AddScoped<IRulesetStore>(_ => Rulesets);

            // Who a request is comes from the spec's header; challenge and forbid stay the cookie's.
            services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, TestSignIn>(TestSignIn.Name, null);
            services.PostConfigure<AuthenticationOptions>(options => options.DefaultAuthenticateScheme = TestSignIn.Name);
        });
    }
}
