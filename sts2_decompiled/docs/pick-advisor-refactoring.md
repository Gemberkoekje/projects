# Pick Advisor — LLM removal & calculation-based scoring

## What changed

The Pick Advisor no longer calls any LLM. All recommendations are produced by a local calculation that scores each offered card against the current build, and also scores the **skip** option.

### Removed
- `PickExplanationService` dependency and the "Explain (LLM)" button/narrative display.

### Added

**Archetype selection** — checkboxes below the form let the player mark one or more archetypes they want to lean into. These feed into the existing archetype-affinity scoring.

**Skip score** — a 0–0.85 score (displayed ×10) that estimates the value of taking no card. It accounts for:

| Factor | Effect on skip score |
|---|---|
| Raw deck size | More cards → higher skip value |
| Power cards | Played once then leave the draw pile — reduces effective deck size |
| Exhaust / Ethereal cards | Self-removing — reduces effective deck size |
| Draw cards in deck | Offset bloat by cycling through more cards per turn |
| Draw relics | Persistent draw bonus every turn (weighted ×2) |

The formula computes an **effective bloat** = (total cards − powers − exhaust cards − draw sources), passes it through a sigmoid curve, then subtracts a tolerance bonus for decks with high power/exhaust ratios or draw density. Result: a lean 12-card deck with draw gets a very low skip score (~0.5 displayed), while a 35-card deck with no draw or powers gets a high one (~6+).

### Files modified
- `sts2_Viewer/Pages/PickAdvisor.cshtml.cs` — removed LLM service, added `DeckDrawProfile` + `CalculateSkipScore`.
- `sts2_Viewer/Pages/PickAdvisor.cshtml` — archetype checkboxes, skip score display with deck breakdown, removed LLM UI.
- `sts2_Viewer/Data/DeckDrawProfile.cs` — new data class.
- `sts2_Viewer/Data/PostgresReadService.cs` — new `LoadDeckDrawProfile` method querying card types, keywords, effect tags, and relic draw tags.
