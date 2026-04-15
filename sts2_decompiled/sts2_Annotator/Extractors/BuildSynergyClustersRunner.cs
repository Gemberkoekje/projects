using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Sts2Extractor.Cli;
using Sts2Extractor.Infrastructure;
using Npgsql;

namespace Sts2Extractor.Extractors;

internal sealed class BuildSynergyClustersRunner
{
    private const int MinEntitiesPerArchetype = 3;

    public BuildSynergyClustersResult Run(CliOptions options)
    {
        string connectionString = DatabaseSettingsResolver.ResolveConnectionString(options);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Missing PostgreSQL connection string. Use --connection-string, Database:ConnectionString, or STS2_POSTGRES_CONNECTION_STRING.");
        }

        string clustersOutputPath = options.OutputPath;
        string outputDirectory = Path.GetDirectoryName(clustersOutputPath) ?? options.RootPath;
        string archetypesOutputPath = Path.Combine(outputDirectory, "archetypes_staging.csv");

        List<EntityClusterSourceRow> entities = LoadEntities(connectionString);

        Dictionary<ArchetypeKey, HashSet<string>> archetypeEntities = new Dictionary<ArchetypeKey, HashSet<string>>();
        Dictionary<ArchetypeKey, string> archetypeTagDisplay = new Dictionary<ArchetypeKey, string>();

        for (int i = 0; i < entities.Count; i++)
        {
            EntityClusterSourceRow entity = entities[i];
            string[] tags = entity.EffectTags
                .Where(static t => !string.IsNullOrWhiteSpace(t))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            for (int t = 0; t < tags.Length; t++)
            {
                string tag = tags[t];
                ArchetypeKey key = new ArchetypeKey(entity.CharacterId, tag);

                if (!archetypeEntities.TryGetValue(key, out HashSet<string> entityKeys))
                {
                    entityKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    archetypeEntities[key] = entityKeys;
                    archetypeTagDisplay[key] = tag;
                }

                entityKeys.Add(entity.EntityType + "::" + entity.EntityId);
            }
        }

        List<ArchetypeStagingRow> archetypes = new List<ArchetypeStagingRow>();
        List<SynergyClusterStagingRow> clusters = new List<SynergyClusterStagingRow>();

        List<ArchetypeKey> orderedKeys = archetypeEntities.Keys
            .OrderBy(static k => k.CharacterId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static k => k.Tag, StringComparer.OrdinalIgnoreCase)
            .ToList();

        for (int i = 0; i < orderedKeys.Count; i++)
        {
            ArchetypeKey key = orderedKeys[i];
            HashSet<string> members = archetypeEntities[key];
            if (members.Count < MinEntitiesPerArchetype)
            {
                continue;
            }

            string rawTag = archetypeTagDisplay[key];
            string archetypeName = BuildArchetypeName(rawTag);
            string archetypeDescription = "Entities centered on '" + rawTag + "' interactions.";

            archetypes.Add(new ArchetypeStagingRow
            {
                CharacterId = key.CharacterId,
                Name = archetypeName,
                Description = archetypeDescription,
                KeyEffectTags = rawTag
            });

            List<EntityClusterSourceRow> matchingEntities = entities
                .Where(e => string.Equals(e.CharacterId, key.CharacterId, StringComparison.OrdinalIgnoreCase)
                         && e.EffectTags.Contains(rawTag, StringComparer.OrdinalIgnoreCase))
                .OrderBy(static e => e.EntityType, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static e => e.EntityId, StringComparer.OrdinalIgnoreCase)
                .ToList();

            for (int m = 0; m < matchingEntities.Count; m++)
            {
                EntityClusterSourceRow entity = matchingEntities[m];
                double score = entity.SynergyRating > 0
                    ? Math.Round(entity.SynergyRating / 10.0, 2)
                    : 0.5;

                clusters.Add(new SynergyClusterStagingRow
                {
                    ArchetypeCharacterId = key.CharacterId,
                    ArchetypeName = archetypeName,
                    EntityType = entity.EntityType,
                    EntityId = entity.EntityId,
                    SynergyRole = ResolveRole(entity.EntityType),
                    SynergyScore = score,
                    Explanation = "Shares key tag '" + rawTag + "'."
                });
            }
        }

        CsvWriter.Write(archetypesOutputPath, ArchetypeColumns(), archetypes.Select(static r => (IReadOnlyList<string>)new[]
        {
            r.CharacterId,
            r.Name,
            r.Description,
            r.KeyEffectTags
        }));

        CsvWriter.Write(clustersOutputPath, ClusterColumns(), clusters.Select(static r => (IReadOnlyList<string>)new[]
        {
            r.ArchetypeCharacterId,
            r.ArchetypeName,
            r.EntityType,
            r.EntityId,
            r.SynergyRole,
            r.SynergyScore.ToString("0.##", CultureInfo.InvariantCulture),
            r.Explanation
        }));

