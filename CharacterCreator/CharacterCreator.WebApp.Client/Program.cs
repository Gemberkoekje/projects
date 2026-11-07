using System.Net.Http;
using CharacterCreator.WebApp.Client.Services;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace CharacterCreator.WebApp.Client;

/// <summary>The browser's composition root.</summary>
/// <remarks>
/// An explicit class in this project's namespace rather than top-level statements: those would make
/// a second public global <c>Program</c>, next to the host's, and the specs' WebApplicationFactory
/// could no longer tell them apart.
/// The server prerenders every page first, so whatever a page here injects must be registered both
/// here and in the WebApp's Program.cs. Components the page shows only once it runs in the browser
/// (the build screen itself) are registered here only.
/// </remarks>
internal static class Program
{
    /// <summary>Starts the client in the browser.</summary>
    /// <param name="args">The arguments the runtime passes.</param>
    /// <returns>The running host.</returns>
    public static async Task Main(string[] args)
    {
        var builder = WebAssemblyHostBuilder.CreateDefault(args);

        // In the browser one scope lives as long as the tab, so Scoped is the tab's lifetime.
        builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
        builder.Services.AddScoped<Browser>();
        builder.Services.AddScoped<IUiLanguage>(sp => sp.GetRequiredService<Browser>());
        builder.Services.AddScoped<RulesetClient>();
        builder.Services.AddScoped<PlanStore>();

        await builder.Build().RunAsync();
    }
}
