using Serilog;
using Serilog.Formatting.Compact;
using SpaceTraders.Infrastructure.Persistence;
using SpaceTraders.Infrastructure.Persistence.Seed;

var builder = WebApplication.CreateBuilder(args);

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
builder.Services.AddRazorPages();

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<SpaceTradersDbContext>();
    await dbContext.Database.EnsureCreatedAsync();
    await DefaultSettingsSeed.SeedAsync(dbContext);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();
app.UseAuthorization();
app.MapStaticAssets();
app.MapRazorPages().WithStaticAssets();
app.Run();
