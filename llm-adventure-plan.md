# LLM Text Adventure — Architecture Plan

## Overview

A multi-agent text adventure engine where each playthrough is generated on-demand from a player prompt, narrated by a stateful AI, and populated by NPCs with their own independent contexts. Built on .NET 10, Postgres, Blazor Server, and the Anthropic Claude API.

---

## Technology Choices

| Layer | Choice | Rationale |
|---|---|---|
| Backend | C# / .NET 10 | Your default; clean async/await for multi-LLM orchestration |
| Frontend | **Blazor Server** | Real-time SignalR streaming fits perfectly — narrator text streams in token by token; no npm complexity |
| Database | PostgreSQL + Marten | Event sourcing for game sessions; you already know Marten well |
| LLM | Anthropic Claude API | Director = Opus 4.7, Narrator = Sonnet 4.6, NPCs = Haiku 4.5 |
| Auth | ASP.NET Core Identity + cookie | Simple, no external IdP needed for a hobby project |

**Why Blazor Server over Razor Pages or an SPA:** Streaming LLM output is the UX centrepiece — each token appearing one by one. Blazor Server's SignalR connection makes this trivial (`await foreach` the stream, call `StateHasChanged()`). With Razor Pages you'd need SSE or WebSockets yourself. With React you'd add a whole frontend build pipeline.

---

## Agent Architecture

```
Player Prompt
     │
     ▼
┌─────────────┐   one-shot at game start
│  DIRECTOR   │   claude-opus-4-7
│             │   → Generates WorldManifest (JSON)
└─────────────┘   → Chapters, scenes, NPCs, lore, win/lose conditions
     │
     │  WorldManifest stored in Postgres
     │
     ▼
┌─────────────┐   called every player action (~100x per game)
│  NARRATOR   │   claude-sonnet-4-6
│             │   Context: system prompt + world summary + chapter state
│             │     + sliding history window + player action
└──────┬──────┘   → Streams narrative response token by token
       │          → May trigger NPC dialogue
       │
       │  (when scene contains active NPCs)
       ▼
┌─────────────┐   called per NPC, per scene where they appear
│  NPC AGENT  │   claude-haiku-4-5  (one instance per NPC call)
│             │   Context: character sheet + scene summary + player input
└─────────────┘   → Returns in-character dialogue/reaction
```

---

## Data Model

### Postgres / Marten Events

```
GameSession
  ├── SessionId (Guid)
  ├── PlayerId
  ├── WorldManifest (JSONB)       ← full Director output, never mutated
  ├── Status (Generating | Active | Completed | Abandoned)
  └── CreatedAt

-- Event stream (Marten event sourcing)
GameEvent (discriminated union)
  ├── SessionCreated     { SessionId, PlayerPrompt, WorldManifest }
  ├── ChapterStarted     { ChapterIndex, Summary }
  ├── SceneEntered       { SceneId, NarratorContext }
  ├── PlayerActed        { Input, NarratorResponse }
  ├── NpcSpoke           { NpcId, Dialogue }
  ├── ChapterCompleted   { ChapterIndex, OutcomeSummary }   ← commit point
  └── GameEnded          { Outcome }
```

### WorldManifest JSON schema (Director output)

```json
{
  "title": "Neon Horizons",
  "premise": "...",
  "lore": "...",
  "win_condition": "...",
  "lose_condition": "...",
  "chapters": [
    {
      "index": 1,
      "title": "...",
      "summary": "...",
      "scenes": [
        {
          "id": "ch1_sc1",
          "description": "...",
          "active_npc_ids": ["npc_ryo", "npc_mira"],
          "exits": ["ch1_sc2"]
        }
      ]
    }
  ],
  "npcs": [
    {
      "id": "npc_ryo",
      "name": "Ryo",
      "personality": "...",
      "backstory": "...",
      "relationship_to_player": "...",
      "secrets": "...",
      "speech_style": "..."
    }
  ]
}
```

---

## Context Window Strategy

The hardest problem is keeping Narrator coherent over 100 turns without blowing up token costs.

### Narrator context composition (per call)

```
[SYSTEM — static, ~800 tokens]
  You are the narrator of an interactive fiction adventure.
  Be vivid, second-person, present tense. Respond to player actions.
  Keep responses to 150–250 words unless drama demands more.
  Honour established facts in the story context below.

[WORLD ANCHOR — static per session, ~600 tokens]
  Title, premise, win/lose conditions, list of NPCs with one-line descriptions.
  → Use prompt caching here (90% cost reduction on repeated calls)

[STORY SO FAR — rolling summary, ~500 tokens]
  Completed chapter summaries (committed at chapter boundaries).
  Never grows past 5 chapter summaries × ~100 tokens each.

[CURRENT CHAPTER — ~300 tokens]
  Chapter title, scene description, active NPCs, available exits.

[RECENT HISTORY — sliding window, last 6 exchanges, ~1200 tokens]
  Alternating PlayerActed / NarratorResponse events.

[PLAYER INPUT — ~50 tokens]
  Current player action.
```

Total: ~3,450 tokens in, ~300 tokens out per call.

**Prompt caching** on World Anchor + System = ~1,400 tokens cached → ~90% cheaper on those tokens after the first call.

### NPC context composition (per call)

```
[SYSTEM — ~200 tokens]
  You are {name}. Respond only as this character. Stay in voice.
  Keep responses to 1–3 sentences.

[CHARACTER SHEET — ~400 tokens]
  Personality, backstory, relationship to player, speech style, secrets.

[SCENE CONTEXT — ~200 tokens]
  Where we are, what just happened, what the player said or did.
```

Total: ~800 tokens in, ~100 tokens out per NPC call.

### Chapter Commit (Narrator side-task, at chapter boundary)

