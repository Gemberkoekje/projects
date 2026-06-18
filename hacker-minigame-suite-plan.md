# Hacker Minigame Suite â€” PoC Plan

## Overview

A suite of 7 multiplayer hacker-themed minigames playable in the browser,
designed so multiple players can work on the same board simultaneously.
Each browser tab is a distinct player for PoC purposes.

**Stack:** ASP.NET Core (.NET 10), SignalR for real-time sync, plain HTML +
vanilla JS frontend, deployed to Kubernetes via existing cluster.

---

## Architecture

### Real-time sync

Use **ASP.NET Core SignalR** for all state synchronisation.

- Each game session has a `gameId` (GUID, generated on board creation).
- Players join a SignalR group keyed by `gameId`.
- All state mutations go through the server: client sends an intent
  (`MakeMove`), server validates, applies, and broadcasts the new state to all
  group members.
- This eliminates last-write-wins races and keeps all clients consistent.

### No claim mechanic

There is no claiming. Any player can interact with any part of the board at
any time â€” exactly like the existing Frontier minigames (disentangle, minesweeper,
word search, pipe game). If two players touch the same element simultaneously,
the server applies both moves in arrival order and broadcasts each result.
Collisions are rare in practice and harmless â€” last move wins and the board
stays consistent.

The only exception is **Signal Reconstruction**, where a player dragging a
slice needs a soft grab lock to prevent the slice jumping while mid-drag.
All other games have no locking whatsoever.

### Player identity

- On load, the client POSTs to `/api/session` and receives a `playerId` (GUID)
  and a randomly assigned display colour.
- `playerId` is stored in `sessionStorage` â€” refreshing gives a new player.
- The server tracks active players per session (in-memory for PoC; replace with
  Redis for production).
- Player colour is used to show attribution (who last touched a cell/dial/node)
  but never to restrict access.

### Win condition

After every move, the server evaluates the board for completion.
If complete, it broadcasts a `GameSolved` event to all group members.
All clients show a shared victory banner.

### State persistence

In-memory for PoC (a `ConcurrentDictionary<Guid, GameState>` in a singleton
service). Games expire after 1 hour of inactivity. No database needed for PoC.

---

## Project Structure

```
HackerMinigames/
â”œâ”€â”€ HackerMinigames.sln
â”œâ”€â”€ src/
â”‚   â”œâ”€â”€ HackerMinigames.Api/          # ASP.NET Core host
â”‚   â”‚   â”œâ”€â”€ Program.cs
â”‚   â”‚   â”œâ”€â”€ Hubs/
â”‚   â”‚   â”‚   â””â”€â”€ GameHub.cs            # SignalR hub (all games share one hub)
â”‚   â”‚   â”œâ”€â”€ Services/
â”‚   â”‚   â”‚   â””â”€â”€ GameSessionService.cs # In-memory game state manager
â”‚   â”‚   â”œâ”€â”€ Models/                   # Shared DTOs and game state records
â”‚   â”‚   â””â”€â”€ wwwroot/                  # Static frontend files
â”‚   â”‚       â”œâ”€â”€ index.html            # Menu / game picker
â”‚   â”‚       â””â”€â”€ games/
â”‚   â”‚           â”œâ”€â”€ cipher-wheel.html
â”‚   â”‚           â”œâ”€â”€ packet-sequencer.html
â”‚   â”‚           â”œâ”€â”€ memory-leak.html
â”‚   â”‚           â”œâ”€â”€ permission-tree.html
â”‚   â”‚           â”œâ”€â”€ circuit-tracer.html
â”‚   â”‚           â”œâ”€â”€ frequency-tuner.html
â”‚   â”‚           â””â”€â”€ signal-reconstruction.html
â””â”€â”€ k8s/
    â”œâ”€â”€ deployment.yaml
    â””â”€â”€ service.yaml
```

**Frontend note:** Plain HTML + vanilla JS with the SignalR JS client
(`@microsoft/signalr` from CDN). No Blazor, no npm build step â€” keeps the PoC
friction-free. Each game is a self-contained HTML file.

---

## Games

### 1. Cipher Wheel *(easiest â€” build first)*

**Concept:** A set of encoded words is displayed. Each word has per-letter
alphabet wheels. Any player can rotate any wheel on any word to decode it.

**State shape:**

