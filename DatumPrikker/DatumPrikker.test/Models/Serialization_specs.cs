using System.Text.Json;
using System.Text.Json.Serialization;

namespace DatumPrikker.test.Models;

/// <summary>Marten stores the documents with System.Text.Json and enums as strings; they must survive the round trip.</summary>
public sealed class Serialization_specs
{
    private static readonly JsonSerializerOptions Options = new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } };

    [Test]
    public void a_closed_poll_round_trips()
    {
        var poll = Fixtures.Poll().Close(Fixtures.Owner, Fixtures.Now).Value;

        var copy = JsonSerializer.Deserialize<Poll>(JsonSerializer.Serialize(poll, Options), Options);

        copy.Should().BeEquivalentTo(poll);
        copy.IsOpen.Should().BeFalse();
    }

    [Test]
    public void an_answer_round_trips_with_its_choices_by_name()
    {
        var poll = Fixtures.Poll();
        var answer = Fixtures.Answer("Carla", "carla@example.com", poll, Availability.Yes, Availability.Maybe);

        var json = JsonSerializer.Serialize(answer, Options);
        var copy = JsonSerializer.Deserialize<Answer>(json, Options);

        json.Should().Contain("\"Maybe\"");
        copy.Should().BeEquivalentTo(answer);
    }
}
