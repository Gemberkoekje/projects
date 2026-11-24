using Klaverjas.application.Ports;
using Klaverjas.infrastructure.Adapters.Application;
using Klaverjas.infrastructure.Adapters.DataProtection;
using Klaverjas.infrastructure.Adapters.Excel;
using Klaverjas.infrastructure.Adapters.Mail;
using Klaverjas.infrastructure.Adapters.Pdf;
using Klaverjas.WebApp;
using Marten;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
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

// Dad's first password, and later the site keeper's way back in. Read here only to fail at boot
// rather than at the first sign-in; the sign-in reads it again at its own read site. The players'
// password is Dad's to choose in Instellingen (D7).
Toegang.ConfigWachtwoord(builder.Configuration);

// The mail account (D2, Q1): on the cluster the site keeper's Gmail, from its mail secret. Optional:
// without a sender address (and, with a user, its password) mailing is switched off and the screens say so.
var smtp = new SmtpInstellingen
{
    Host = builder.Configuration["Mail:Host"] ?? string.Empty,
    Port = builder.Configuration.GetValue("Mail:Port", 587),
    StartTls = builder.Configuration.GetValue("Mail:StartTls", true),
    Gebruiker = builder.Configuration["Mail:User"] ?? string.Empty,
    Wachtwoord = builder.Configuration["Mail:Password"] ?? string.Empty,
    Van = builder.Configuration["Mail:From"] is { Length: > 0 } van ? van : builder.Configuration["Mail:User"] ?? string.Empty,
};

// Marten as a document store: players, evenings (plus every replaced version), each evening's
// preparation, the settings, a planned workbook import, the standings Dad put online, every mailing
// and the data-protection keys. The standings are computed; only the copy online is stored (D2, D8).
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
    options.Schema.For<Voorbereiding>().Identity(x => x.Id);
    options.Schema.For<OnlineStand>().Identity(x => x.Id);
    options.Schema.For<Verzending>().Identity(x => x.Id);
    options.Schema.For<DataProtectionSleutel>().Identity(x => x.Id);
});

// Data protection. The pod has no volume, so without a persisted ring every image bump would hand
// out fresh keys and sign Dad out of a cookie that is meant to last a year.
builder.Services.AddDataProtection().SetApplicationName("klaverjas");
builder.Services.AddSingleton<IConfigureOptions<KeyManagementOptions>>(sp =>
    new ConfigureOptions<KeyManagementOptions>(options =>
        options.XmlRepository = new MartenXmlRepository(sp.GetRequiredService<IDocumentStore>())));

// Two doors, one cookie (D7): the players' password opens the standings at /, Dad's opens /beheer.
// Signed in for a year on each phone or computer, sliding, so a weekly visit never asks again. A
// cookie opened with a password that has since changed is thrown away (Toegang.ControleerAsync).
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = Toegang.SpelersAanmelden;
        options.AccessDeniedPath = Toegang.BeheerAanmelden;
        options.Cookie.Name = "klaverjas";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.ExpireTimeSpan = Toegang.Geldigheid;
        options.SlidingExpiration = true;
        options.Events.OnValidatePrincipal = Toegang.ControleerAsync;
        options.Events.OnRedirectToLogin = Toegang.NaarAanmelden;
    });

// Every page is closed unless it says [AllowAnonymous]: the site is on the public ingress. Signed
// in at all (player or Dad) opens the players' page; /beheer needs Dad's role.
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
    .AddPolicy(Toegang.BeheerBeleid, policy => policy.RequireRole(Toegang.Beheer));

// The password forms are the one thing a stranger can poke at, so posting them is rate limited per
// address. The players' limit is higher: on the club evening they may all sign in over one wifi.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(Toegang.RateLimitBeheer, context => Aanmeldpogingen(context, 10));
    options.AddPolicy(Toegang.RateLimitSpelers, context => Aanmeldpogingen(context, 40));
});

builder.Services.AddRazorPages(options => options.Conventions.AuthorizeFolder("/Beheer", Toegang.BeheerBeleid))
    .AddMvcOptions(options => options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true);