After chapter completion, make one additional Narrator call with instruction:

> "Summarise what happened in chapter {n} in 80–100 words. Focus on: key decisions, NPC relationship changes, items gained/lost, mysteries opened or closed."

This summary becomes permanent context for all future calls. Cost: ~3,500 tokens once per chapter.

---

## Project Structure

```
AdventureEngine/
├── src/
│   ├── AdventureEngine.Web/              ← Blazor Server project
│   │   ├── Components/
│   │   │   ├── Pages/
│   │   │   │   ├── Home.razor            ← prompt entry, recent sessions
│   │   │   │   ├── Lobby.razor           ← world gen in progress
│   │   │   │   └── Play.razor            ← main game view
│   │   │   └── GameTerminal.razor        ← streaming text output component
│   │   └── Program.cs
│   │
│   ├── AdventureEngine.Application/      ← use cases / orchestration
│   │   ├── Sessions/
│   │   │   ├── CreateSessionCommand.cs
│   │   │   └── SubmitActionCommand.cs
│   │   └── Agents/
│   │       ├── DirectorAgent.cs          ← one-shot world gen
│   │       ├── NarratorAgent.cs          ← streaming, sliding context
│   │       └── NpcAgent.cs               ← per-character calls
│   │
│   ├── AdventureEngine.Domain/           ← events, aggregates, value objects
│   │   ├── GameSession.cs                ← Marten aggregate
│   │   └── Events/
│   │
│   └── AdventureEngine.Infrastructure/  ← Postgres/Marten, Anthropic client
│       ├── AnthropicClientWrapper.cs
│       └── MartenConfiguration.cs
│
└── tests/
    └── AdventureEngine.Tests/
```

---

## Key Implementation Details

### Streaming to Blazor

```csharp
// NarratorAgent.cs
public async IAsyncEnumerable<string> StreamNarratorResponse(
    NarratorContext ctx,
    [EnumeratorCancellation] CancellationToken ct = default)
{
    var request = BuildRequest(ctx);
    await foreach (var chunk in _anthropic.StreamAsync(request, ct))
        yield return chunk.Delta?.Text ?? "";
}

// Play.razor
private async Task HandlePlayerAction(string input)
{
    _streamBuffer = "";
    await foreach (var token in _narratorAgent.StreamNarratorResponse(ctx))
    {
        _streamBuffer += token;
        await InvokeAsync(StateHasChanged);
    }
    await _sessionService.CommitPlayerTurn(SessionId, input, _streamBuffer);
}
```

### Director Prompt (abbreviated)

```
You are a creative director generating a 5-chapter interactive fiction adventure.
Generate ONLY valid JSON matching the WorldManifest schema below.
Do not include any text outside the JSON object.

Player premise: {playerPrompt}

Schema:
{worldManifestSchema}

Requirements:
- 5 chapters, 2–4 scenes each
- 3–5 named NPCs with distinct personalities and secrets
- A clear win condition and a meaningful lose condition
- Internal consistency: NPCs referenced in scenes must exist in the npcs array
```

### Prompt Caching

```csharp
// On all Narrator calls after the first, mark the stable prefix as cached
var messages = new[]
{
    new Message(Role.User, new[]
    {
        new CacheControl(CacheType.Ephemeral),  // World Anchor cached here
        new TextBlock(worldAnchorText),
        new TextBlock(storyProgressText),       // not cached — changes
        new TextBlock(playerInput)
    })
};
```

---

## Revised Cost Estimate (current pricing)

Pricing: Opus 4.7 at $5/$25 per MTok, Sonnet 4.6 at $3/$15 per MTok, Haiku 4.5 at $1/$5 per MTok.

| Agent | Calls | Tokens (in/out) | Cost (standard) | Cost (w/ caching) |
|---|---|---|---|---|
| Director (Opus 4.7) | 1 | 1,000 in / 3,000 out | ~$0.08 | ~$0.08 |
| Narrator (Sonnet 4.6) | 100 | 345,000 in / 30,000 out | ~$1.49 | **~$0.62** |
| NPC (Haiku 4.5) | 80 | 64,000 in / 8,000 out | ~$0.10 | ~$0.10 |
| Chapter commits (Sonnet) | 5 | 17,500 in / 2,500 out | ~$0.09 | ~$0.09 |
| **Total** | | | **~$1.76** | **~$0.89** |

**With prompt caching on the Narrator's static prefix (~1,400 tokens), cost per playthrough drops to under $1.**

At 1 EUR ≈ 1.08 USD, that's roughly **€0.80–€1.60 per complete playthrough**.

---

## Build Order

1. **Skeleton** — Blazor Server project, Marten configured, health endpoint
2. **Domain** — GameSession aggregate, event types
3. **Director** — prompt + JSON parsing + WorldManifest validation
4. **Lobby flow** — player enters prompt → Director called → stored → redirect to Play
5. **Narrator** — context builder, streaming integration
6. **Play.razor** — streaming terminal UI, player input
7. **NPC agents** — triggered by Narrator when scene has active NPCs
8. **Chapter commits** — chapter boundary detection, summary generation
9. **Prompt caching** — add cache control headers to stable context prefix
10. **Polish** — session history, save/resume, error recovery, cost tracking

---

## Open Design Questions

- **Multiplayer?** The architecture supports it (events are the source of truth, Narrator just needs shared context) but adds turn-ordering complexity. Suggest deferring.
- **Save/resume** — Marten's event log means this is basically free: rebuild context from events on reconnect.
- **NPC memory** — currently NPCs have no memory across scenes. Could add a per-NPC "relationship state" updated at chapter commits if you want deeper NPC arcs.
- **Moderation** — player prompts can go anywhere. Add a lightweight Haiku call to screen the initial premise before feeding it to Opus.
