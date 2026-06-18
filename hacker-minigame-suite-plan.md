# Hacker Minigame Suite — PoC Plan

## Overview

A suite of 7 multiplayer hacker-themed minigames playable in the browser,
designed so multiple players can work on the same board simultaneously.
Each browser tab is a distinct player for PoC purposes.

**Stack:** ASP.NET Core (.NET 10), SignalR for real-time sync, Blazor Server or
minimal API + vanilla JS frontend (see Frontend note below), deployed to
Kubernetes via existing cluster.

---

## Architecture

### Real-time sync

Use **ASP.NET Core SignalR** for all state synchronisation.

- Each game session has a `gameId` (GUID, generated on board creation).
- Players join a SignalR group keyed by `gameId`.
- All state mutations go through the server: client sends an intent
  (`ClaimPartition`, `MakeMove`, `ReleaseLock`), server validates, applies,
  and broadcasts the new state to all group members.
- This eliminates last-write-wins races that a localStorage approach would have.

### Player identity

- On load, the client POSTs to `/api/session` and receives a `playerId` (GUID)
  and a randomly assigned display colour.
- `playerId` is stored in `sessionStorage` — refreshing gives a new player.
- The server tracks active players per session (in-memory for PoC; replace with
  Redis for production).

### Claim mechanic (partitioned games)

All partitioned games share the same claim flow:

1. Client sends `ClaimPartition(gameId, partitionId, playerId)`.
2. Server checks: if `partition.ClaimedBy == null`, assign it and broadcast.
   If already claimed, return a rejection.
3. This is atomic on the server — no race condition possible.
4. A claimed partition is visually highlighted in all connected clients with
   the claiming player's colour.
5. Unclaiming happens on disconnect (SignalR `OnDisconnectedAsync`) or
   explicit release.

### Win condition

After every move, the server evaluates the board for completion.
If complete, it broadcasts a `GameSolved` event to all group members.
All clients show a shared victory banner.

### State persistence

In-memory for PoC (a `ConcurrentDictionary<Guid, GameState>` on the hub or
a singleton service). Games expire after 1 hour of inactivity.
No database needed for PoC.

---

## Project Structure

```
HackerMinigames/
├── HackerMinigames.sln
├── src/
│   ├── HackerMinigames.Api/          # ASP.NET Core host
│   │   ├── Program.cs
│   │   ├── Hubs/
│   │   │   └── GameHub.cs            # SignalR hub (all games share one hub)
│   │   ├── Services/
│   │   │   └── GameSessionService.cs # In-memory game state manager
│   │   ├── Models/                   # Shared DTOs and game state records
│   │   └── wwwroot/                  # Static frontend files
│   │       ├── index.html            # Menu / game picker
│   │       └── games/
│   │           ├── cipher-wheel.html
│   │           ├── packet-sequencer.html
│   │           ├── memory-leak.html
│   │           ├── permission-tree.html
│   │           ├── circuit-tracer.html
│   │           ├── frequency-tuner.html
│   │           └── signal-reconstruction.html
└── k8s/
    ├── deployment.yaml
    └── service.yaml
```

**Frontend note:** Plain HTML + vanilla JS with the SignalR JS client
(`@microsoft/signalr` from CDN). No Blazor, no npm build step — keeps the PoC
friction-free. Each game is a self-contained HTML file.

---

## Games

### 1. Cipher Wheel *(easiest — build first)*

**Concept:** A set of encoded words is displayed. Each word has per-letter
alphabet wheels. Players claim a word and rotate the wheels to decode it.

**Partition:** One word per player.

**State shape:**

```csharp
record CipherWheelState(
    List<CipherWord> Words,
    bool Solved
);

record CipherWord(
    string Id,
    string EncodedText,
    int[] Offsets,           // per-letter rotation (0–25)
    string? ClaimedBy,
    string? SolvedBy
);
```

**Server events (client → server):**
- `ClaimWord(gameId, wordId, playerId)`
- `SetOffset(gameId, wordId, letterIndex, offset, playerId)`

