namespace HackerMinigames.Api.Models;

/// <summary>
/// A connected player. Identity lives in the browser's sessionStorage for the
/// PoC, so refreshing a tab yields a brand-new player. Colour is used purely
/// for attribution (who last touched a cell/dial/node) and never to restrict
/// access — any player can interact with any part of any board.
/// </summary>
public record Player(string Id, string Colour);

/// <summary>Response from POST /api/session.</summary>
public record SessionResponse(string PlayerId, string Colour);
