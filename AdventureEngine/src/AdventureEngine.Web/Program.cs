using AdventureEngine.Infrastructure;
using AdventureEngine.Web.Components;

var builder = WebApplication.CreateBuilder(args);

if (builder.Environment.IsDevelopment())
{
    builder.Configuration.AddUserSecrets<Program>(optional: true);
}

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddAdventureEngineInfrastructure(builder.Configuration);

var postgresConnectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException("Connection string 'Postgres' was not found. Configure it via user secrets or environment variables.");
builder.Services.AddHealthChecks().AddNpgSql(postgresConnectionString);

var app = builder.Build();

app.UsePathBase("/adventureengine");

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.MapHealthChecks("/healthz");

app.Run();
