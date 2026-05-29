using System;
using System.IO;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using TheCuratool.Application;
using TheCuratool.Domain;
using TheCuratool.Infrastructure;
using TheCuratool.Infrastructure.Data;
using TheCuratool.Web;

var builder = WebApplication.CreateBuilder(args);

if (builder.Environment.IsDevelopment())
{
    builder.Configuration.AddUserSecrets<Program>(optional: true);
}

var dataPath = Path.Combine(AppContext.BaseDirectory, "data");
var charactersPath = Path.Combine(dataPath, "characters.json");
var loricsPath = Path.Combine(dataPath, "lorics.json");

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});
builder.Services.AddSingleton(CharacterDatabase.LoadFromFile(charactersPath));
builder.Services.AddSingleton(LoricDatabase.LoadFromFile(loricsPath));
builder.Services.AddSingleton<ScriptParser>();
builder.Services.AddSingleton<SetupCalculator>();
builder.Services.AddScoped<DraftEngine>();
builder.Services.AddScoped<DraftSessionState>();
builder.Services.AddCuratoolInfrastructure(builder.Configuration);
builder.Services.AddProblemDetails();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddRazorComponents().AddInteractiveServerComponents();

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    // Trust all proxies in the cluster; restrict further if the ingress IP range is known.
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

var postgresConnectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? "Host=localhost;Port=5432;Database=curatool;Username=postgres;Password=postgres";
builder.Services.AddHealthChecks().AddNpgSql(postgresConnectionString);

var app = builder.Build();

// Must be first — rewrites Request.Scheme to https based on X-Forwarded-Proto
// set by the Kubernetes ingress controller before any other middleware sees it.
app.UseForwardedHeaders();

app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/curatool", out var remainingPath))
    {
        context.Request.PathBase = context.Request.PathBase.Add("/curatool");
        context.Request.Path = remainingPath.HasValue ? remainingPath : "/";
    }

    await next();
});

if (app.Configuration.GetValue<bool>("Database:AutoMigrate"))
{
    using var scope = app.Services.CreateScope();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    var dbContext = scope.ServiceProvider.GetRequiredService<CuratoolDbContext>();

    if (dbContext.Database.IsRelational())
    {
        try
        {
            await dbContext.Database.MigrateAsync();
        }
        catch (Exception migrationException)
        {
            logger.LogWarning(migrationException, "Database auto-migration failed during startup. Attempting EnsureCreated fallback.");

            try
            {
                await dbContext.Database.EnsureCreatedAsync();
            }
            catch (Exception ensureCreatedException)
            {
                logger.LogError(ensureCreatedException, "Database EnsureCreated fallback failed during startup. Continuing without blocking app launch.");
            }
        }
    }
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler();
    // HSTS and HTTPS redirection are handled by the ingress; skip them here.
}

app.UsePathBase("/curatool");
app.UseRouting();

app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.RoutePrefix = "swagger";
    options.SwaggerEndpoint("v1/swagger.json", "TheCuratool API v1");
});

app.UseStaticFiles();
app.UseAntiforgery();

app.MapCuratoolApi();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.MapHealthChecks("/healthz");

app.Run();

public partial class Program;