        return new BuildSynergyClustersResult
        {
            ArchetypesOutputPath = archetypesOutputPath,
            ClustersOutputPath = clustersOutputPath,
            ArchetypeCount = archetypes.Count,
            ClusterCount = clusters.Count
        };
    }

    private static string[] ArchetypeColumns()
    {
        return new[]
        {
            "character_id",
            "name",
            "description",
            "key_effect_tags"
        };
    }

    private static string[] ClusterColumns()
    {
        return new[]
        {
            "archetype_character_id",
            "archetype_name",
            "entity_type",
            "entity_id",
            "synergy_role",
            "synergy_score",
            "explanation"
        };
    }

    private static string BuildArchetypeName(string rawTag)
    {
        string[] parts = rawTag
            .Split(new[] { '_', '-', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(static part => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(part.ToLowerInvariant()))
            .ToArray();

        string label = parts.Length == 0 ? rawTag : string.Join(" ", parts);
        return label + " Synergy";
    }

    private static string ResolveRole(string entityType)
    {
        if (string.Equals(entityType, "card", StringComparison.OrdinalIgnoreCase))
        {
            return "core";
        }

        if (string.Equals(entityType, "relic", StringComparison.OrdinalIgnoreCase))
        {
            return "enabler";
        }

        if (string.Equals(entityType, "potion", StringComparison.OrdinalIgnoreCase))
        {
            return "support";
        }

        return "support";
    }

    private static List<EntityClusterSourceRow> LoadEntities(string connectionString)
    {
        List<EntityClusterSourceRow> rows = new List<EntityClusterSourceRow>();

        using NpgsqlConnection connection = new NpgsqlConnection(connectionString);
        connection.Open();

        LoadEntityRows(connection, rows, "card", @"
SELECT
    'card' AS entity_type,
    c.id AS entity_id,
    COALESCE(c.character_id, '') AS character_id,
    COALESCE(c.effect_tags, ARRAY[]::text[]) AS effect_tags,
    COALESCE(esr.synergy_rating, 0) AS synergy_rating
FROM cards c
LEFT JOIN entity_strength_ratings esr
    ON esr.entity_type = 'card'
   AND esr.entity_id = c.id;");

        LoadEntityRows(connection, rows, "relic", @"
SELECT
    'relic' AS entity_type,
    r.id AS entity_id,
    COALESCE(r.character_id, '') AS character_id,
    COALESCE(r.effect_tags, ARRAY[]::text[]) AS effect_tags,
    COALESCE(esr.synergy_rating, 0) AS synergy_rating
FROM relics r
LEFT JOIN entity_strength_ratings esr
    ON esr.entity_type = 'relic'
   AND esr.entity_id = r.id;");

        LoadEntityRows(connection, rows, "potion", @"
SELECT
    'potion' AS entity_type,
    p.id AS entity_id,
    COALESCE(p.character_id, '') AS character_id,
    COALESCE(p.effect_tags, ARRAY[]::text[]) AS effect_tags,
    COALESCE(esr.synergy_rating, 0) AS synergy_rating
FROM potions p
LEFT JOIN entity_strength_ratings esr
    ON esr.entity_type = 'potion'
   AND esr.entity_id = p.id;");

        return rows;
    }

    private static void LoadEntityRows(NpgsqlConnection connection, List<EntityClusterSourceRow> rows, string expectedEntityType, string sql)
    {
        using NpgsqlCommand command = new NpgsqlCommand(sql, connection);
        using NpgsqlDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            string entityType = reader.GetString(0);
            if (!string.Equals(entityType, expectedEntityType, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string[] tags = reader.IsDBNull(3)
                ? Array.Empty<string>()
                : reader.GetFieldValue<string[]>(3) ?? Array.Empty<string>();

            rows.Add(new EntityClusterSourceRow
            {
                EntityType = entityType,
                EntityId = reader.GetString(1),
                CharacterId = reader.GetString(2),
                EffectTags = tags,
                SynergyRating = reader.IsDBNull(4) ? 0 : reader.GetInt32(4)
            });
        }
    }

    private readonly struct ArchetypeKey
    {
        public string CharacterId { get; }

        public string Tag { get; }

        public ArchetypeKey(string characterId, string tag)
        {
            CharacterId = (characterId ?? string.Empty).ToLowerInvariant();
            Tag = (tag ?? string.Empty).ToLowerInvariant();
        }
    }

    private sealed class EntityClusterSourceRow
    {
        public string EntityType { get; set; }

        public string EntityId { get; set; }

        public string CharacterId { get; set; }

        public string[] EffectTags { get; set; }

        public int SynergyRating { get; set; }

        public EntityClusterSourceRow()
        {
            EntityType = string.Empty;
            EntityId = string.Empty;
            CharacterId = string.Empty;
            EffectTags = Array.Empty<string>();
        }
    }

    private sealed class ArchetypeStagingRow
    {
        public string CharacterId { get; set; }

        public string Name { get; set; }

        public string Description { get; set; }

        public string KeyEffectTags { get; set; }

        public ArchetypeStagingRow()
        {
            CharacterId = string.Empty;
            Name = string.Empty;
            Description = string.Empty;
            KeyEffectTags = string.Empty;
        }
    }

    private sealed class SynergyClusterStagingRow
    {
        public string ArchetypeCharacterId { get; set; }

        public string ArchetypeName { get; set; }

        public string EntityType { get; set; }

        public string EntityId { get; set; }

        public string SynergyRole { get; set; }

        public double SynergyScore { get; set; }

        public string Explanation { get; set; }

        public SynergyClusterStagingRow()
        {
            ArchetypeCharacterId = string.Empty;
            ArchetypeName = string.Empty;
            EntityType = string.Empty;
            EntityId = string.Empty;
            SynergyRole = string.Empty;
            Explanation = string.Empty;
        }
    }
}
