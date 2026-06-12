# AdventureEngine — Implementation Status

Tracks progress against the build order in [llm-adventure-plan.md](./llm-adventure-plan.md).

---

## Build Order

### ✅ 1. Skeleton
Blazor Server project wired up, Marten configured, Postgres health check registered in `Program.cs`.

### ✅ 2. Domain
`GameSession` Marten aggregate and the full event discriminated union (`SessionCreated`, `ChapterStarted`, `SceneEntered`, `PlayerActed`, `NpcSpoke`, `ChapterCompleted`, `GameEnded`, `UsageRecorded`) are implemented in `AdventureEngine.Domain`.

### ✅ 3. Director
`DirectorAgent` calls claude-opus-4 with a structured prompt, parses the JSON response into a `WorldManifest`, and validates the result. `WorldManifest` JSON schema deserialization is covered by a unit test.

### ✅ 4. Lobby flow
`Home.razor` accepts the player prompt, calls `GameSessionService.CreateSessionAsync` (which invokes the Director), and navigates to `Play.razor` on success.

### ✅ 5. Narrator
`NarratorAgent` builds the full layered context (system prompt, world anchor, story-so-far, current chapter, recent history, player input) and streams tokens back via `IAsyncEnumerable<string>`. Prompt caching is enabled (`PromptCacheType.AutomaticToolsAndSystem`) on the stable prefix.

### ✅ 6. Play.razor
`Play.razor` renders the `GameTerminal` component, streams narrator tokens one by one (calling `StateHasChanged()` per token), and exposes a player input box. `GameTerminal.razor` shows a blinking cursor while streaming.

### ✅ 7. NPC agents
`NpcAgent` calls claude-haiku-4-5 with a character-sheet context. `GameSessionService` detects active NPCs in the current scene and fires one `NpcAgent` call per NPC, persisting a `NpcSpoke` event for each.

### ✅ 8. Chapter commits
`GameSession` now tracks `CurrentChapterHistory` (full exchange log for the current chapter, cleared on chapter completion).

The narrator system prompt instructs it to emit HTML comment markers at the end of its response when significant state changes occur:
- `<!-- SCENE:{scene_id} -->` — player navigated to a new scene
- `<!-- OUTCOME:WON -->` / `<!-- OUTCOME:LOST -->` — win/lose condition met

`GameSessionService.SubmitActionAsync` parses these markers, strips them from the display text, and emits the appropriate events:
- `SceneEntered` when the player moves to a new scene within a chapter
- `ChapterCompleted` (+ chapter summary via `NarratorAgent.GenerateChapterSummaryAsync`) when moving to a scene in the next chapter
- `GameEnded` when a win/lose condition is detected

The `BuildCurrentChapter` helper now includes the description of each exit scene so the narrator can correctly emit scene IDs.

### ✅ 9. Prompt caching
Cache control is applied to the static narrator prefix in `NarratorAgent.StreamResponseAsync`. No further work needed here until the caching strategy is re-evaluated.

### ✅ 10. Polish

| Item | Status |
|---|---|
| Session history UI (list past games on Home page) | ✅ Home.razor shows a paginated list of up to 10 past sessions with title, date, and status |
| Save / resume (reconnect rebuilds context from event log) | ✅ Marten event sourcing rebuilds context on every load; `Play.razor` restores terminal history on reconnect; Home page shows Resume links |
| Error recovery (graceful fallbacks for API failures) | ✅ `GameSessionService` retries transient HTTP/timeout errors up to 3 times with exponential back-off; failed player actions restore the input field |
| Cost / token-usage tracking | ✅ `UsageRecorded` event added; `GameSession` accumulates `TotalInputTokens`/`TotalOutputTokens`; recorded for Director, NPC, and chapter summary calls |
| Input moderation (Haiku pre-screen before Director) | ✅ `ModerationAgent` calls claude-haiku-4-5 to classify the player's premise as SAFE/UNSAFE before the Director is invoked |
| Authentication (replace hardcoded `"guest"` PlayerId) | ✅ Player name stored in browser localStorage; Home.razor prompts for a name on first visit |

---

## Previously Missing Key Logic — Now Resolved

| Gap | Resolution |
|---|---|
| **Chapter boundary detection** | Narrator emits `<!-- SCENE:{id} -->` markers; service detects cross-chapter transitions and calls `GenerateChapterSummaryAsync`, then emits `ChapterCompleted` |
| **Scene / chapter navigation** | `SceneEntered` events emitted on scene changes; `ChapterCompleted.Apply` advances `CurrentChapterIndex` and `CurrentSceneId` |
| **Win / lose evaluation** | Narrator emits `<!-- OUTCOME:WON/LOST -->` markers; service emits `GameEnded`; `Play.razor` shows victory/defeat banners |
| **NPC relationship state** | Out of scope per original plan; deferred as a future enhancement |

---

## Technology Alignment

| Layer | Planned | Status |
|---|---|---|
| Backend | C# / .NET 10 | ✅ |
| Frontend | Blazor Server | ✅ |
| Database | PostgreSQL + Marten event sourcing | ✅ |
| LLM — Director | claude-opus-4 | ✅ |
| LLM — Narrator | claude-sonnet-4-6, streaming | ✅ |
| LLM — NPC | claude-haiku-4-5 | ✅ |
| Prompt caching | Anthropic cache control on stable prefix | ✅ |
| Auth | Player name via localStorage | ✅ (lightweight; no IdP) |