**Server events (server → client):**
- `WordClaimed(wordId, playerId, colour)`
- `OffsetChanged(wordId, letterIndex, offset)`
- `GameSolved()`

**Completion check:** All words have all offsets producing the target plaintext.

**Sync risk:** Claim collision — handled atomically server-side.

**Multi-tab test:** Two tabs claim the same word simultaneously; verify only
one succeeds and the other receives a rejection.

---

### 2. Packet Sequencer

**Concept:** N columns of shuffled code-line fragments. Each column is an
independent stream. Player claims a column and drags lines into the correct
order.

**Partition:** One column per player.

**State shape:**

```csharp
record PacketSequencerState(
    List<PacketColumn> Columns,
    bool Solved
);

record PacketColumn(
    string Id,
    List<string> Items,     // current order (shuffled)
    List<string> Solution,  // correct order
    string? ClaimedBy
);
```

**Server events (client → server):**
- `ClaimColumn(gameId, columnId, playerId)`
- `MoveItem(gameId, columnId, fromIndex, toIndex, playerId)`

**Server events (server → client):**
- `ColumnClaimed(columnId, playerId, colour)`
- `ColumnUpdated(columnId, items)`
- `GameSolved()`

**Completion check:** `Items.SequenceEqual(Solution)` for all columns.

**Sync risk:** Claim collision.

**Multi-tab test:** Two tabs claim the same column simultaneously; also verify
that reordering in one tab is reflected in the other tab's view of that column
(read-only mirror for non-owners).

---

### 3. Memory Leak *(shared board — no claiming)*

**Concept:** A grid of hex/binary values. Some cells violate a parity rule
(row XOR should equal zero). Any player can click a cell to patch it. All
contributions are additive.

**Partition:** None — open board.

**State shape:**

```csharp
record MemoryLeakState(
    int Rows,
    int Cols,
    bool[] Patched,         // flat array [row * cols + col]
    string?[] PatchedBy,    // who patched each cell
    bool Solved
);
```

**Server events (client → server):**
- `PatchCell(gameId, row, col, playerId)`

**Server events (server → client):**
- `CellPatched(row, col, playerId, colour)`
- `GameSolved()`

**Completion check:** All cells with parity violations are patched.

**Sync risk:** Two tabs patch the same cell simultaneously — idempotent on the
server (second patch is a no-op), so safe. Last writer wins for `PatchedBy`
attribution.

**Multi-tab test:** Two tabs each patch different cells rapidly; verify both
appear in the other tab within ~100 ms. Deliberately patch the same cell from
two tabs and verify no error.

---

### 4. Permission Tree

**Concept:** A fake filesystem tree where nodes carry permission flags (e.g.
`rwxr--r--`). Some flags are intentionally wrong. Players claim a top-level
branch (subtree) and toggle flags to match the expected values.

**Partition:** One top-level branch per player.

**State shape:**

```csharp
record PermissionTreeState(
    List<FileNode> Roots,  // top-level branches
    bool Solved
);

record FileNode(
    string Id,
    string Path,
    string CurrentFlags,
    string ExpectedFlags,
    string? ClaimedBy,
    List<FileNode> Children
);
```

**Server events (client → server):**
- `ClaimBranch(gameId, rootNodeId, playerId)`
- `ToggleFlag(gameId, nodeId, flagIndex, playerId)`

**Server events (server → client):**
- `BranchClaimed(rootNodeId, playerId, colour)`
- `NodeUpdated(nodeId, currentFlags)`
- `GameSolved()`

**Completion check:** All nodes have `CurrentFlags == ExpectedFlags`.

**Sync risk:** Branch claim collision; also verify cross-branch independence
(fixing a node in branch A does not mutate branch B).

**Multi-tab test:** Claim same branch from two tabs; fix nodes in different
branches simultaneously and verify correct isolated updates.

---

### 5. Circuit Tracer

**Concept:** A grid of cells containing logic gates (AND, OR, NOT, WIRE).
Input signals enter from the left; output nodes sit on the right. Each signal
chain is a coloured thread — players claim a thread and verify/fix the gate
connections along it.

**Partition:** One signal chain per player.

