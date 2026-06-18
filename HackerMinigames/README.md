# Hacker Minigame Suite

A suite of multiplayer, hacker-themed browser minigames. Every browser tab is a
distinct player; all game state is synced in real time over SignalR so multiple
players can work the same board at once. See
[`../hacker-minigame-suite-plan.md`](../hacker-minigame-suite-plan.md) for the
full design.

**Stack:** ASP.NET Core (.NET 10) · SignalR · plain HTML + vanilla JS · Kubernetes.

## Run locally

```bash
dotnet run --project src/HackerMinigames.Api
```

Then open the printed URL. Open it again in a second tab (or share the in-game
URL) to play collaboratively.

## Status

This is the **initial scaffold**. The foundation is in place and the first game
is fully wired end-to-end:

| # | Game | Status |
|---|------|--------|
| 1 | Cipher Wheel | ✅ Playable — validates the move → broadcast → win pipeline |
| 2 | Packet Sequencer | ⬜ Planned |
| 3 | Memory Leak | ⬜ Planned |
| 4 | Permission Tree | ⬜ Planned |
| 5 | Circuit Tracer | ⬜ Planned |
| 6 | Frequency Tuner | ⬜ Planned |
| 7 | Signal Reconstruction | ⬜ Planned (adds the grab-lock mechanic) |

### Adding the next game

Each game follows the same pattern:

1. Add its state records to `src/HackerMinigames.Api/Models/`.
2. Add board generation + an `Apply…` move handler to `GameSessionService`.
3. Add the matching intent method to `GameHub` (validate → apply → broadcast).
4. Add a self-contained `wwwroot/games/<slug>.html` and flip `ready:true` in
   the menu (`wwwroot/index.html`).

## Architecture notes

- **No client authority.** Clients send intents; the server validates, applies,
  and broadcasts. This removes last-write-wins races.
- **In-memory state** (`GameSessionService` singleton). Keep Kubernetes at
  `replicas: 1`. To scale, add a Redis SignalR backplane and move state to Redis.
- **Per-tab identity.** `POST /api/session` issues a `playerId` + colour stored
  in `sessionStorage`; refreshing a tab makes a new player.

## Deploy

```bash
docker build -t ghcr.io/<your-repo>/hacker-minigames:latest .
kubectl apply -f k8s/
```
