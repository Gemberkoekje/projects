# AdventureEngine — Usage & Caching Fix Plan

Plan to close the gap between `status.md` (which claims token/cost tracking is done) and the
actual implementation, where `UsageRecorded` is defined but never emitted by any agent.

## Problem summary

| # | Issue | Evidence |
|---|---|---|
| 1 | Narrator streaming usage is never captured. `StreamResponseAsync` only yields `res.Delta?.Text` and drops the final `message_delta` metadata. | `src/AdventureEngine.Infrastructure/Agents/NarratorAgent.cs:77-81` |
| 2 | Non-streaming calls (Director, NPC, chapter summary, moderation) read `.Message` but never read `.Usage`, so no `UsageRecorded` is emitted. | `DirectorAgent.cs:93`, `NpcAgent.cs:54`, `ModerationAgent.cs:50`, `NarratorAgent.cs:119` |
| 3 | `UsageRecorded` has no cache fields, so cache hits from `PromptCacheType.AutomaticToolsAndSystem` can't be verified. The World Anchor lives in the **user** turn, which automatic caching likely does **not** cover. | `GameEvents.cs:46-51`, `NarratorAgent.cs:48-58,74` |
| 4 | Moderation returns only `bool`; a rejected premise gives the player no actionable reason. | `ModerationAgent.cs:22-61`, `GameSessionService.cs:39-43` |
| 5 | `status.md` overstates the feature ("recorded for Director, NPC, and chapter summary calls"). | `status.md:54` |

## Goals

- Emit a `UsageRecorded` event for **every** LLM call (streaming and non-streaming).
- Capture cache read / cache creation token counts so caching effectiveness is measurable.
- Surface the moderation rejection reason to the player.
- Update `status.md` to reflect reality.

---

## Step 1 — Extend the `UsageRecorded` event (completed)

In `src/AdventureEngine.Domain/Events/GameEvents.cs`, add two cache fields to `UsageRecorded`:

- `CacheReadInputTokens`
- `CacheCreationInputTokens`

Rules for all emitters:
- Always emit one `UsageRecorded` per LLM call.
- When a usage field is absent from provider metadata, store `0` (never null) so aggregation stays trivial.

Update `GameSession.Apply(UsageRecorded)` in `src/AdventureEngine.Domain/GameSession.cs` to accumulate the
two new fields (add `TotalCacheReadInputTokens` / `TotalCacheCreationInputTokens` properties).

Update the existing test in `tests/AdventureEngine.Tests/GameSessionTests.cs` that constructs
`UsageRecorded` (currently 5-arg) to use the new shape, and add assertions for the cache totals.

> Note: this is an event-schema change. Because sessions are rebuilt by replaying the Marten event
> stream, older `UsageRecorded` events must still deserialize. Keep new fields as additive positional
> record parameters with default-friendly values, or add an explicit upcast if Marten cannot fill the
> new positions. Verify replay of an existing stream during testing.

✅ Implemented:
- Added `CacheReadInputTokens` and `CacheCreationInputTokens` to `UsageRecorded` with default `0` values.
- Added `TotalCacheReadInputTokens` / `TotalCacheCreationInputTokens` accumulation in `GameSession`.
- Updated `GameSessionTests.Apply_UsageRecorded_AccumulatesTokens` with cache assertions.
- Added a legacy JSON deserialization test to verify older payloads still default cache fields to `0`.

## Step 2 — Narrator streaming usage (highest priority)

In `src/AdventureEngine.Infrastructure/Agents/NarratorAgent.StreamResponseAsync`:
- Add an optional completion callback parameter to the method (and to `INarratorAgent`), e.g.
  `Action<NarratorUsage>? onComplete = null`, keeping the `IAsyncEnumerable<string>` text stream intact.
- While iterating `StreamClaudeMessageAsync`, keep yielding text deltas as today, but also inspect each
  streamed event for the final usage payload (the `message_delta` / message-stop carrying `Usage`,
  including `cache_read_input_tokens` and `cache_creation_input_tokens`).
- After the loop, if usage was captured, invoke `onComplete` with the mapped values.

This sidesteps the "can't both `yield return` and `return` a value" limitation without adding a wrapper
result type to the streaming contract.

In `GameSessionService.SubmitActionAsync`:
- Pass a callback that appends a `UsageRecorded(sessionId, "narrator", …)` event.
- `CollectStreamAsync` already drains the stream; thread the callback through so the usage is captured
  by the time the events list is appended.