```csharp
record CipherWheelState(
    List<CipherWord> Words,
    bool Solved
);

record CipherWord(
    string Id,
    string EncodedText,
    int[] Offsets,           // per-letter rotation (0â€“25)
    string?[] LastTouchedBy  // per-letter attribution
);
```

**Server events (client â†’ server):**
- `SetOffset(gameId, wordId, letterIndex, offset, playerId)`

**Server events (server â†’ client):**
- `OffsetChanged(wordId, letterIndex, offset, playerId, colour)`
- `GameSolved()`

**Completion check:** All words have all offsets producing the target plaintext.

**Multi-tab test:** Both tabs rotate wheels on the same word simultaneously;
verify both updates land and the board stays consistent. Verify that completing
the last word from either tab triggers `GameSolved` in both.

---

### 2. Packet Sequencer

**Concept:** N columns of shuffled code-line fragments. Each column is an
independent stream. Any player can reorder lines in any column.

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
    string? LastTouchedBy
);
```

**Server events (client â†’ server):**
- `MoveItem(gameId, columnId, fromIndex, toIndex, playerId)`

**Server events (server â†’ client):**
- `ColumnUpdated(columnId, items, playerId, colour)`
- `GameSolved()`

**Completion check:** `Items.SequenceEqual(Solution)` for all columns.

**Multi-tab test:** Two tabs reorder different columns simultaneously; verify
both updates are reflected in each other's view. Two tabs reorder the same
column simultaneously; verify the result is consistent (not corrupted).

---

### 3. Memory Leak *(hex XOR nonogram)*

**Concept:** A technical-looking nonogram. Every cell shows a **fixed hex byte**
that never changes. Players **click a cell to toggle it on/off** (highlighted =
"good / kept", dimmed = "bad / leaked"). Only **highlighted** cells contribute
their byte to the parity maths.

Each row carries an XOR **clue** at its right edge and each column one along the
bottom: the running XOR of the bytes currently highlighted in that line. A line
is satisfied when its clue reads `00`. The board is solved when **every row clue
AND every column clue equals `00`** at the same time. That is the nonogram loop:
toggle cells until all the edge numbers fall to zero. Clues not yet at `00` read
red; clues at `00` read green.

**Pre-defined solvable images (PoC):** boards are **not** randomised. The PoC
ships a handful of hand-authored 8x8 "images" - the binary pattern of cells that
must be highlighted in the solved state. Cell bytes are generated *to fit* the
chosen image so the solution drives every row and column XOR to `00` (see
*Solvability*). Toggling is fully reversible, so there are no dead-ends.

**Solvability (how a board is built):**
1. Author a binary image `Solution[r][c]` (1 = highlighted when solved).
2. Treat the bottom row and right column as a **checksum frame** that is always
   highlighted in the solution; the interior `(R-1) x (C-1)` holds the picture.
3. Assign random bytes to interior cells, then:
   - right-column cell `(r, C-1)` = XOR of highlighted interior bytes in row `r`
     -> row `r` XOR = `00`;
   - bottom-row cell `(R-1, c)` = XOR of highlighted interior bytes in column `c`
     -> column `c` XOR = `00`;
   - corner `(R-1, C-1)` = XOR of the frame bytes -> closes the last row and
     last column at `00`.

   This is classic 2-D parity over bytes (XOR instead of a count): the authored
   image is provably a valid all-zero solution. OFF cells never contribute, so
   their bytes can be anything.

**State shape:**

```csharp
record MemoryLeakState(
    int Rows,
    int Cols,
    int[][] Values,          // fixed hex bytes, fully visible, never change
    bool[][] On,             // current toggle state (shared, the only mutable part)
    string?[][] ToggledBy,   // attribution: who last toggled each cell
    bool Solved
);
// Solution[][] is kept server-side only (generation / optional hints); the win
// check is purely XOR-based and does not need it.
```

**Server events (client -> server):**
- `ToggleCell(gameId, row, col, playerId)`

**Server events (server -> client):**
- `CellToggled(row, col, isOn, rowXor, colXor, playerId, colour)`
- `GameSolved()`

**Completion check:** for every row the XOR of highlighted bytes == `0x00`, **and**
for every column the XOR of highlighted bytes == `0x00`.

**Sync note:** toggling is a pure flip applied server-side, so concurrent toggles
on different cells are independent. Two tabs flipping the *same* cell just land in
arrival order - the last flip wins and every tab converges on the same
authoritative `On` state. No locking needed.

**Multi-tab test:** two tabs toggle different cells rapidly; verify both states
appear in each other within ~100 ms and the row/column clues update. Toggle the
same cell from two tabs simultaneously; verify no error and that all tabs agree
on the final on/off state. Verify the toggle that zeroes the last clue triggers
`GameSolved` in every tab.

---

### 4. Permission Tree

**Concept:** A fake filesystem tree where nodes carry permission flags (e.g.
`rwxr--r--`). Some flags are intentionally wrong. Any player can toggle any
flag on any node.

**State shape:**

```csharp
record PermissionTreeState(
    List<FileNode> Roots,
    bool Solved
);

