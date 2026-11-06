using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

namespace CharacterCreator.WebApp.Client;

/// <summary>The browser's composition root.</summary>
/// <remarks>
/// An explicit class in this project's namespace rather than top-level statements: those would make
/// a second public global <c>Program</c>, next to the host's, and the specs' WebApplicationFactory
/// could no longer tell them apart.
/// The server prerenders every page first, so whatever a page here injects must be registered both
/// here and in the WebApp's Program.cs.
/// </remarks>
internal static class Program
{
    /// <summary>Starts the client in the browser.</summary>
    /// <param name="args">The arguments the runtime passes.</param>
    /// <returns>The running host.</returns>
    public static async Task Main(string[] args)
    {
        var builder = WebAssemblyHostBuilder.CreateDefault(args);

        await builder.Build().RunAsync();
    }
}