// Addresses in lower case, so the links read /beheer/stand like the address Dad types.
builder.Services.Configure<RouteOptions>(options => options.LowercaseUrls = true);

// Persistence: stateless stores over the singleton IDocumentStore, scoped as in the template.
builder.Services.AddScoped<MartenSpelerStore>();
builder.Services.AddScoped<ISpelerStore>(sp => sp.GetRequiredService<MartenSpelerStore>());
builder.Services.AddScoped<MartenAvondStore>();
builder.Services.AddScoped<IAvondStore>(sp => sp.GetRequiredService<MartenAvondStore>());
builder.Services.AddScoped<MartenInstellingenStore>();
builder.Services.AddScoped<IInstellingenStore>(sp => sp.GetRequiredService<MartenInstellingenStore>());
builder.Services.AddScoped<MartenImportVoorstelStore>();
builder.Services.AddScoped<IImportVoorstelStore>(sp => sp.GetRequiredService<MartenImportVoorstelStore>());
builder.Services.AddScoped<MartenVoorbereidingStore>();
builder.Services.AddScoped<IVoorbereidingStore>(sp => sp.GetRequiredService<MartenVoorbereidingStore>());
builder.Services.AddScoped<MartenOnlineStandStore>();
builder.Services.AddScoped<IOnlineStandStore>(sp => sp.GetRequiredService<MartenOnlineStandStore>());
builder.Services.AddScoped<MartenVerzendingStore>();
builder.Services.AddScoped<IVerzendingStore>(sp => sp.GetRequiredService<MartenVerzendingStore>());

// The workbook reader and the export writer hold no state.
builder.Services.AddSingleton<ClosedXmlStandenLezer>();
builder.Services.AddSingleton<IStandenLezer>(sp => sp.GetRequiredService<ClosedXmlStandenLezer>());
builder.Services.AddSingleton<ClosedXmlExportSchrijver>();
builder.Services.AddSingleton<IExportSchrijver>(sp => sp.GetRequiredService<ClosedXmlExportSchrijver>());

// The PDF maker and the mail sender hold no state between calls (the sender opens a connection per mailing).
builder.Services.AddSingleton<QuestPdfStandMaker>();
builder.Services.AddSingleton<IStandPdfMaker>(sp => sp.GetRequiredService<QuestPdfStandMaker>());
builder.Services.AddSingleton(smtp);
builder.Services.AddSingleton<MailKitMailVerzender>();
builder.Services.AddSingleton<IMailVerzender>(sp => sp.GetRequiredService<MailKitMailVerzender>());

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
builder.Services.AddScoped<VoorbereidingOpslaanHandler>();
builder.Services.AddScoped<StandOnlineZettenHandler>();
builder.Services.AddScoped<StandMailenHandler>();

// Query handlers.
builder.Services.AddScoped<AvondenOphalenHandler>();
builder.Services.AddScoped<UitslagOphalenHandler>();
builder.Services.AddScoped<StandOphalenHandler>();
builder.Services.AddScoped<SpelersOphalenHandler>();
builder.Services.AddScoped<InstellingenOphalenHandler>();
builder.Services.AddScoped<ImportVoorstelOphalenHandler>();
builder.Services.AddScoped<ExportMakenHandler>();
builder.Services.AddScoped<VoorbereidingOphalenHandler>();
builder.Services.AddScoped<KaartjesOphalenHandler>();
builder.Services.AddScoped<OnlineStandOphalenHandler>();
builder.Services.AddScoped<MailOphalenHandler>();

var app = builder.Build();

if (!smtp.IsCompleet)
{
    app.Logger.LogInformation("Mailen staat uit: Mail:From (en bij een Mail:User ook Mail:Password) is niet ingesteld.");
}

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

// Posting a password counts; opening the form does not.
static RateLimitPartition<string> Aanmeldpogingen(HttpContext context, int perVijfMinuten)
    => HttpMethods.IsPost(context.Request.Method)
        ? RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "onbekend",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = perVijfMinuten, Window = TimeSpan.FromMinutes(5) })
        : RateLimitPartition.GetNoLimiter("formulier");