**State shape:**

```csharp
record CircuitTracerState(
    int Rows,
    int Cols,
    List<GateCell> Cells,
    List<SignalChain> Chains,
    bool Solved
);

record GateCell(string Id, int Row, int Col, GateType Type);
record SignalChain(string Id, List<string> CellIds, bool ExpectedOutput,
                   string? ClaimedBy);

enum GateType { Wire, And, Or, Not, Input, Output }
```

**Server events (client → server):**
- `ClaimChain(gameId, chainId, playerId)`
- `SetGate(gameId, cellId, GateType newType, playerId)`

**Server events (server → client):**
- `ChainClaimed(chainId, playerId, colour)`
- `CellUpdated(cellId, newType)`
- `GameSolved()`

**Completion check:** Evaluate all chains; each output node must match its
`ExpectedOutput` given the current gate configuration.

**Sync risk:** Chain claim collision; gate evaluation must be server-side to
prevent desync.

**Multi-tab test:** Two tabs claim the same chain simultaneously; fix gates in
parallel chains and verify isolated evaluation.

---

### 6. Frequency Tuner

**Concept:** An array of dials, each controlling a frequency parameter. A
target waveform is displayed. Players nudge dials toward their target values
until the reconstructed waveform matches.

**Partition:** Soft — no hard claims. Last player to touch a dial "owns" it
visually (shown with their colour), but any player can move any dial.

**State shape:**

```csharp
record FrequencyTunerState(
    List<Dial> Dials,
    bool Solved
);

record Dial(
    string Id,
    double CurrentValue,    // 0.0–1.0
    double TargetValue,
    double Tolerance,
    string? LastTouchedBy
);
```

**Server events (client → server):**
- `SetDial(gameId, dialId, value, playerId)`

**Server events (server → client):**
- `DialUpdated(dialId, value, playerId, colour)`
- `GameSolved()`

**Completion check:** `Math.Abs(dial.CurrentValue - dial.TargetValue) <= dial.Tolerance`
for all dials.

**Waveform rendering:** Computed client-side from current dial values using a
simple sum-of-sines formula. Server only stores dial values.

**Sync risk:** Two tabs moving the same dial causes rapid `DialUpdated` events
— last-write-wins produces jitter. Acceptable for PoC; in production, add
client-side debounce and server-side rate limiting per dial.

**Multi-tab test:** Both tabs drag the same dial simultaneously; observe and
document the jitter behaviour. Verify that dials moved in separate tabs do not
interfere with each other.

---

### 7. Signal Reconstruction *(hardest — build last)*

**Concept:** A waveform or image is sliced into vertical columns and shuffled.
Players drag slices into the correct positions collaboratively. A player must
"hold" a slice while dragging it, preventing others from moving it.

**Partition:** Temporary per-slice lock during drag.

**State shape:**

```csharp
record SignalReconstructionState(
    List<SignalSlice> Slices,
    bool Solved
);

record SignalSlice(
    string Id,
    int CorrectPosition,
    int CurrentPosition,
    string? PlacedBy,
    string? HeldBy,          // non-null while a player is dragging this slice
    DateTime? HeldSince      // for timeout cleanup
);
```

**Server events (client → server):**
- `GrabSlice(gameId, sliceId, playerId)` — acquire lock
- `PlaceSlice(gameId, sliceId, targetPosition, playerId)` — drop and release lock
- `ReleaseSlice(gameId, sliceId, playerId)` — cancel drag (e.g. on disconnect)

**Server events (server → client):**
- `SliceGrabbed(sliceId, playerId, colour)`
- `SlicePlaced(sliceId, targetPosition, playerId, colour)`
- `SliceReleased(sliceId)`
- `GameSolved()`

**Lock timeout:** Server runs a background `Timer` (or hosted service) that
releases any slice held for more than 10 seconds without a `PlaceSlice` event.
This handles tab crashes and network drops.

**Completion check:** All slices have `CurrentPosition == CorrectPosition`.

**Sync risk:** Two tabs attempt `GrabSlice` on the same slice simultaneously —
server grants to first arrival, rejects second. The temporary lock adds
significant complexity compared to other games.

