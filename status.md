# AdventureEngine — Implementation Status

Tracks progress against the build order in [llm-adventure-plan.md](./llm-adventure-plan.md).

---

## Build Order

### ✅ 1. Skeleton
Blazor Server project wired up, Marten configured, Postgres health check registered in `Program.cs`.

### ✅ 2. Domain
`GameSession` Marten aggregate and the full event discriminated union (`SessionCreated`, `ChapterStarted`, `SceneEntered`, `PlayerActed`, `NpcSpoke`, `ChapterCompleted`, `GameEnded`) are implemented in `AdventureEngine.Domain`.

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

### ⚠️ 8. Chapter commits
`NarratorAgent.GenerateChapterSummaryAsync` is implemented, but **nothing calls it**. There is no chapter-boundary detection: `ChapterCompleted` events are never emitted, scene transitions are not tracked, and win/lose conditions are never evaluated. This step is functionally incomplete.

### ✅ 9. Prompt caching
Cache control is applied to the static narrator prefix in `NarratorAgent.StreamResponseAsync`. No further work needed here until the caching strategy is re-evaluated.

### ❌ 10. Polish
None of the polish items are implemented:

| Item | Status |
|---|---|
| Session history UI (list past games on Home page) | ❌ Not started |
| Save / resume (reconnect rebuilds context from event log) | ❌ Not started |
| Error recovery (graceful fallbacks for API failures) | ❌ Not started |
| Cost / token-usage tracking | ❌ Not started |
| Input moderation (Haiku pre-screen before Director) | ❌ Not started |
| Authentication (replace hardcoded `"guest"` PlayerId) | ❌ Not started |

---

## Missing Key Logic (cross-cutting gaps)

These are required for the game to function end-to-end and do not map to a single build step:

| Gap | Detail |
|---|---|
| **Chapter boundary detection** | No code detects when a chapter is complete to emit `ChapterCompleted` + trigger the summary call |
| **Scene / chapter navigation** | Player actions do not update `CurrentSceneId` or `CurrentChapterIndex` on the session |
| **Win / lose evaluation** | `GameEnded` is never emitted; win/lose conditions from the manifest are never checked |
| **NPC relationship state** | Plan mentions per-NPC state updated at chapter commits; not implemented |

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
| Auth | ASP.NET Core Identity | ❌ (placeholder only) |
