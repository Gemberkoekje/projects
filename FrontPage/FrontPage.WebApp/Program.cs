using FrontPage.application.Ports;
using FrontPage.infrastructure;
using FrontPage.WebApp;
using k8s;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

// Read at boot so a bad Apps entry stops the pod instead of showing every tool as down.
var settings = ClusterSettings.Read(builder.Configuration);
var refreshInterval = ClusterSettings.RefreshInterval(builder.Configuration);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(settings);

// The Kubernetes API: $KUBECONFIG or ~/.kube/config when present (local runs), otherwise the pod's
// own ServiceAccount (read-only, see DESIGN.md).
builder.Services.AddSingleton<IKubernetes>(_ => new Kubernetes(KubernetesClientConfiguration.BuildDefaultConfig()));
builder.Services.AddSingleton<KubernetesClusterFactsReader>();
builder.Services.AddSingleton<IClusterFactsReader>(sp => sp.GetRequiredService<KubernetesClusterFactsReader>());

// One summary, refreshed in the background; requests only read it.
builder.Services.AddSingleton<ClusterStatus>();
builder.Services.AddHostedService(sp => new ClusterStatusRefresher(
    sp.GetRequiredService<ClusterStatus>(),
    refreshInterval,
    sp.GetRequiredService<TimeProvider>()));

var app = builder.Build();

app.Use(SecurityHeaders.Add);

// The page is static: index.html, its stylesheet, script, icon and fonts. The fonts never change
// under the same name; everything else revalidates, so a new image shows at once.
app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = file => file.Context.Response.Headers.CacheControl =
        file.File.Name.EndsWith(".woff2", StringComparison.Ordinal) ? "public, max-age=31536000, immutable" : "no-cache",
});

app.MapGet("/api/cluster", (ClusterStatus status, HttpContext context) =>
{
    context.Response.Headers.CacheControl = "no-cache";
    return Results.Json(status.Current, ApiJson.Options);
});

app.MapGet("/healthz", () => "ok");

await app.RunAsync();
