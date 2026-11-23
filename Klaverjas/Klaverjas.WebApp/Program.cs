using Klaverjas.application.Ports;
using Klaverjas.infrastructure.Adapters.Application;
using Klaverjas.infrastructure.Adapters.DataProtection;
using Klaverjas.infrastructure.Adapters.Excel;
using Klaverjas.WebApp.Pages;
using Marten;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System.Threading.RateLimiting;
using Weasel.Core;

// The content root is the app's own directory, not whatever working directory it was started from.
// Without that, static assets are found in the manifest next to the assembly but their files are
// not, and every asset comes back as an empty 200.
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});

// Behind the cluster's ingress, which terminates TLS.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

var connectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException("Connection string 'Postgres' not found.");

// Dad's password. Read here only to fail at boot rather than at the first sign-in; the sign-in page
// reads it again at its own read site. (The players' password comes with the players' page, slice 4.)
if (string.IsNullOrWhiteSpace(builder.Configuration["Auth:Password"]))
{
    throw new InvalidOperationException("Auth:Password is not set; without it nobody can sign in.");
}

// Marten as a document store: players, evenings (plus every replaced version), the settings, a
// planned workbook import and the data-protection keys. The standings are computed, never stored (D8).
builder.Services.AddMarten(options =>
{
    options.Connection(connectionString);
    options.AutoCreateSchemaObjects = JasperFx.AutoCreate.CreateOrUpdate;

    // Enums by name: the scores are kept for years and an integer would silently change meaning the
    // day somebody inserts a member.
    options.UseSystemTextJsonForSerialization(EnumStorage.AsString);

    options.Schema.For<Speler>().Identity(x => x.Id);
    options.Schema.For<Avond>().Identity(x => x.Id);
    options.Schema.For<AvondVersie>().Identity(x => x.Id);
    options.Schema.For<Instellingen>().Identity(x => x.Id);
    options.Schema.For<ImportVoorstel>().Identity(x => x.Id);
    options.Schema.For<DataProtectionSleutel>().Identity(x => x.Id);
});

// Data protection. The pod has no volume, so without a persisted ring every image bump would hand
// out fresh keys and sign Dad out of a cookie that is meant to last a year.
builder.Services.AddDataProtection().SetApplicationName("klaverjas");
builder.Services.AddSingleton<IConfigureOptions<KeyManagementOptions>>(sp =>
    new ConfigureOptions<KeyManagementOptions>(options =>
        options.XmlRepository = new MartenXmlRepository(sp.GetRequiredService<IDocumentStore>())));

// Signed in for a year on each computer, sliding, so a weekly visit never asks again (D7).
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Aanmelden";
        options.Cookie.Name = "klaverjas";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.ExpireTimeSpan = AanmeldenModel.Geldigheid;
        options.SlidingExpiration = true;
    });

// Every page is closed unless it says [AllowAnonymous]: the site is on the public ingress.
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

// The password form is the one thing a stranger can poke at, so it is rate limited per address.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(AanmeldenModel.RateLimitPolicy, context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "onbekend",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(5) }));
});

builder.Services.AddRazorPages()
    .AddMvcOptions(options => options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true);

// Persistence: stateless stores over the singleton IDocumentStore, scoped as in the template.
builder.Services.AddScoped<MartenSpelerStore>();
builder.Services.AddScoped<ISpelerStore>(sp => sp.GetRequiredService<MartenSpelerStore>());
builder.Services.AddScoped<MartenAvondStore>();
builder.Services.AddScoped<IAvondStore>(sp => sp.GetRequiredService<MartenAvondStore>());
builder.Services.AddScoped<MartenInstellingenStore>();
builder.Services.AddScoped<IInstellingenStore>(sp => sp.GetRequiredService<MartenInstellingenStore>());
builder.Services.AddScoped<MartenImportVoorstelStore>();
builder.Services.AddScoped<IImportVoorstelStore>(sp => sp.GetRequiredService<MartenImportVoorstelStore>());

// The workbook reader and the export writer hold no state.
builder.Services.AddSingleton<ClosedXmlStandenLezer>();
builder.Services.AddSingleton<IStandenLezer>(sp => sp.GetRequiredService<ClosedXmlStandenLezer>());
builder.Services.AddSingleton<ClosedXmlExportSchrijver>();
builder.Services.AddSingleton<IExportSchrijver>(sp => sp.GetRequiredService<ClosedXmlExportSchrijver>());

// Command handlers.
builder.Services.AddScoped<UitslagOpslaanHandler>();
builder.Services.AddScoped<SpelerToevoegenHandler>();
builder.Services.AddScoped<SpelerAanpassenHandler>();
builder.Services.AddScoped<InstellingenOpslaanHandler>();
builder.Services.AddScoped<GeenSpeelavondToevoegenHandler>();
builder.Services.AddScoped<GeenSpeelavondWeghalenHandler>();
builder.Services.AddScoped<ImportVoorbereidenHandler>();
builder.Services.AddScoped<ImportKoppelingWijzigenHandler>();
builder.Services.AddScoped<ImportUitvoerenHandler>();
builder.Services.AddScoped<ImportAnnulerenHandler>();

// Query handlers.
builder.Services.AddScoped<AvondenOphalenHandler>();
builder.Services.AddScoped<UitslagOphalenHandler>();
builder.Services.AddScoped<StandOphalenHandler>();
builder.Services.AddScoped<SpelersOphalenHandler>();
builder.Services.AddScoped<InstellingenOphalenHandler>();
builder.Services.AddScoped<ImportVoorstelOphalenHandler>();
builder.Services.AddScoped<ExportMakenHandler>();

var app = builder.Build();

app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Fout");
    app.UseHsts();
}

// Nothing here is for search engines: the players' names are on these pages (D5).
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
    await next(context);
});

app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

// The sign-in page and its stylesheet must load before anyone is signed in.
app.MapStaticAssets().AllowAnonymous();
app.MapRazorPages().WithStaticAssets();
app.MapGet("/healthz", () => "ok").AllowAnonymous();

await app.RunAsync();
