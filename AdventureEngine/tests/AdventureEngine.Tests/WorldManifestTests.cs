using System.Text.Json;
using AdventureEngine.Domain;

namespace AdventureEngine.Tests;

public class WorldManifestTests
{
    [Fact]
    public void WorldManifest_DeserializesFromJson()
    {
        var json = """
            {
              "title": "Test World",
              "premise": "A test premise.",
              "lore": "Some lore.",
              "win_condition": "Win.",
              "lose_condition": "Lose.",
              "chapters": [
                {
                  "index": 1,
                  "title": "Chapter One",
                  "summary": "Summary.",
                  "scenes": [
                    {
                      "id": "ch1_sc1",
                      "description": "A dark room.",
                      "active_npc_ids": ["npc_guard"],
                      "exits": ["ch1_sc2"]
                    }
                  ]
                }
              ],
              "npcs": [
                {
                  "id": "npc_guard",
                  "name": "Guard",
                  "personality": "Stern",
                  "backstory": "A veteran soldier.",
                  "relationship_to_player": "Neutral",
                  "secrets": "Secretly afraid.",
                  "speech_style": "Terse"
                }
              ]
            }
            """;

        var manifest = JsonSerializer.Deserialize<WorldManifest>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        });

        Assert.NotNull(manifest);
        Assert.Equal("Test World", manifest.Title);
        Assert.Single(manifest.Chapters);
        Assert.Single(manifest.Npcs);
        Assert.Equal("ch1_sc1", manifest.Chapters[0].Scenes[0].Id);
        Assert.Equal("npc_guard", manifest.Npcs[0].Id);
    }
}
