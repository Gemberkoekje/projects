using System;
using System.Threading.RateLimiting;
using System.Threading.Tasks;
using DatumPrikker.ApiService;
using DatumPrikker.ApiService.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();
builder.AddRedisOutputCache("cache");

// Add services to the container.
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.AddNpgsqlDbContext<DatumPrikkerDbContext>("datumprikker");
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("anonymous-respond", context =>
    {
        if (context.Request.Headers.ContainsKey("X-Respondent-IdentityId"))
        {
            return RateLimitPartition.GetNoLimiter("authenticated");
        }

        var ipAddress = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var shareToken = context.Request.RouteValues.TryGetValue("shareToken", out var shareTokenValue)
            ? shareTokenValue?.ToString() ?? "unknown"
            : "unknown";
        var partitionKey = $"{ipAddress}:{shareToken}";

        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 30,
                Window = TimeSpan.FromMinutes(5),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0,
            });
    });
});

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseExceptionHandler();
app.UseRateLimiter();
app.UseOutputCache();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet("/", () => "API service is running.");
app.MapPollsEndpoints();

app.MapDefaultEndpoints();

await ApplyMigrationsAsync(app.Services);

await app.RunAsync();

static async Task ApplyMigrationsAsync(IServiceProvider services)
{
    await using var scope = services.CreateAsyncScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<DatumPrikkerDbContext>();
    await dbContext.Database.MigrateAsync();
}
