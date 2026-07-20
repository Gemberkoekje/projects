---
type: community
cohesion: 0.17
members: 25
---

# Community 4

**Cohesion:** 0.17 - loosely connected
**Members:** 25 nodes

## Members
- [[.Apply()]] - code - src/AdventureEngine.Domain/GameSession.cs
- [[.Apply_ChapterCompleted_AdvancesChapterAndClearsHistory()]] - code - tests/AdventureEngine.Tests/GameSessionTests.cs
- [[.Apply_GameEnded_Lost_SetsStatusAbandoned()]] - code - tests/AdventureEngine.Tests/GameSessionTests.cs
- [[.Apply_GameEnded_Won_SetsStatusCompleted()]] - code - tests/AdventureEngine.Tests/GameSessionTests.cs
- [[.Apply_PlayerActed_AddsToRecentHistoryAndChapterHistory()]] - code - tests/AdventureEngine.Tests/GameSessionTests.cs
- [[.Apply_PlayerActed_SlidesWindowAfterSix()]] - code - tests/AdventureEngine.Tests/GameSessionTests.cs
- [[.Apply_SceneEntered_UpdatesCurrentScene()]] - code - tests/AdventureEngine.Tests/GameSessionTests.cs
- [[.Apply_UsageRecorded_AccumulatesAllNonStreamingAgentTags()]] - code - tests/AdventureEngine.Tests/GameSessionTests.cs
- [[.Apply_UsageRecorded_AccumulatesTokens()]] - code - tests/AdventureEngine.Tests/GameSessionTests.cs
- [[.InvokeParseStateMarkers()]] - code - tests/AdventureEngine.Tests/GameSessionTests.cs
- [[.MakeSessionCreated()]] - code - tests/AdventureEngine.Tests/GameSessionTests.cs
- [[.ParseStateMarkers_ParsesSceneAndOutcomeMarkers()]] - code - tests/AdventureEngine.Tests/GameSessionTests.cs
- [[.ParseStateMarkers_StripsLeakedDisplayArtifacts()]] - code - tests/AdventureEngine.Tests/GameSessionTests.cs
- [[.UsageRecorded_DeserializesLegacyPayloadWithZeroCacheDefaults()]] - code - tests/AdventureEngine.Tests/GameSessionTests.cs
- [[ChapterCompleted_1]] - code
- [[Fact]] - code
- [[GameEnded_1]] - code
- [[GameSessionTests]] - code - tests/AdventureEngine.Tests/GameSessionTests.cs
- [[PlayerActed_1]] - code
- [[SceneEntered_1]] - code
- [[SessionCreated_1]] - code
- [[UsageRecorded_1]] - code
- [[nextSceneId_1]] - code
- [[outcomeWon_1]] - code
- [[response_1]] - code

## Live Query (requires Dataview plugin)

```dataview
TABLE source_file, type FROM #community/Community_4
SORT file.name ASC
```

## Connections to other communities
- 3 edges to [[_COMMUNITY_Community 2]]

## Top bridge nodes
- [[.Apply()]] - degree 15, connects to 1 community
- [[GameSessionTests]] - degree 14, connects to 1 community
- [[.MakeSessionCreated()]] - degree 10, connects to 1 community