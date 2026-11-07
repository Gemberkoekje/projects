using System.IO.Compression;
using System.Threading.RateLimiting;
using CharacterCreator.application.Ports;
using CharacterCreator.application.Rulesets;
using CharacterCreator.core.Rulesets;
using CharacterCreator.infrastructure.Adapters.DataProtection;
using CharacterCreator.infrastructure.Content;
using CharacterCreator.infrastructure.MartenDb;
using CharacterCreator.infrastructure.Printing;
using CharacterCreator.infrastructure.Void;
using CharacterCreator.WebApp.Client.Services;
using CharacterCreator.WebApp.Components;
using CharacterCreator.WebApp.Endpoints;
using CharacterCreator.WebApp.Localization;
using JasperFx;
using Marten;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.ResponseCompression;
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
    options.Schema.For<PublishedRuleset>().Identity(x => x.Id);
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

        // Signed in without the permission a page needs: say so, instead of ASP.NET's default
        // /Account/AccessDenied, which does not exist here.
        options.AccessDeniedPath = "/geen-toegang";
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

// VOID (DESIGN.md §6): called from the server with the signed-in SL's or EC's token. Without
// Void:BaseUrl the client says it is not configured, and nothing can be imported.
var voidBaseUrl = builder.Configuration["Void:BaseUrl"];
if (!string.IsNullOrWhiteSpace(voidBaseUrl) && !voidBaseUrl.EndsWith('/'))
{
    // The client asks for "skills" relative to the base; without the slash the base's last segment
    // would be replaced instead of extended.
    throw new InvalidOperationException("Void:BaseUrl must end with a slash.");
}

builder.Services.AddHttpClient<VoidHttpClient>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
    if (!string.IsNullOrWhiteSpace(voidBaseUrl))
    {
        client.BaseAddress = new Uri(voidBaseUrl);
    }
});
builder.Services.AddScoped<IVoidClient>(sp => sp.GetRequiredService<VoidHttpClient>());
builder.Services.AddSingleton<EmbeddedOfflineSnapshot>();
builder.Services.AddSingleton<IOfflineVoidSnapshot>(sp => sp.GetRequiredService<EmbeddedOfflineSnapshot>());

// Published rulesets (DESIGN.md §4.2), and the handlers around them.
builder.Services.AddScoped<MartenRulesetStore>();
builder.Services.AddScoped<IRulesetStore>(sp => sp.GetRequiredService<MartenRulesetStore>());
builder.Services.AddScoped<PreviewImportHandler>();
builder.Services.AddScoped<PublishRulesetHandler>();
builder.Services.AddScoped<SeedRulesetHandler>();
builder.Services.AddScoped<ReadCurrentRulesetHandler>();

// The printed sheet (DESIGN.md §5, Print): MigraDoc, stateless, so one for the app. The endpoint is
// open to anyone and a PDF costs real CPU, hence a limit per address; behind the proxy the address
// is the forwarded one.
builder.Services.AddSingleton<MigraDocSheetWriter>();
builder.Services.AddSingleton<ISheetWriter>(sp => sp.GetRequiredService<MigraDocSheetWriter>());
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = (rejected, cancellationToken) =>
    {
        rejected.HttpContext.Response.Headers.RetryAfter = "60";
        return new ValueTask(rejected.HttpContext.Response.WriteAsJsonAsync(new { message = "Too many sheets from here; try again in a minute." }, cancellationToken));
    };
    options.AddPolicy(SheetEndpoints.RateLimit, context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});

// The visitor's language, from the language cookie: for the server pages, and for the build screen
// while the server prerenders it (the browser reads it from the page instead).
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<CookieUiLanguage>();
builder.Services.AddScoped<IUiLanguage>(sp => sp.GetRequiredService<CookieUiLanguage>());

// The ERU token for VOID, refreshed through Keycloak's token endpoint when it has expired.
builder.Services.AddHttpClient(EruAccessTokens.HttpClientName);
builder.Services.AddScoped<EruAccessTokens>();

// The API writes what the build screen reads: camelCase, enums by name (RulesetJson), compressed.
// A whole ruleset is about 650 KB of JSON, mostly text.
builder.Services.ConfigureHttpJsonOptions(options => RulesetJson.Apply(options.SerializerOptions));
builder.Services.AddResponseCompression(options => options.EnableForHttps = true);

// Brotli's default level is its fastest, which came out bigger than gzip for a ruleset (222 KB
// against 141 KB). A browser fetches a ruleset once and then gets 304s, so the better level is cheap.
builder.Services.Configure<BrotliCompressionProviderOptions>(options => options.Level = CompressionLevel.Optimal);
builder.Services.Configure<GzipCompressionProviderOptions>(options => options.Level = CompressionLevel.Optimal);

// Server-rendered pages, with the build screen interactive in the browser (DESIGN.md decision 12).
builder.Services.AddRazorComponents()
    .AddInteractiveWebAssemblyComponents();

var app = builder.Build();

// The first start publishes the content with the offline snapshot, so the build screen is never
// empty before an SL or EC imported from VOID. Once a ruleset exists it does nothing.
using (var scope = app.Services.CreateScope())
{
    var seeded = await scope.ServiceProvider.GetRequiredService<SeedRulesetHandler>().HandleAsync(CancellationToken.None);
    if (!seeded.IsValid)
    {
        app.Logger.LogWarning("No ruleset could be seeded: {Reason}", string.Join("; ", seeded.Messages.Select(message => message.Message)));
    }
}

app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/fout", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/niet-gevonden", createScopeForStatusCodePages: true);

// The not-found page is for the pages only. An API answer stays what it is: re-executing a bodiless
// 415 or 429 against the page would replay the POST there and come back as a confusing 400.
app.Use((context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase)
        && context.Features.Get<IStatusCodePagesFeature>() is { } pages)
    {
        pages.Enabled = false;
    }

    return next(context);
});
app.UseResponseCompression();
app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(CharacterCreator.WebApp.Client._Imports).Assembly);

app.MapAccountEndpoints();
app.MapRulesetEndpoints();
app.MapLanguageEndpoints();
app.MapSheetEndpoints();
app.MapGet("/healthz", () => "ok").AllowAnonymous();

await app.RunAsync();
