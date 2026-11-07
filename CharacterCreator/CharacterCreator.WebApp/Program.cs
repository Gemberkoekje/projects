using CharacterCreator.application.Ports;
using CharacterCreator.infrastructure.Adapters.DataProtection;
using CharacterCreator.infrastructure.Content;
using CharacterCreator.WebApp.Components;
using CharacterCreator.WebApp.Endpoints;
using JasperFx;
using Marten;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Weasel.Core;

// The content root is the app's own directory, not whatever working directory it was started from.
// Without that, static assets are found in the manifest next to the assembly but their files are
// not, and every asset comes back as an empty 200 (Kluskat's lesson).
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});

// Behind a TLS-terminating proxy: the cluster's nginx ingress for the demo, Caddy in production.
// The OIDC redirect URI is built from these headers; without them Keycloak would be asked to return
// to http://.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

var connectionString = builder.Configuration.GetConnectionString("Postgres");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("Connection string 'Postgres' (ConnectionStrings:Postgres) is not set.");
}

// Marten is the document store: published rulesets and plans from phase 2 on, and the
// data-protection keys now. No event store (DESIGN.md decision 13).
builder.Services.AddMarten(options =>
{
    options.Connection(connectionString);
    options.AutoCreateSchemaObjects = AutoCreate.CreateOrUpdate;

    // Enums by name: documents are kept, and an integer would silently change meaning the day
    // somebody inserts a member.
    options.UseSystemTextJsonForSerialization(EnumStorage.AsString);

    options.Schema.For<DataProtectionKeyDocument>().Identity(x => x.Id);
});

// Data protection. The pod has no volume, so the key ring lives in PostgreSQL; otherwise every image
// bump would invalidate sign-in cookies and the antiforgery tokens of forms already on screen.
builder.Services.AddDataProtection().SetApplicationName("charactercreator");
builder.Services.AddSingleton<IConfigureOptions<KeyManagementOptions>>(sp =>
    new ConfigureOptions<KeyManagementOptions>(options =>
        options.XmlRepository = new MartenXmlRepository(sp.GetRequiredService<IDocumentStore>())));

// Sign-in. No local accounts and no ASP.NET Identity: the Vortex ERU (Keycloak) is the only way in,
// and signing in is optional (DESIGN.md decision 9). The cookie carries the ERU's decision for a
// working session; phase 7 revisits the length once VOID is called with the stored access token.
var authentication = builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "charactercreator.aanmelding";
        options.LoginPath = "/aanmelden";
        options.LogoutPath = "/afmelden";
        options.ReturnUrlParameter = "returnUrl";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
    });

// Vortex ERU (Keycloak), gated on configuration: without an issuer the app runs anonymous only, as
// the specs and the gembernodes demo do, and /aanmelden explains why there is no sign-in.
var keycloakIssuer = builder.Configuration["Keycloak:Issuer"];
if (!string.IsNullOrWhiteSpace(keycloakIssuer))
{
    authentication.AddOpenIdConnect(EruSignIn.Scheme, EruSignIn.DisplayName, options =>
    {
        options.Authority = keycloakIssuer;
        options.ClientId = builder.Configuration["Keycloak:ClientId"]
            ?? throw new InvalidOperationException("Keycloak:ClientId is not set while Keycloak:Issuer is.");

        // vortex-dev is a public client (PKCE); production gets a confidential one with a secret.
        var keycloakClientSecret = builder.Configuration["Keycloak:ClientSecret"];
        if (!string.IsNullOrWhiteSpace(keycloakClientSecret))
        {
            options.ClientSecret = keycloakClientSecret;
        }

        options.ResponseType = OpenIdConnectResponseType.Code;
        options.UsePkce = true;

        // Plain http metadata is only tolerated against a local (http) issuer for development.
        options.RequireHttpsMetadata = keycloakIssuer.StartsWith("https", StringComparison.OrdinalIgnoreCase);

        options.Scope.Clear();
        options.Scope.Add("openid");
        options.Scope.Add("profile");
        options.Scope.Add("email");

        // SaveTokens is load-bearing: the id_token ends the ERU session at sign-out, and from phase 7
        // the access token is what the server calls VOID with on the player's behalf.
        options.SaveTokens = true;
        options.GetClaimsFromUserInfoEndpoint = true;

        // Keep the JWT claim names (sub, name, email) instead of the long WS-* URIs.
        options.MapInboundClaims = false;
        options.TokenValidationParameters.NameClaimType = EruClaims.Name;

        options.CallbackPath = "/signin-oidc";
        options.SignedOutCallbackPath = "/signout-callback-oidc";
        options.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;

        options.EventsType = typeof(EruSignInEvents);
    });

    builder.Services.AddScoped<EruSignInEvents>();
}

// No fallback policy: the build screen is for anyone. Pages that need more name their policy.
builder.Services.AddAuthorizationBuilder()
    .AddPolicy(CharacterCreatorPolicies.Beheer, CharacterCreatorPolicies.BeheerPolicy());

builder.Services.AddCascadingAuthenticationState();

// The content per ruleset (DESIGN.md §4.1), embedded in the infrastructure assembly: stateless after
// the first read, so one for the app.
builder.Services.AddSingleton<EmbeddedContentSource>();
builder.Services.AddSingleton<IContentSource>(sp => sp.GetRequiredService<EmbeddedContentSource>());

// Server-rendered pages, with the build screen interactive in the browser (DESIGN.md decision 12).
builder.Services.AddRazorComponents()
    .AddInteractiveWebAssemblyComponents();

var app = builder.Build();

app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/fout", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/niet-gevonden", createScopeForStatusCodePages: true);

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(CharacterCreator.WebApp.Client._Imports).Assembly);

app.MapAccountEndpoints();
app.MapGet("/healthz", () => "ok").AllowAnonymous();

await app.RunAsync();
