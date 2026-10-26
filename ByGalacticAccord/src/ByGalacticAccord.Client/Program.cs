using ByGalacticAccord.Client;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

// The browser client (design §18). One root component: the main screen, or the engine speed probe
// when the address asks for it (?bench=N).
WebAssemblyHostBuilder builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<Shell>("#app");
await builder.Build().RunAsync();
