using HackerMinigames.Api.Hubs;
using HackerMinigames.Api.Models;
using HackerMinigames.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSignalR();
builder.Services.AddSingleton<GameSessionService>();

var app = builder.Build();

var configuredPathBase = builder.Configuration["PathBase"];
var pathBase = string.IsNullOrWhiteSpace(configuredPathBase)
    ? Environment.GetEnvironmentVariable("ASPNETCORE_PATHBASE")
    : configuredPathBase;

if (!string.IsNullOrWhiteSpace(pathBase))
{
    app.UsePathBase(pathBase);
}

app.UseDefaultFiles();   // serve wwwroot/index.html at "/"
app.UseStaticFiles();

// Issue a per-tab player identity (stored in sessionStorage on the client).
app.MapPost("/api/session", (GameSessionService sessions) =>
{
    var player = sessions.CreatePlayer();
    return Results.Ok(new SessionResponse(player.Id, player.Colour));
});

// Create a new board of the given type and return its gameId.
app.MapPost("/api/games/{gameType}", (string gameType, GameSessionService sessions) =>
{
    var gameId = sessions.CreateGame(gameType);
    return Results.Ok(new { gameId });
});

// Snapshot a joining tab uses to render the current board state.
app.MapGet("/api/games/{gameId:guid}/cipher-wheel", (Guid gameId, GameSessionService sessions) =>
{
    var state = sessions.GetCipherWheelGame(gameId);
    return state is null ? Results.NotFound() : Results.Ok(state);
});

app.MapGet("/api/games/{gameId:guid}/memory-leak", (Guid gameId, GameSessionService sessions) =>
{
    var state = sessions.GetMemoryLeak(gameId);
    return state is null ? Results.NotFound() : Results.Ok(state);
});

app.MapGet("/api/games/{gameId:guid}/packet-sequencer", (Guid gameId, GameSessionService sessions) =>
{
    var state = sessions.GetPacketSequencer(gameId);
    return state is null ? Results.NotFound() : Results.Ok(state);
});

app.MapHub<GameHub>("/hub");

app.Run();
