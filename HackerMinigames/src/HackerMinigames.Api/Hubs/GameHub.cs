using HackerMinigames.Api.Services;
using Microsoft.AspNetCore.SignalR;

namespace HackerMinigames.Api.Hubs;

public class GameHub : Hub
{
    private readonly GameSessionService _sessions;

    public GameHub(GameSessionService sessions) => _sessions = sessions;

    public override async Task OnConnectedAsync()
    {
        var gameId = Context.GetHttpContext()?.Request.Query["gameId"].ToString();
        if (!string.IsNullOrEmpty(gameId))
            await Groups.AddToGroupAsync(Context.ConnectionId, gameId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var released = _sessions.ReleaseHeldSlices(Context.ConnectionId);
        foreach (var (gameId, sliceId) in released)
            await Clients.Group(gameId).SendAsync("SliceReleased", sliceId);
        await base.OnDisconnectedAsync(exception);
    }

    // ---- Game 1: Cipher Wheel --------------------------------------------

    /// <summary>
    /// Client sets a wheel to an absolute value (0–25). Server applies it, then
    /// broadcasts the new value + closeness to every tab watching this game.
    /// </summary>
    public async Task SetWheel(string gameId, string sentenceId, string wheelId,
                               int value, string playerId)
    {
        if (!Guid.TryParse(gameId, out var id)) return;

        var r = _sessions.ApplySetWheel(id, sentenceId, wheelId, value, playerId);

        await Clients.Group(gameId).SendAsync("WheelChanged",
            sentenceId, wheelId, value, r.Closeness, r.SentenceSolved, playerId, r.Colour);

        if (r.BoardSolved)
            await Clients.Group(gameId).SendAsync("GameSolved");
    }

    // ---- Game 2: Packet Sequencer ----------------------------------------

    /// <summary>
    /// Client moves an item from <paramref name="fromIndex"/> so it ends up at
    /// <paramref name="toIndex"/> in the final array. Server applies, broadcasts
    /// the authoritative new column order to all tabs.
    /// </summary>
    public async Task MoveItem(string gameId, string columnId,
                               int fromIndex, int toIndex, string playerId)
    {
        if (!Guid.TryParse(gameId, out var id)) return;

        var r = _sessions.ApplyMoveItem(id, columnId, fromIndex, toIndex, playerId);

        await Clients.Group(gameId).SendAsync("ColumnUpdated",
            columnId, r.Items, r.ColumnSolved, playerId, r.Colour);

        if (r.BoardSolved)
            await Clients.Group(gameId).SendAsync("GameSolved");
    }

    // ---- Game 3: Memory Leak (hex XOR nonogram) --------------------------

    public async Task ToggleCell(string gameId, int row, int col, string playerId)
    {
        if (!Guid.TryParse(gameId, out var id)) return;

        var r = _sessions.ApplyToggleCell(id, row, col, playerId);

        await Clients.Group(gameId).SendAsync("CellToggled",
            r.Row, r.Col, r.IsOn, playerId, r.Colour);

        if (r.BoardSolved)
            await Clients.Group(gameId).SendAsync("GameSolved");
    }
}
