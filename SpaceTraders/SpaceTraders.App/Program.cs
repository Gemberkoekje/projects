using Serilog;
using Serilog.Formatting.Compact;
using SpaceTraders.App.Services;
using SpaceTraders.Infrastructure.Persistence;
using SpaceTraders.Infrastructure.Persistence.Seed;

const string PathBase = "/spacetraders";

var builder = WebApplication.CreateBuilder(args);

if (builder.Environment.IsDevelopment())
{
    builder.Configuration.AddUserSecrets<Program>(optional: true, reloadOnChange: true);
}

builder.Host.UseSerilog((ctx, cfg) =>
{
    if (ctx.HostingEnvironment.IsProduction())
    {
        cfg.WriteTo.Console(new CompactJsonFormatter());
    }
    else
    {
        cfg.WriteTo.Console();
    }

    cfg.ReadFrom.Configuration(ctx.Configuration);
    cfg.Enrich.FromLogContext();
    cfg.Enrich.WithProperty("Application", "SpaceTraders.App");
});

builder.Services.AddPersistence(builder.Configuration);
builder.Services.Configure<InternalApiOptions>(builder.Configuration.GetSection(InternalApiOptions.SectionName));
builder.Services.AddHttpClient<IShipCommandClient, ShipCommandClient>((sp, client) =>
{
    var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<InternalApiOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl);
});
builder.Services.AddRazorPages();

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<SpaceTradersDbContext>();
    await SpaceTradersDatabaseInitializer.InitializeAsync(dbContext);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UsePathBase(PathBase);
app.UseHttpsRedirection();
app.UseRouting();
app.UseAuthorization();
app.MapStaticAssets();
app.MapRazorPages().WithStaticAssets();
await app.RunAsync();
