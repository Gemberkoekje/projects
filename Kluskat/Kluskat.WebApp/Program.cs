using JasperFx.Events;
using Kluskat.application.Ports;
using Kluskat.core.Events;
using Kluskat.infrastructure.Adapters.Application;
using Kluskat.infrastructure.Adapters.DataProtection;
using Kluskat.infrastructure.MartenDb;
using Kluskat.infrastructure.Services;
using Kluskat.WebApp.Pages;
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

// Behind the cluster's nginx ingress, which terminates TLS.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

var connectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException("Connection string 'Postgres' not found.");

// The one password. Read here only to fail at boot rather than at the first sign-in; the sign-in
// page reads it again at its own read site.
if (string.IsNullOrWhiteSpace(builder.Configuration["Auth:Password"]))
{
    throw new InvalidOperationException("Auth:Password is not set; without it nobody can sign in.");
}

// Marten is both the event store (the klus streams) and the document store (read models, the
// sessie, the completion log and the data-protection keys).
builder.Services.AddMarten(options =>
{
    options.Connection(connectionString);
    options.AutoCreateSchemaObjects = JasperFx.AutoCreate.CreateOrUpdate;

    // Enums by name: the stream is kept forever and an integer would silently change meaning the
    // day somebody inserts a member.
    options.UseSystemTextJsonForSerialization(EnumStorage.AsString);

    options.Events.AddEventTypes(
    [
        typeof(KlusToegevoegd),
        typeof(KlusAangepast),
        typeof(KlusGedaan),
        typeof(KlusGedaanTeruggedraaid),
        typeof(KlusOvergeslagen),
        typeof(KlusGeschrapt),
    ]);

    options.Schema.For<KlusOverzicht>().Identity(x => x.Id);
    options.Schema.For<Voltooiing>().Identity(x => x.Id);
    options.Schema.For<Sessie>().Identity(x => x.Id);
    options.Schema.For<Profiel>().Identity(x => x.Id);
    options.Schema.For<DataProtectionSleutel>().Identity(x => x.Id);
});

// Data protection. The pod has no volume, so without a persisted ring every image bump would hand
// out fresh keys and sign you out of a cookie that is meant to last a year.
builder.Services.AddDataProtection().SetApplicationName("kluskat");
builder.Services.AddSingleton<IConfigureOptions<KeyManagementOptions>>(sp =>
    new ConfigureOptions<KeyManagementOptions>(options =>
        options.XmlRepository = new MartenXmlRepository(sp.GetRequiredService<IDocumentStore>())));

// One user, one password, signed in for a year on each device. Sliding, so a phone that is used
// every few days never asks again.
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Aanmelden";
        options.Cookie.Name = "kluskat";
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

// Persistence: the event store is stateless over the singleton IDocumentStore; everything above it
// is scoped, as in the template.
builder.Services.AddSingleton<MartenEventStore>();
builder.Services.AddSingleton<IEventStore<Uuid>>(sp => sp.GetRequiredService<MartenEventStore>());
builder.Services.AddScoped<KlusRepository>();
builder.Services.AddScoped<MartenKlusRepository>();
builder.Services.AddScoped<IKlusRepository>(sp => sp.GetRequiredService<MartenKlusRepository>());
builder.Services.AddScoped<MartenKlusOverzichtStore>();
builder.Services.AddScoped<IKlusOverzichtStore>(sp => sp.GetRequiredService<MartenKlusOverzichtStore>());
builder.Services.AddScoped<MartenSessieStore>();
builder.Services.AddScoped<ISessieStore>(sp => sp.GetRequiredService<MartenSessieStore>());
builder.Services.AddScoped<MartenVoltooiingStore>();
builder.Services.AddScoped<IVoltooiingStore>(sp => sp.GetRequiredService<MartenVoltooiingStore>());
builder.Services.AddScoped<MartenProfielStore>();
builder.Services.AddScoped<IProfielStore>(sp => sp.GetRequiredService<MartenProfielStore>());

// Command handlers.
builder.Services.AddScoped<KlusGedaanMeldenHandler>();
builder.Services.AddScoped<KlusOverslaanHandler>();
builder.Services.AddScoped<GedaanTerugdraaienHandler>();
builder.Services.AddScoped<OvergeslagenVrijgevenHandler>();
builder.Services.AddScoped<KlusToevoegenHandler>();
builder.Services.AddScoped<KlusAanpassenHandler>();
builder.Services.AddScoped<KlusSchrappenHandler>();
builder.Services.AddScoped<KlusGedaanOpHandler>();
builder.Services.AddScoped<StartvullingToepassenHandler>();
builder.Services.AddScoped<AccessoireKiezenHandler>();

// Query handlers.
builder.Services.AddScoped<VolgendeKlusOphalenHandler>();
builder.Services.AddScoped<OverwinningOphalenHandler>();
builder.Services.AddScoped<KlussenOphalenHandler>();
builder.Services.AddScoped<KlusOphalenHandler>();
builder.Services.AddScoped<ProfielOphalenHandler>();

builder.Services.AddScoped<StartvullingService>();

var app = builder.Build();

// Seed the chore list on the very first start; a no-op ever after.
using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<StartvullingService>().ToepassenAsync();
}

app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Fout");
    app.UseHsts();
}

app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

// The sign-in page, the manifest and the icons must load before anyone is signed in.
app.MapStaticAssets().AllowAnonymous();
app.MapRazorPages().WithStaticAssets();
app.MapGet("/healthz", () => "ok").AllowAnonymous();

await app.RunAsync();
