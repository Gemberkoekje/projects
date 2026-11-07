using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using CharacterCreator.application.Void;
using CharacterCreator.core.Rules;

namespace CharacterCreator.infrastructure.Void;

/// <summary>
/// Reads VOID's answer to <c>GET /skills</c>: a <c>List</c> envelope of compact skills, each with its
/// <c>requirements</c>, and the rules for a new character as <c>creation</c> (void
/// <c>feature/skills-requirements-api</c>). The offline snapshot has the same
/// shape, with <c>source</c>, <c>taken_at</c> and <c>synthesized</c> next to the list.
/// </summary>
/// <remarks>Stateless and thread-safe.</remarks>
internal static class VoidSkillsJson
{
    private static readonly JsonSerializerOptions Json = new ()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    /// <summary>Parses the answer.</summary>
    /// <param name="json">VOID's JSON.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The envelope: the skills, and the snapshot's own fields when present.</returns>
    /// <exception cref="JsonException">The JSON is not VOID's list of skills.</exception>
    public static async Task<Envelope> ParseAsync(Stream json, CancellationToken cancellationToken)
    {
        var envelope = await JsonSerializer.DeserializeAsync<EnvelopeJson>(json, Json, cancellationToken)
            ?? throw new JsonException("VOID answered with nothing.");

        return new Envelope
        {
            Source = envelope.Source ?? string.Empty,
            TakenAt = envelope.TakenAt,
            Synthesized = envelope.Synthesized,
            Creation = envelope.Creation is { StartXp: { } start, MaxLeftOver: { } left }
                ? new CreationRules { StartXp = start, MaxLeftOver = left }
                : null,
            Skills = [.. envelope.List.Select(skill => new VoidSkill
            {
                Id = skill.Id,
                Name = skill.Name ?? string.Empty,
                Cost = skill.Cost,
                BaseMax = skill.BaseMax,
                TimesMax = skill.TimesMax,
                Deprecated = skill.Deprecated,
                Manatype = skill.Manatype?.Name ?? string.Empty,
                ManaAmount = skill.ManaAmount ?? 0,
                ImbueType = skill.ImbueType?.Name ?? string.Empty,
                ImbueCap = skill.ImbueCap ?? 0,
                Loresheet = skill.Loresheet,
                Requirements = [.. (skill.Requirements ?? []).Select(group => new VoidRequirement
                {
                    Kind = group.Kind ?? "any",
                    Amount = group.Amount ?? 1,
                    Skills = group.Skills ?? [],
                })],
            })],
        };
    }

    /// <summary>What the JSON held.</summary>
    internal sealed record Envelope
    {
        required public string Source { get; init; }

        required public DateTimeOffset? TakenAt { get; init; }

        required public bool Synthesized { get; init; }

        /// <summary>The rules for a new character, when VOID gave them; <c>null</c> for an older VOID.</summary>
        required public CreationRules Creation { get; init; }

        required public IReadOnlyList<VoidSkill> Skills { get; init; }
    }

    /// <summary>The list envelope. VOID writes null for an absent number, hence the nullable numbers.</summary>
    internal sealed record EnvelopeJson
    {
        public string Source { get; init; }

        public DateTimeOffset? TakenAt { get; init; }

        public bool Synthesized { get; init; }

        public List<SkillJson> List { get; init; } = [];

        public CreationJson Creation { get; init; }
    }

    /// <summary>GET /skills's rules for a new character.</summary>
    internal sealed record CreationJson
    {
        public decimal? StartXp { get; init; }

        public decimal? MaxLeftOver { get; init; }
    }

    internal sealed record SkillJson
    {
        public int Id { get; init; }

        public string Name { get; init; }

        public int Cost { get; init; }

        public int BaseMax { get; init; }

        public int TimesMax { get; init; }

        public bool Deprecated { get; init; }

        public bool Loresheet { get; init; }

        public int? ManaAmount { get; init; }

        public NamedJson Manatype { get; init; }

        public int? ImbueCap { get; init; }

        public NamedJson ImbueType { get; init; }

        public List<RequirementJson> Requirements { get; init; }
    }

    internal sealed record NamedJson
    {
        public string Name { get; init; }
    }

    internal sealed record RequirementJson
    {
        public string Kind { get; init; }

        public int? Amount { get; init; }

        public List<int> Skills { get; init; }
    }
}
