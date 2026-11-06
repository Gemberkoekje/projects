using System.Net.Http;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace CharacterCreator.test.TestHelpers;

/// <summary>Boots the web application in memory for the HTTP specs, anonymous and without a database.</summary>
internal sealed class CharacterCreatorWebApplicationFactory : WebApplicationFactory<Program>
{
    /// <summary>A syntactically valid connection string pointing at nothing.</summary>
    /// <remarks>No spec needs a live database: Marten connects on first use, and nothing here uses it.</remarks>
    public const string UnreachableDatabase =
        "Host=localhost;Port=1;Database=charactercreator;Username=charactercreator;Password=charactercreator;Timeout=1;Command Timeout=1";

    /// <summary>Creates an anonymous client that does not follow redirects, so the specs can assert on them.</summary>
    /// <returns>The client.</returns>
    public HttpClient CreateAnonymousClient()
        => CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    /// <inheritdoc />
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseEnvironment("Testing");

        // No ERU: the app boots anonymous only, as the demo does.
        builder.UseSetting("ConnectionStrings:Postgres", UnreachableDatabase);
        builder.UseSetting("Keycloak:Issuer", string.Empty);

        // The key ring lives in PostgreSQL in the app; in memory here, so rendering a form's
        // antiforgery token needs no database.
        builder.ConfigureTestServices(services => services.AddDataProtection().UseEphemeralDataProtectionProvider());
    }
}
