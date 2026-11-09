using DatumPrikker.infrastructure.Adapters;
using DatumPrikker.WebApp;
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

// The content root is the app's own directory, not whatever working directory it was started from
// (as in Kluskat: otherwise static assets come back as empty 200s).
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});

// Behind the cluster's nginx ingress, which terminates TLS. Without this the OAuth redirect URI
// would say http:// and the rate limit would see the ingress instead of the visitor.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

var connectionString = builder.Configuration.GetConnectionString("Postgres");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("Connection string 'Postgres' is not set.");
}

// Marten as a document store only (D2): polls, answers and the data-protection keys.
builder.Services.AddMarten(options =>
{
    options.Connection(connectionString);
    options.AutoCreateSchemaObjects = JasperFx.AutoCreate.CreateOrUpdate;

    // Enums by name, so inserting a member never silently changes stored answers.
    options.UseSystemTextJsonForSerialization(EnumStorage.AsString);

    options.Schema.For<Poll>().Identity(x => x.Id).UniqueIndex(x => x.ShareToken).Index(x => x.OwnerAccountId);
    options.Schema.For<Answer>().Identity(x => x.Id).Index(x => x.PollId).Index(x => x.Email);
    options.Schema.For<DataProtectionKey>().Identity(x => x.Id);
});

// The key ring lives in the database: the pod has no volume, and a fresh ring on every deploy
// would sign everybody out and break half-filled forms.
builder.Services.AddDataProtection().SetApplicationName("datumprikker");
builder.Services.AddSingleton<IConfigureOptions<KeyManagementOptions>>(sp =>
    new ConfigureOptions<KeyManagementOptions>(options =>
        options.XmlRepository = new MartenXmlRepository(sp.GetRequiredService<IDocumentStore>())));

// A cookie, filled by Google or Microsoft when configured (D5), or by the dev login in Development.
var authentication = builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Aanmelden";
        options.LogoutPath = "/Afmelden";
        options.AccessDeniedPath = "/Aanmelden";
        options.Cookie.Name = "datumprikker";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.ExpireTimeSpan = TimeSpan.FromDays(60);
        options.SlidingExpiration = true;
    });

foreach (var provider in LoginProviders.Configured(builder.Configuration))
{
    var clientId = builder.Configuration[$"Authentication:{provider.Scheme}:ClientId"] ?? string.Empty;
    var clientSecret = builder.Configuration[$"Authentication:{provider.Scheme}:ClientSecret"] ?? string.Empty;

    if (provider == LoginProviders.Google)
    {
        authentication.AddGoogle(provider.Scheme, options =>
        {
            options.ClientId = clientId;
            options.ClientSecret = clientSecret;
            options.Events.OnCreatingTicket = context =>
            {
                AccountClaims.Stamp(context.Identity, provider);
                return Task.CompletedTask;
            };
        });
    }
    else
    {
        authentication.AddMicrosoftAccount(provider.Scheme, options =>
        {
            options.ClientId = clientId;
            options.ClientSecret = clientSecret;
            options.Events.OnCreatingTicket = context =>
            {
                AccountClaims.Stamp(context.Identity, provider);
                return Task.CompletedTask;
            };
        });
    }
}

// Every page is closed unless it says [AllowAnonymous]: answering is open, making is not.
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

// Anonymous answering is the one thing a stranger can repeat at will (D11).
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(RateLimits.Answers, context => HttpMethods.IsPost(context.Request.Method)
        ? RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "onbekend",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = RateLimits.AnswersPerWindow, Window = RateLimits.Window })
        : RateLimitPartition.GetNoLimiter("lezen"));
});

builder.Services.AddRazorPages()
    .AddMvcOptions(options => options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true);

// Persistence: the stores are scoped over the singleton IDocumentStore, as in Kluskat.
builder.Services.AddScoped<MartenPollStore>();
builder.Services.AddScoped<IPollStore>(sp => sp.GetRequiredService<MartenPollStore>());
builder.Services.AddScoped<MartenAnswerStore>();
builder.Services.AddScoped<IAnswerStore>(sp => sp.GetRequiredService<MartenAnswerStore>());

// Command handlers.
builder.Services.AddScoped<CreatePollHandler>();
builder.Services.AddScoped<GiveAnswerHandler>();
builder.Services.AddScoped<ClosePollHandler>();

// Query handlers.
builder.Services.AddScoped<GetPollHandler>();
builder.Services.AddScoped<GetResultsHandler>();

var app = builder.Build();

app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Fout");
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/Fout", "?code={0}");

app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets().AllowAnonymous();
app.MapRazorPages().WithStaticAssets();
app.MapGet("/healthz", () => "ok").AllowAnonymous();

await app.RunAsync();
