using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Sts2Viewer.Data;
using Sts2Viewer.Llm;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();
builder.Services.AddHttpClient("llm");
builder.Services.AddSingleton<PostgresReadService>();
builder.Services.AddSingleton<PostgresWriteService>();
builder.Services.AddSingleton<LlmProviderFactory>();
builder.Services.AddSingleton<PickExplanationService>();

WebApplication app = builder.Build();

app.MapGet("/healthz", () => Results.Ok());

app.UsePathBase("/sts2viewer");

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}

app.UseStaticFiles();
app.UseRouting();
app.MapRazorPages();

app.Run();