✅ Implemented:
- Added `NarratorUsage` and an optional `onComplete` callback to `INarratorAgent.StreamResponseAsync`.
- Updated `NarratorAgent.StreamResponseAsync` to capture streaming `Usage` metadata and invoke callback on completion.
- Updated `GameSessionService.SubmitActionAsync` to pass the callback and append narrator `UsageRecorded` events.

## Step 3 — Non-streaming usage (Director, NPC, chapter summary, moderation)

For each non-streaming call site, read `.Usage` from the response (alongside the existing `.Message`)
and emit a `UsageRecorded` with the matching `Agent` tag:
- `DirectorAgent` → `"director"`
- `NpcAgent` → `"npc"`
- `NarratorAgent.GenerateChapterSummaryAsync` → `"chapter_summary"`
- `ModerationAgent` → `"moderation"`

Design choice — keep agents persistence-free: agents should **return** usage to the service rather than
writing events themselves (the agents currently have no `IDocumentSession`). Options:
- Return a small `(result, usage)` tuple / record from each agent method, **or**
- Use the same `onComplete` callback pattern as the Narrator.

Then `GameSessionService` is the single place that appends `UsageRecorded` events, keeping the
event-sourcing write path centralized. `CreateSessionAsync` emits the Director (and moderation) usage;
`SubmitActionAsync` emits Narrator, NPC, and chapter-summary usage.

✅ Implemented:
- Renamed `NarratorUsage` → `AgentUsage` (shared record for all agents).
- Changed `IDirectorAgent.GenerateWorldAsync` to return `(WorldManifest World, AgentUsage Usage)`.
- Changed `INpcAgent.RespondAsync` to return `(string Dialogue, AgentUsage Usage)`.
- Changed `INarratorAgent.GenerateChapterSummaryAsync` to return `(string Summary, AgentUsage Usage)`.
- Changed `ModerationAgent.IsSafeAsync` to return `(bool IsSafe, AgentUsage Usage)`.
- Updated all implementations to read `.Usage` from the Anthropic response and return it.
- Updated `GameSessionService.CreateSessionAsync` to emit `UsageRecorded` for "moderation" and "director".
- Updated `GameSessionService.SubmitActionAsync` to emit `UsageRecorded` for "npc" and "chapter_summary".
- Added `Apply_UsageRecorded_AccumulatesAllNonStreamingAgentTags` test covering all four new agent tags.

## Step 4 — Cache hit verification

Once usage flows with cache fields:
- Log `CacheReadInputTokens` / `CacheCreationInputTokens` per Narrator call (the biggest cost driver).
- Optionally surface session totals in the Home/session-history UI.

Expected finding: `AutomaticToolsAndSystem` caches only the system prompt + tools, not the World Anchor
(which is in the user message). If cache reads stay ~0:
- Move the stable prefix (World Anchor + static framing) into the **system** block, **or**
- Switch to explicit cache-control breakpoints on the World Anchor block so the stable prefix is cached.

Re-check the cache counters after the change to confirm hits.

## Step 5 — Moderation reason (lower priority)

In `ModerationAgent`:
- Change the Haiku prompt to return JSON: `{"result":"SAFE"}` or `{"result":"UNSAFE","reason":"…"}`.
- Parse it into a small result type (e.g. `ModerationResult(bool IsSafe, string? Reason)`); default to
  safe on empty/unparseable output (preserving current fail-open behaviour) and log a warning.

In `GameSessionService.CreateSessionAsync`:
- Include the returned reason in the thrown `InvalidOperationException` message so `Home.razor` can show
  the player *why* the premise was rejected instead of a generic message.

## Step 6 — Docs

Update `status.md` section 10 / "Cost / token-usage tracking" row to accurately describe:
- which agents emit `UsageRecorded` (now all of them, incl. Narrator streaming),
- the new cache fields, and
- the caching verification outcome.

---

## Validation

- `dotnet restore AdventureEngine.slnx`
- `dotnet build AdventureEngine.slnx --no-restore`
- `dotnet test AdventureEngine.slnx`

Add/extend unit tests:
- `UsageRecorded` accumulation including the two cache fields.
- Replay of a pre-change `UsageRecorded` event still rehydrates a session (schema-evolution safety).
- `ModerationResult` JSON parsing (SAFE, UNSAFE+reason, empty/garbage → fail-open).

## Suggested sequencing

1. ✅ Step 1 (event shape) — complete.
2. ✅ Step 2 (Narrator streaming usage) — complete.
3. ✅ Step 3 (non-streaming usage) — complete.
4. Step 4 (cache verification + possible cache-control fix).
5. Step 5 (moderation reason).
6. Step 6 (status.md).