record FileNode(
    string Id,
    string Path,
    string CurrentFlags,
    string ExpectedFlags,
    string? LastTouchedBy,
    List<FileNode> Children
);
```

**Server events (client â†’ server):**
- `ToggleFlag(gameId, nodeId, flagIndex, playerId)`

**Server events (server â†’ client):**
- `NodeUpdated(nodeId, currentFlags, playerId, colour)`
- `GameSolved()`

**Completion check:** All nodes have `CurrentFlags == ExpectedFlags`.

**Multi-tab test:** Two tabs toggle flags on different nodes simultaneously;
verify isolated updates. Two tabs toggle the same flag simultaneously; verify
consistent result (not a torn state).

---

### 5. Circuit Tracer

**Concept:** A grid of cells containing logic gates (AND, OR, NOT, WIRE).
Input signals enter from the left; output nodes sit on the right. Any player
can change any gate. The board is solved when all outputs resolve to their
expected values.

**State shape:**

```csharp
record CircuitTracerState(
    int Rows,
    int Cols,
    List<GateCell> Cells,
    List<SignalChain> Chains,
    bool Solved
);

record GateCell(string Id, int Row, int Col, GateType Type, string? LastTouchedBy);
record SignalChain(string Id, List<string> CellIds, bool ExpectedOutput);

enum GateType { Wire, And, Or, Not, Input, Output }
```

**Server events (client â†’ server):**
- `SetGate(gameId, cellId, GateType newType, playerId)`

**Server events (server â†’ client):**
- `CellUpdated(cellId, newType, playerId, colour)`
- `GameSolved()`

**Completion check:** Evaluate all chains server-side; each output node must
match its `ExpectedOutput` given the current gate configuration.

**Note:** Gate evaluation must happen server-side to prevent clients from
diverging if events arrive in different orders.

**Multi-tab test:** Two tabs change gates on different chains simultaneously;
verify evaluation fires correctly after each update. Two tabs change the same
gate simultaneously; verify no evaluation inconsistency.

---

### 6. Frequency Tuner

**Concept:** An array of dials, each controlling a frequency parameter. A
target waveform is displayed. Any player can move any dial. The board is solved
when all dials are within tolerance of their target values.

**State shape:**

```csharp
record FrequencyTunerState(
    List<Dial> Dials,
    bool Solved
);