**Multi-tab test:** Two tabs attempt to grab the same slice simultaneously;
verify only one succeeds. Crash a tab while holding a slice; verify the lock
is released after the timeout.

---

## Build Order

| # | Game | Complexity | Validates |
|---|------|------------|-----------|
| 1 | Cipher Wheel | Low | Claim → move → win pipeline end-to-end |
| 2 | Packet Sequencer | Low | Same pipeline, drag-reorder UI |
| 3 | Memory Leak | Low | Shared-board additive pattern |
| 4 | Permission Tree | Medium | Tree rendering, flag toggling |
| 5 | Circuit Tracer | Medium | Server-side logic evaluation |
| 6 | Frequency Tuner | Medium | Soft ownership, waveform render |
| 7 | Signal Reconstruction | High | Temporary lock + timeout cleanup |

---

## Kubernetes Deployment

Minimal setup — single deployment, single service, ingress via existing
wildcard DNS (`*.gemberkoekje.nl`).

```yaml
# k8s/deployment.yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: hacker-minigames
spec:
  replicas: 1          # Keep at 1 for PoC — SignalR in-memory state is not shared across pods
  selector:
    matchLabels:
      app: hacker-minigames
  template:
    metadata:
      labels:
        app: hacker-minigames
    spec:
      containers:
        - name: api
          image: ghcr.io/<your-repo>/hacker-minigames:latest
          ports:
            - containerPort: 8080
          env:
            - name: ASPNETCORE_ENVIRONMENT
              value: Production
```

> **Important:** Keep `replicas: 1` while using in-memory state. If you ever
> scale to multiple replicas, add Redis backplane for SignalR
> (`AddStackExchangeRedis`) and move game state to Redis or a database.

---

## SignalR Hub Skeleton

```csharp
// Hubs/GameHub.cs
public class GameHub : Hub
{
    private readonly GameSessionService _sessions;

    public GameHub(GameSessionService sessions) => _sessions = sessions;

    public override async Task OnConnectedAsync()
    {
        // Player joins by navigating to /{gameType}/{gameId}
        // gameId passed as query param on connect
        var gameId = Context.GetHttpContext()?.Request.Query["gameId"].ToString();
        if (!string.IsNullOrEmpty(gameId))
            await Groups.AddToGroupAsync(Context.ConnectionId, gameId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        // Release all claims and held locks for this connection
        var released = _sessions.ReleaseAll(Context.ConnectionId);
        foreach (var (gameId, evt) in released)
            await Clients.Group(gameId).SendAsync("PartitionReleased", evt);
        await base.OnDisconnectedAsync(exception);
    }

    public async Task ClaimPartition(string gameId, string partitionId, string playerId)
    {
        var result = _sessions.TryClaim(gameId, partitionId, playerId, Context.ConnectionId);
        if (result.Success)
            await Clients.Group(gameId).SendAsync("PartitionClaimed", partitionId, playerId, result.Colour);
        else
            await Clients.Caller.SendAsync("ClaimRejected", partitionId);
    }

    // Each game adds its own move methods following the same broadcast pattern
}
```

---

## Things to Test with Multiple Tabs

| Test | Game(s) | What to verify |
|------|---------|----------------|
| Claim collision | All partitioned games | Only one tab wins; other receives rejection |
| Parallel moves on different partitions | All partitioned games | Moves are isolated; no cross-contamination |
| Shared board rapid patching | Memory Leak | Both tabs' patches appear in each other within ~100 ms |
| Same-cell double patch | Memory Leak | No error; idempotent; attribution goes to last writer |
| Simultaneous dial movement | Frequency Tuner | Document jitter behaviour |
| Slice grab collision | Signal Reconstruction | One tab wins lock; other is rejected |
| Slice lock timeout | Signal Reconstruction | Crash a tab mid-drag; slice released after 10 s |
| Win triggered by any tab | All games | Final move in tab A shows victory banner in tab B |
| Disconnect/reconnect | All games | Reconnecting tab receives current board state |