record Dial(
    string Id,
    double CurrentValue,    // 0.0â€“1.0
    double TargetValue,
    double Tolerance,
    string? LastTouchedBy
);
```

**Server events (client â†’ server):**
- `SetDial(gameId, dialId, value, playerId)`

**Server events (server â†’ client):**
- `DialUpdated(dialId, value, playerId, colour)`
- `GameSolved()`

**Completion check:** `Math.Abs(dial.CurrentValue - dial.TargetValue) <= dial.Tolerance`
for all dials.

**Waveform rendering:** Computed client-side from current dial values using a
simple sum-of-sines formula. Server only stores dial values.

**Sync note:** Two tabs moving the same dial produces rapid `DialUpdated`
events â€” last-write-wins causes visible jitter. Acceptable for PoC; in
production add client-side debounce and server-side rate limiting per dial.

**Multi-tab test:** Both tabs drag the same dial simultaneously; observe and
document the jitter behaviour. Verify dials moved in separate tabs do not
interfere with each other.

---

### 7. Signal Reconstruction *(hardest â€” build last)*

**Concept:** A waveform or image is sliced into vertical columns and shuffled.
Players drag slices into the correct positions collaboratively.

**Grab lock (this game only):** Because a drag is a continuous gesture, a
player grabs a slice at drag-start and holds it until drop or cancel. This
prevents the slice from jumping mid-drag if another player tries to move it
simultaneously. The lock is per-slice and times out after 10 seconds to handle
tab crashes.

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

**Server events (client â†’ server):**
- `GrabSlice(gameId, sliceId, playerId)` â€” acquire grab lock
- `PlaceSlice(gameId, sliceId, targetPosition, playerId)` â€” drop and release
- `ReleaseSlice(gameId, sliceId, playerId)` â€” cancel drag (e.g. Escape key)

**Server events (server â†’ client):**
- `SliceGrabbed(sliceId, playerId, colour)`
- `SlicePlaced(sliceId, targetPosition, playerId, colour)`
- `SliceReleased(sliceId)`
- `GrabRejected(sliceId)` â€” sent to caller only when slice is already held
- `GameSolved()`

**Lock timeout:** A background hosted service scans for slices where
`HeldSince < DateTime.UtcNow - 10s` and releases them, broadcasting
`SliceReleased`. This handles tab crashes and dropped connections.

**Disconnect handling:** `OnDisconnectedAsync` releases all slices held by
that connection immediately, without waiting for the timeout.

**Completion check:** All slices have `CurrentPosition == CorrectPosition`.

**Multi-tab test:** Two tabs attempt to grab the same slice simultaneously;
verify only one receives confirmation and the other receives `GrabRejected`.
Crash a tab while holding a slice; verify `SliceReleased` is broadcast after
the timeout.

---

## Build Order

| # | Game | Complexity | Validates |
|---|------|------------|-----------|
| 1 | Cipher Wheel | Low | Core move â†’ broadcast â†’ win pipeline end-to-end |
| 2 | Packet Sequencer | Low | Same pipeline, drag-reorder UI |
| 3 | Memory Leak | Low | Toggle flip, row+column XOR win check, hex nonogram UI |
| 4 | Permission Tree | Medium | Tree rendering, flag toggling |
| 5 | Circuit Tracer | Medium | Server-side logic evaluation |
| 6 | Frequency Tuner | Medium | Continuous input, waveform rendering |
| 7 | Signal Reconstruction | High | Grab lock, timeout cleanup |

---

## Kubernetes Deployment

Minimal setup â€” single deployment, single service, ingress via existing
wildcard DNS (`*.gemberkoekje.nl`).

```yaml
# k8s/deployment.yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: hacker-minigames
spec:
  replicas: 1          # Keep at 1 for PoC â€” SignalR in-memory state is not shared across pods
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
> scale to multiple replicas, add a Redis backplane for SignalR
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
        // gameId passed as query param on connect
        var gameId = Context.GetHttpContext()?.Request.Query["gameId"].ToString();
        if (!string.IsNullOrEmpty(gameId))
            await Groups.AddToGroupAsync(Context.ConnectionId, gameId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        // Release any grab locks held by this connection (Signal Reconstruction)
        var released = _sessions.ReleaseHeldSlices(Context.ConnectionId);
        foreach (var (gameId, sliceId) in released)
            await Clients.Group(gameId).SendAsync("SliceReleased", sliceId);
        await base.OnDisconnectedAsync(exception);
    }

    // Example move handler â€” all games follow this same pattern
    public async Task SetOffset(string gameId, string wordId, int letterIndex,
                                int offset, string playerId)
    {
        var (colour, solved) = _sessions.ApplySetOffset(gameId, wordId, letterIndex,
                                                         offset, playerId);
        await Clients.Group(gameId)
            .SendAsync("OffsetChanged", wordId, letterIndex, offset, playerId, colour);
        if (solved)
            await Clients.Group(gameId).SendAsync("GameSolved");
    }
}
```

---

## Things to Test with Multiple Tabs

| Test | Game(s) | What to verify |
|------|---------|----------------|
| Simultaneous moves on same element | All games | Result is consistent; no torn state |
| Simultaneous moves on different elements | All games | Both updates reflected in all tabs within ~100 ms |
| Same-cell double toggle | Memory Leak | Last flip wins; no error; all tabs converge on same on/off state |
| Simultaneous dial movement | Frequency Tuner | Document jitter behaviour for future mitigation |
| Slice grab collision | Signal Reconstruction | One tab wins; other receives `GrabRejected` |
| Slice lock timeout | Signal Reconstruction | Crash a tab mid-drag; `SliceReleased` broadcast after 10 s |
| Win triggered by any tab | All games | Final move in tab A shows victory banner in tab B |
| Disconnect/reconnect | All games | Reconnecting tab receives current board state on join |
