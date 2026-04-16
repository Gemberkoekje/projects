using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Sts2Viewer.Data;

public sealed class PostgresReadService
{
    private const double WeightAffinity = 0.50;
    private const double WeightDeckFit = 0.25;
    private const double WeightAnti = 0.15;
    private const double WeightFlex = 0.10;

    private readonly string _connectionString;

    public PostgresReadService(IConfiguration configuration)
    {
        string configured = configuration["Database:ConnectionString"] ?? string.Empty;
        string env = Environment.GetEnvironmentVariable("STS2_POSTGRES_CONNECTION_STRING") ?? string.Empty;
        _connectionString = string.IsNullOrWhiteSpace(configured) ? env : configured;
    }

    public bool HasConnectionString()
    {
        return !string.IsNullOrWhiteSpace(_connectionString);
    }

    public IReadOnlyList<EntityRow> GetEntities(EntityQueryRequest request, int limit)
    {
        if (!HasConnectionString())
        {
            return new List<EntityRow>();
        }

        List<EntityRow> rows = new List<EntityRow>();

        using NpgsqlConnection connection = OpenConnection();
        using NpgsqlCommand command = new NpgsqlCommand(@"
WITH entities AS (
    SELECT
        'card'::text AS entity_type,
        'card'::text AS rating_entity_type,
        c.id AS entity_id,
        COALESCE(c.character_id, 'colorless') AS character_id,
        'exclusive'::text AS character_scope_kind,
        COALESCE(NULLIF(c.title, ''), c.id) AS title,
        COALESCE(c.description, '') AS description,
        COALESCE(c.effect_tags, ARRAY[]::text[]) AS tags,
        COALESCE(c.energy_cost, -2147483648) AS energy_cost,
        COALESCE(c.resources_generated, ARRAY[]::text[]) AS resources_generated,
        COALESCE(c.resources_consumed, ARRAY[]::text[]) AS resources_consumed
    FROM cards c

    UNION ALL

    SELECT
        'relic'::text,
        'relic'::text,
        r.id,
        COALESCE(r.character_id, ''),
        CASE WHEN r.character_id IS NULL THEN 'shared'::text ELSE 'exclusive'::text END,
        COALESCE(NULLIF(r.title, ''), r.id),
        COALESCE(r.description, ''),
        COALESCE(r.effect_tags, ARRAY[]::text[]),
        -2147483648,
        COALESCE(r.resources_generated, ARRAY[]::text[]),
        COALESCE(r.resources_consumed, ARRAY[]::text[])
    FROM relics r

    UNION ALL

    SELECT
        'potion'::text,
        'potion'::text,
        p.id,
        COALESCE(p.character_id, ''),
        CASE WHEN p.character_id IS NULL THEN 'shared'::text ELSE 'exclusive'::text END,
        COALESCE(NULLIF(p.title, ''), p.id),
        COALESCE(p.description, ''),
        COALESCE(p.effect_tags, ARRAY[]::text[]),
        -2147483648,
        ARRAY[]::text[],
        ARRAY[]::text[]
    FROM potions p

    UNION ALL

    SELECT
        'event_option'::text,
        'event'::text,
        eo.event_id || '::' || eo.option_key,
        ''::text,
        'none'::text,
        COALESCE(NULLIF(eo.title, ''), eo.option_key),
        COALESCE(NULLIF(eo.description, ''), COALESCE(eo.handler_method, '')),
        ARRAY[]::text[],
        -2147483648,
        ARRAY[]::text[],
        ARRAY[]::text[]
    FROM event_options eo
)
SELECT
    e.entity_type,
    e.entity_id,
    e.character_id,
    e.title,
    e.description,
    e.tags,
    e.energy_cost,
    e.resources_generated,
    e.resources_consumed,
    COALESCE(esr.synergy_rating, 0),
    COALESCE(esr.flexibility_rating, 0),
    COALESCE(esr.anti_synergy_rating, 0)
FROM entities e
LEFT JOIN entity_strength_ratings esr
    ON esr.entity_type = e.rating_entity_type
   AND esr.entity_id = e.entity_id
WHERE
    (@entity_type = '' OR e.entity_type = @entity_type)
    AND (@search = '' OR e.entity_id ILIKE @search_like OR e.title ILIKE @search_like OR e.description ILIKE @search_like)
    AND (@tag = '' OR @tag = ANY(e.tags))
    AND (
        @character_id = ''
        OR (e.character_scope_kind = 'exclusive' AND LOWER(e.character_id) = @character_id)
        OR (e.character_scope_kind = 'shared')
    )
    AND COALESCE(esr.synergy_rating, 0) >= @min_synergy
    AND COALESCE(esr.flexibility_rating, 0) >= @min_flexibility
    AND COALESCE(esr.anti_synergy_rating, 0) <= @max_anti_synergy
ORDER BY
    CASE WHEN @sort_by = 'synergy' AND @sort_direction = 'asc' THEN COALESCE(esr.synergy_rating, 0) END ASC,
    CASE WHEN @sort_by = 'synergy' AND @sort_direction <> 'asc' THEN COALESCE(esr.synergy_rating, 0) END DESC,
    CASE WHEN @sort_by = 'flexibility' AND @sort_direction = 'asc' THEN COALESCE(esr.flexibility_rating, 0) END ASC,
    CASE WHEN @sort_by = 'flexibility' AND @sort_direction <> 'asc' THEN COALESCE(esr.flexibility_rating, 0) END DESC,
    CASE WHEN @sort_by = 'anti' AND @sort_direction = 'asc' THEN COALESCE(esr.anti_synergy_rating, 0) END ASC,
    CASE WHEN @sort_by = 'anti' AND @sort_direction <> 'asc' THEN COALESCE(esr.anti_synergy_rating, 0) END DESC,
    CASE WHEN (@sort_by = 'title' OR @sort_by = 'name') AND @sort_direction = 'asc' THEN e.title END ASC,
    CASE WHEN (@sort_by = 'title' OR @sort_by = 'name') AND @sort_direction <> 'asc' THEN e.title END DESC,
    e.entity_type,
    e.entity_id
LIMIT @limit;", connection);

        string searchLike = request.Search.Length == 0 ? string.Empty : "%" + request.Search + "%";

        command.Parameters.AddWithValue("entity_type", request.EntityType);
        command.Parameters.AddWithValue("search", request.Search);
        command.Parameters.AddWithValue("search_like", searchLike);
        command.Parameters.AddWithValue("character_id", request.CharacterId);
        command.Parameters.AddWithValue("tag", request.Tag);
        command.Parameters.AddWithValue("sort_by", request.SortBy);
        command.Parameters.AddWithValue("sort_direction", request.SortDirection);
        command.Parameters.AddWithValue("min_synergy", request.MinSynergy);
        command.Parameters.AddWithValue("min_flexibility", request.MinFlexibility);
        command.Parameters.AddWithValue("max_anti_synergy", request.MaxAntiSynergy);
        command.Parameters.AddWithValue("limit", limit);

        using NpgsqlDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            string[] tags = reader.IsDBNull(5) ? Array.Empty<string>() : reader.GetFieldValue<string[]>(5);
            string[] resourcesGenerated = reader.IsDBNull(7) ? Array.Empty<string>() : reader.GetFieldValue<string[]>(7);
            string[] resourcesConsumed = reader.IsDBNull(8) ? Array.Empty<string>() : reader.GetFieldValue<string[]>(8);
            rows.Add(new EntityRow
            {
                EntityType = reader.GetString(0),
                EntityId = reader.GetString(1),
                CharacterId = reader.GetString(2),
                Title = reader.GetString(3),
                Description = reader.GetString(4),
                Tags = tags,
                EnergyCost = reader.GetInt32(6),
                ResourcesGenerated = resourcesGenerated,
                ResourcesConsumed = resourcesConsumed,
                SynergyRating = reader.GetInt32(9),
                FlexibilityRating = reader.GetInt32(10),
                AntiSynergyRating = reader.GetInt32(11)
            });
        }

        return rows;
    }

    public IReadOnlyList<string> GetTags(int limit)
    {
        if (!HasConnectionString())
        {
            return new List<string>();
        }

        List<string> tags = new List<string>();

        using NpgsqlConnection connection = OpenConnection();
        using NpgsqlCommand command = new NpgsqlCommand(@"
SELECT DISTINCT tag
FROM (
    SELECT unnest(COALESCE(effect_tags, ARRAY[]::text[])) AS tag FROM cards
    UNION
    SELECT unnest(COALESCE(effect_tags, ARRAY[]::text[])) AS tag FROM relics
    UNION
    SELECT unnest(COALESCE(effect_tags, ARRAY[]::text[])) AS tag FROM potions
) t
WHERE tag <> ''
ORDER BY tag
LIMIT @limit;", connection);

        command.Parameters.AddWithValue("limit", limit);

        using NpgsqlDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            tags.Add(reader.GetString(0));
        }

        return tags;
    }

    public IReadOnlyList<SynergyClusterRow> GetSynergyClusters(int limit)
    {
        if (!HasConnectionString())
        {
            return new List<SynergyClusterRow>();
        }

        List<SynergyClusterRow> rows = new List<SynergyClusterRow>();

        try
        {
            using NpgsqlConnection connection = OpenConnection();
            using NpgsqlCommand command = new NpgsqlCommand(@"
SELECT
    COALESCE(a.name, ''),
    sc.entity_type::text,
    sc.entity_id,
    COALESCE(sc.synergy_role, ''),
    COALESCE(sc.synergy_score, 0)::numeric,
    COALESCE(sc.explanation, '')
FROM synergy_clusters sc
LEFT JOIN archetypes a ON a.id = sc.archetype_id
ORDER BY sc.synergy_score DESC NULLS LAST, sc.id
LIMIT @limit;", connection);

            command.Parameters.AddWithValue("limit", limit);

            using NpgsqlDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                rows.Add(new SynergyClusterRow
                {
                    ArchetypeName = reader.GetString(0),
                    EntityType = reader.GetString(1),
                    EntityId = reader.GetString(2),
                    Role = reader.GetString(3),
                    Score = reader.GetDecimal(4),
                    Explanation = reader.GetString(5)
                });
            }
        }
        catch (PostgresException ex) when (ex.SqlState == "42P01")
        {
            // Table doesn't exist yet - synergy clustering feature not yet implemented
            // Return empty list to allow viewer to function without this feature
            return new List<SynergyClusterRow>();
        }

        return rows;
    }

    public PipelineDiagnosticsRow GetPipelineDiagnostics()
    {
        PipelineDiagnosticsRow diagnostics = new PipelineDiagnosticsRow();

        if (!HasConnectionString())
        {
            return diagnostics;
        }

        try
        {
            using NpgsqlConnection connection = OpenConnection();
            using NpgsqlCommand command = new NpgsqlCommand(@"
SELECT
    (SELECT COUNT(*) FROM synergy_clusters) AS cluster_count,
    (SELECT COUNT(*) FROM entity_strength_ratings) AS rating_count,
    (SELECT COUNT(*) FROM entity_archetype_affinity) AS affinity_count;", connection);

            using NpgsqlDataReader reader = command.ExecuteReader();
            if (reader.Read())
            {
                diagnostics.SynergyClusterCount = reader.GetInt32(0);
                diagnostics.EntityStrengthRatingCount = reader.GetInt32(1);
                diagnostics.EntityArchetypeAffinityCount = reader.GetInt32(2);
            }
        }
        catch (PostgresException ex) when (ex.SqlState == "42P01")
        {
            return diagnostics;
        }

        return diagnostics;
    }

    public IReadOnlyList<CharacterRow> GetCharacters()
    {
        if (!HasConnectionString())
        {
            return new List<CharacterRow>();
        }

        List<CharacterRow> rows = new List<CharacterRow>();
        using NpgsqlConnection connection = OpenConnection();
        using NpgsqlCommand command = new NpgsqlCommand(@"
SELECT id, COALESCE(NULLIF(title, ''), id)
FROM characters
ORDER BY id;", connection);

        using NpgsqlDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(new CharacterRow
            {
                Id = reader.GetString(0),
                Title = reader.GetString(1)
            });
        }

        rows.Add(new CharacterRow { Id = "colorless", Title = "Colorless" });
        return rows;
    }

    public IReadOnlyList<ArchetypeRow> GetArchetypes(string characterId)
    {
        if (!HasConnectionString())
        {
            return new List<ArchetypeRow>();
        }

        List<ArchetypeRow> rows = new List<ArchetypeRow>();

        using NpgsqlConnection connection = OpenConnection();
        using NpgsqlCommand command = new NpgsqlCommand(@"
SELECT id, COALESCE(character_id, ''), COALESCE(name, ''), COALESCE(description, ''), COALESCE(key_effect_tags, ARRAY[]::text[])
FROM archetypes
WHERE (@character_id = '' OR LOWER(COALESCE(character_id, '')) = LOWER(@character_id))
ORDER BY name;", connection);

        command.Parameters.AddWithValue("character_id", characterId ?? string.Empty);

        using NpgsqlDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(new ArchetypeRow
            {
                Id = reader.GetInt32(0),
                CharacterId = reader.GetString(1),
                Name = reader.GetString(2),
                Description = reader.GetString(3),
                KeyEffectTags = reader.GetFieldValue<string[]>(4)
            });
        }

        return rows;
    }

    public IReadOnlyList<EntityOptionRow> GetCardOptions(string characterId, bool includeColorless)
    {
        if (!HasConnectionString())
        {
            return new List<EntityOptionRow>();
        }

        List<EntityOptionRow> rows = new List<EntityOptionRow>();

        using NpgsqlConnection connection = OpenConnection();
        using NpgsqlCommand command = new NpgsqlCommand(@"
SELECT
    id,
    COALESCE(character_id, ''),
    COALESCE(NULLIF(title, ''), id),
    COALESCE(effect_tags, ARRAY[]::text[])
FROM cards
WHERE
    (@character_id = '')
    OR LOWER(COALESCE(character_id, '')) = LOWER(@character_id)
    OR (@include_colorless = TRUE AND character_id IS NULL)
ORDER BY id;", connection);

        command.Parameters.AddWithValue("character_id", characterId ?? string.Empty);
        command.Parameters.AddWithValue("include_colorless", includeColorless);

        using NpgsqlDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(new EntityOptionRow
            {
                EntityType = "card",
                EntityId = reader.GetString(0),
                CharacterId = reader.GetString(1),
                Title = reader.GetString(2),
                Tags = reader.GetFieldValue<string[]>(3)
            });
        }

        return rows;
    }

    public IReadOnlyList<EntityOptionRow> GetRelicOptions(string characterId)
    {
        if (!HasConnectionString())
        {
            return new List<EntityOptionRow>();
        }

        List<EntityOptionRow> rows = new List<EntityOptionRow>();

        using NpgsqlConnection connection = OpenConnection();
        using NpgsqlCommand command = new NpgsqlCommand(@"
SELECT
    id,
    COALESCE(character_id, ''),
    COALESCE(NULLIF(title, ''), id),
    COALESCE(effect_tags, ARRAY[]::text[])
FROM relics
WHERE
    (@character_id = '')
    OR LOWER(COALESCE(character_id, '')) = LOWER(@character_id)
    OR character_id IS NULL
ORDER BY id;", connection);

        command.Parameters.AddWithValue("character_id", characterId ?? string.Empty);

        using NpgsqlDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(new EntityOptionRow
            {
                EntityType = "relic",
                EntityId = reader.GetString(0),
                CharacterId = reader.GetString(1),
                Title = reader.GetString(2),
                Tags = reader.GetFieldValue<string[]>(3)
            });
        }

        return rows;
    }

    public IReadOnlyList<EntityArchetypeAffinityRow> GetEntityArchetypeAffinities(string entityType, string entityId)
    {
        if (!HasConnectionString())
        {
            return new List<EntityArchetypeAffinityRow>();
        }

        List<EntityArchetypeAffinityRow> rows = new List<EntityArchetypeAffinityRow>();

        try
        {
            using NpgsqlConnection connection = OpenConnection();
            using NpgsqlCommand command = new NpgsqlCommand(@"
SELECT
    ea.archetype_id,
    COALESCE(a.name, ''),
    COALESCE(ea.affinity_score, 0),
    COALESCE(a.character_id, '')
FROM entity_archetype_affinity ea
LEFT JOIN archetypes a ON a.id = ea.archetype_id
WHERE LOWER(ea.entity_type) = LOWER(@entity_type)
  AND ea.entity_id = @entity_id
ORDER BY ea.affinity_score DESC, a.name;", connection);

            command.Parameters.AddWithValue("entity_type", entityType);
            command.Parameters.AddWithValue("entity_id", entityId);

            using NpgsqlDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                rows.Add(new EntityArchetypeAffinityRow
                {
                    ArchetypeId = reader.GetInt32(0),
                    ArchetypeName = reader.GetString(1),
                    AffinityScore = reader.GetInt32(2),
                    CharacterId = reader.GetString(3)
                });
            }
        }
        catch (PostgresException ex) when (ex.SqlState == "42P01")
        {
            return new List<EntityArchetypeAffinityRow>();
        }

        return rows;
    }

    public IReadOnlyList<SynergyClusterRow> GetSynergyClustersForEntity(string entityType, string entityId)
    {
        if (!HasConnectionString())
        {
            return new List<SynergyClusterRow>();
        }

        List<SynergyClusterRow> rows = new List<SynergyClusterRow>();

        try
        {
            using NpgsqlConnection connection = OpenConnection();
            using NpgsqlCommand command = new NpgsqlCommand(@"
SELECT
    COALESCE(a.name, ''),
    sc.entity_type::text,
    sc.entity_id,
    COALESCE(sc.synergy_role, ''),
    COALESCE(sc.synergy_score, 0)::numeric,
    COALESCE(sc.explanation, '')
FROM synergy_clusters sc
LEFT JOIN archetypes a ON a.id = sc.archetype_id
WHERE LOWER(COALESCE(sc.entity_type, '')) = LOWER(@entity_type)
  AND sc.entity_id = @entity_id
ORDER BY sc.synergy_score DESC NULLS LAST, sc.id;", connection);

            command.Parameters.AddWithValue("entity_type", entityType);
            command.Parameters.AddWithValue("entity_id", entityId);

            using NpgsqlDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                rows.Add(new SynergyClusterRow
                {
                    ArchetypeName = reader.GetString(0),
                    EntityType = reader.GetString(1),
                    EntityId = reader.GetString(2),
                    Role = reader.GetString(3),
                    Score = reader.GetDecimal(4),
                    Explanation = reader.GetString(5)
                });
            }
        }
        catch (PostgresException ex) when (ex.SqlState == "42P01")
        {
            return new List<SynergyClusterRow>();
        }

        return rows;
    }

    public IReadOnlyList<PickScoreRow> ScorePickOptions(int archetypeId, string[] deckIds, string[] relicIds, string[] optionIds)
    {
        if (!HasConnectionString() || optionIds.Length == 0)
        {
            return new List<PickScoreRow>();
        }

        string[] allHeld = deckIds.Concat(relicIds).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        Dictionary<string, string> optionTypes = ResolveEntityTypes(optionIds);

        Dictionary<string, double> archetypeAffinity = archetypeId > 0
            ? LoadOptionAffinity(new[] { archetypeId }, optionIds, optionTypes)
            : new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        int[] investedArchetypes = deckIds.Length > 0
            ? LoadInvestedArchetypes(deckIds)
            : Array.Empty<int>();

        Dictionary<string, double> deckAffinityFit = investedArchetypes.Length > 0
            ? LoadOptionDeckAffinityFit(investedArchetypes, optionIds, optionTypes)
            : new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        Dictionary<string, double> flex = LoadFlexibilityScores(optionIds);
        HashSet<string> heldEffectTags = LoadHeldEffectTags(deckIds, relicIds);
        Dictionary<string, string[]> antiTags = LoadOptionAntiSynergyTags(optionIds, optionTypes);

        List<PickScoreRow> scores = new List<PickScoreRow>();
        foreach (string optionId in optionIds)
        {
            double affinity = archetypeAffinity.TryGetValue(optionId, out double af) ? af : 0.0;
            double deckFit = deckAffinityFit.TryGetValue(optionId, out double df) ? df : 0.0;

            double antiPenalty = 0.0;
            string[] cardAntiTags = antiTags.TryGetValue(optionId, out string[] at) ? at : Array.Empty<string>();
            int tagOverlap = cardAntiTags.Count(t => heldEffectTags.Contains(t));
            antiPenalty = Math.Min(tagOverlap * 0.1, 1.0);

            double flexScore = flex.TryGetValue(optionId, out double fs) ? fs : 0.5;
            double composite = (WeightAffinity * affinity) + (WeightDeckFit * deckFit) - (WeightAnti * antiPenalty) + (WeightFlex * flexScore);
            composite = Math.Max(0.0, composite);

            scores.Add(new PickScoreRow
            {
                EntityId = optionId,
                EntityType = optionTypes.TryGetValue(optionId, out string et) ? et : "unknown",
                EdgeOverlapScore = deckFit,
                ArchetypeFitScore = affinity,
                AntiSynergyPenalty = antiPenalty,
                FlexibilityScore = flexScore,
                CompositeScore = composite,
                SynergyDrivers = new List<string>()
            });
        }

        List<PickScoreRow> ordered = scores
            .OrderByDescending(s => s.CompositeScore)
            .ThenBy(s => s.EntityId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        for (int i = 0; i < ordered.Count; i++)
        {
            ordered[i].Rank = i + 1;
        }

        return ordered;
    }

    private NpgsqlConnection OpenConnection()
    {
        NpgsqlConnection connection = new NpgsqlConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private Dictionary<string, string> ResolveEntityTypes(string[] optionIds)
    {
        Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using NpgsqlConnection connection = OpenConnection();

        ReadEntityType(connection, result, "SELECT id FROM cards WHERE id = ANY(@ids)", optionIds, "card");
        ReadEntityType(connection, result, "SELECT id FROM relics WHERE id = ANY(@ids)", optionIds, "relic");
        ReadEntityType(connection, result, "SELECT id FROM potions WHERE id = ANY(@ids)", optionIds, "potion");

        foreach (string optionId in optionIds)
        {
            if (!result.ContainsKey(optionId))
            {
                result[optionId] = "unknown";
            }
        }

        return result;
    }

    private static void ReadEntityType(NpgsqlConnection connection, Dictionary<string, string> result, string sql, string[] ids, string entityType)
    {
        using NpgsqlCommand command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("ids", ids);
        using NpgsqlDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            string id = reader.GetString(0);
            if (!result.ContainsKey(id))
            {
                result[id] = entityType;
            }
        }
    }

    private Dictionary<string, double> LoadOptionAffinity(int[] archetypeIds, string[] optionIds, Dictionary<string, string> optionTypes)
    {
        Dictionary<string, double> result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        if (archetypeIds.Length == 0)
        {
            return result;
        }

        Dictionary<string, double> map = LoadAffinityRows(archetypeIds, optionIds);
        for (int i = 0; i < optionIds.Length; i++)
        {
            string optionId = optionIds[i];
            string optionType = optionTypes.TryGetValue(optionId, out string type) ? type : "unknown";
            string key = BuildAffinityKey(archetypeIds[0], optionType, optionId);
            if (map.TryGetValue(key, out double score))
            {
                result[optionId] = score;
            }
        }

        return result;
    }

    private int[] LoadInvestedArchetypes(string[] deckIds)
    {
        List<int> rows = new List<int>();
        using NpgsqlConnection connection = OpenConnection();
        using NpgsqlCommand command = new NpgsqlCommand(@"
SELECT archetype_id
FROM entity_archetype_affinity
WHERE entity_type = 'card'
  AND entity_id = ANY(@deck)
  AND affinity_score >= 5
GROUP BY archetype_id
HAVING COUNT(*) >= 2
ORDER BY archetype_id;", connection);

        command.Parameters.AddWithValue("deck", deckIds);

        using NpgsqlDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(reader.GetInt32(0));
        }

        return rows.ToArray();
    }

    private Dictionary<string, double> LoadOptionDeckAffinityFit(int[] archetypeIds, string[] optionIds, Dictionary<string, string> optionTypes)
    {
        Dictionary<string, double> result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, double> map = LoadAffinityRows(archetypeIds, optionIds);

        for (int i = 0; i < optionIds.Length; i++)
        {
            string optionId = optionIds[i];
            string optionType = optionTypes.TryGetValue(optionId, out string type) ? type : "unknown";

            double sum = 0.0;
            int count = 0;
            for (int a = 0; a < archetypeIds.Length; a++)
            {
                string key = BuildAffinityKey(archetypeIds[a], optionType, optionId);
                if (map.TryGetValue(key, out double score))
                {
                    sum += score;
                    count++;
                }
            }

            result[optionId] = count == 0 ? 0.0 : sum / count;
        }

        return result;
    }

    private Dictionary<string, double> LoadAffinityRows(int[] archetypeIds, string[] optionIds)
    {
        Dictionary<string, double> result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        using NpgsqlConnection connection = OpenConnection();
        using NpgsqlCommand command = new NpgsqlCommand(@"
SELECT archetype_id, entity_type, entity_id, affinity_score
FROM entity_archetype_affinity
WHERE archetype_id = ANY(@archetype_ids)
  AND entity_id = ANY(@entity_ids);", connection);

        command.Parameters.AddWithValue("archetype_ids", archetypeIds);
        command.Parameters.AddWithValue("entity_ids", optionIds);

        using NpgsqlDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            int archetypeId = reader.GetInt32(0);
            string entityType = reader.GetString(1);
            string entityId = reader.GetString(2);
            int score = reader.IsDBNull(3) ? 0 : reader.GetInt32(3);
            result[BuildAffinityKey(archetypeId, entityType, entityId)] = score / 10.0;
        }

        return result;
    }

    private static string BuildAffinityKey(int archetypeId, string entityType, string entityId)
    {
        return archetypeId.ToString(System.Globalization.CultureInfo.InvariantCulture) + "::" + entityType + "::" + entityId;
    }

    private Dictionary<string, double> LoadFlexibilityScores(string[] optionIds)
    {
        Dictionary<string, double> result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        using NpgsqlConnection connection = OpenConnection();
        using NpgsqlCommand command = new NpgsqlCommand("SELECT entity_id, flexibility_rating FROM entity_strength_ratings WHERE entity_id = ANY(@ids)", connection);

        command.Parameters.AddWithValue("ids", optionIds);

        using NpgsqlDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            string entityId = reader.GetString(0);
            int rating = reader.IsDBNull(1) ? 5 : reader.GetInt32(1);
            if (!result.ContainsKey(entityId))
            {
                result[entityId] = rating / 10.0;
            }
        }

        return result;
    }

    private HashSet<string> LoadHeldEffectTags(string[] deckIds, string[] relicIds)
    {
        HashSet<string> tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using NpgsqlConnection connection = OpenConnection();

        if (deckIds.Length > 0)
        {
            CollectTags(connection, "SELECT effect_tags FROM cards WHERE id = ANY(@ids)", deckIds, tags);
        }

        if (relicIds.Length > 0)
        {
            CollectTags(connection, "SELECT effect_tags FROM relics WHERE id = ANY(@ids)", relicIds, tags);
        }

        return tags;
    }

    private static void CollectTags(NpgsqlConnection connection, string sql, string[] ids, HashSet<string> target)
    {
        using NpgsqlCommand command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("ids", ids);
        using NpgsqlDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (!reader.IsDBNull(0))
            {
                string[] rowTags = reader.GetFieldValue<string[]>(0);
                foreach (string tag in rowTags)
                {
                    target.Add(tag);
                }
            }
        }
    }

    private Dictionary<string, string[]> LoadOptionAntiSynergyTags(string[] optionIds, Dictionary<string, string> optionTypes)
    {
        Dictionary<string, string[]> result = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        string[] cardOptions = optionIds
            .Where(id => optionTypes.TryGetValue(id, out string entityType) && string.Equals(entityType, "card", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (cardOptions.Length == 0)
        {
            return result;
        }

        using NpgsqlConnection connection = OpenConnection();
        using NpgsqlCommand command = new NpgsqlCommand("SELECT id, anti_synergy_tags FROM cards WHERE id = ANY(@ids)", connection);

        command.Parameters.AddWithValue("ids", cardOptions);

        using NpgsqlDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            string id = reader.GetString(0);
            string[] tags = reader.IsDBNull(1) ? Array.Empty<string>() : reader.GetFieldValue<string[]>(1);
            result[id] = tags;
        }

        return result;
    }

    public EntityArchetypeAffinityRow GetHighestSynergyArchetype(string entityType, string entityId)
    {
        if (!HasConnectionString())
        {
            return new EntityArchetypeAffinityRow();
        }

        try
        {
            using NpgsqlConnection connection = OpenConnection();
            using NpgsqlCommand command = new NpgsqlCommand(@"
SELECT
    ea.archetype_id,
    COALESCE(a.name, ''),
    COALESCE(ea.affinity_score, 0),
    COALESCE(a.character_id, '')
FROM entity_archetype_affinity ea
LEFT JOIN archetypes a ON a.id = ea.archetype_id
WHERE LOWER(ea.entity_type) = LOWER(@entity_type)
  AND ea.entity_id = @entity_id
ORDER BY ea.affinity_score DESC NULLS LAST
LIMIT 1;", connection);

            command.Parameters.AddWithValue("entity_type", entityType);
            command.Parameters.AddWithValue("entity_id", entityId);

            using NpgsqlDataReader reader = command.ExecuteReader();
            if (reader.Read())
            {
                return new EntityArchetypeAffinityRow
                {
                    ArchetypeId = reader.GetInt32(0),
                    ArchetypeName = reader.GetString(1),
                    AffinityScore = reader.GetInt32(2),
                    CharacterId = reader.GetString(3)
                };
            }
        }
        catch (PostgresException ex) when (ex.SqlState == "42P01")
        {
            return new EntityArchetypeAffinityRow();
        }

        return new EntityArchetypeAffinityRow();
    }

    public IReadOnlyList<EntityRow> GetEntitiesSynergizingWithArchetype(int archetypeId, int minAffinityScore, int limit)
    {
        if (!HasConnectionString())
        {
            return new List<EntityRow>();
        }

        List<EntityRow> rows = new List<EntityRow>();

        try
        {
            using NpgsqlConnection connection = OpenConnection();
            using NpgsqlCommand command = new NpgsqlCommand(@"
WITH entities AS (
    SELECT
        'card'::text AS entity_type,
        'card'::text AS rating_entity_type,
        c.id AS entity_id,
        COALESCE(c.character_id, 'colorless') AS character_id,
        COALESCE(NULLIF(c.title, ''), c.id) AS title,
        COALESCE(c.description, '') AS description,
        COALESCE(c.effect_tags, ARRAY[]::text[]) AS tags,
        COALESCE(c.energy_cost, -2147483648) AS energy_cost,
        COALESCE(c.resources_generated, ARRAY[]::text[]) AS resources_generated,
        COALESCE(c.resources_consumed, ARRAY[]::text[]) AS resources_consumed
    FROM cards c

    UNION ALL

    SELECT
        'relic'::text,
        'relic'::text,
        r.id,
        COALESCE(r.character_id, ''),
        COALESCE(NULLIF(r.title, ''), r.id),
        COALESCE(r.description, ''),
        COALESCE(r.effect_tags, ARRAY[]::text[]),
        -2147483648,
        COALESCE(r.resources_generated, ARRAY[]::text[]),
        COALESCE(r.resources_consumed, ARRAY[]::text[])
    FROM relics r

    UNION ALL

    SELECT
        'potion'::text,
        'potion'::text,
        p.id,
        COALESCE(p.character_id, ''),
        COALESCE(NULLIF(p.title, ''), p.id),
        COALESCE(p.description, ''),
        COALESCE(p.effect_tags, ARRAY[]::text[]),
        -2147483648,
        ARRAY[]::text[],
        ARRAY[]::text[]
    FROM potions p
)
SELECT
    e.entity_type,
    e.entity_id,
    e.character_id,
    e.title,
    e.description,
    e.tags,
    e.energy_cost,
    e.resources_generated,
    e.resources_consumed,
    ea.affinity_score,
    COALESCE(esr.synergy_rating, 0),
    COALESCE(esr.flexibility_rating, 0),
    COALESCE(esr.anti_synergy_rating, 0)
FROM entities e
INNER JOIN entity_archetype_affinity ea
    ON ea.archetype_id = @archetype_id
   AND LOWER(ea.entity_type) = LOWER(e.entity_type)
   AND ea.entity_id = e.entity_id
   AND ea.affinity_score >= @min_affinity_score
LEFT JOIN entity_strength_ratings esr
    ON esr.entity_type = e.rating_entity_type
   AND esr.entity_id = e.entity_id
ORDER BY
    ea.affinity_score DESC,
    e.entity_type,
    e.entity_id
LIMIT @limit;", connection);

            command.Parameters.AddWithValue("archetype_id", archetypeId);
            command.Parameters.AddWithValue("min_affinity_score", minAffinityScore);
            command.Parameters.AddWithValue("limit", limit);

            using NpgsqlDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                string[] tags = reader.IsDBNull(5) ? Array.Empty<string>() : reader.GetFieldValue<string[]>(5);
                string[] resourcesGenerated = reader.IsDBNull(7) ? Array.Empty<string>() : reader.GetFieldValue<string[]>(7);
                string[] resourcesConsumed = reader.IsDBNull(8) ? Array.Empty<string>() : reader.GetFieldValue<string[]>(8);
                rows.Add(new EntityRow
                {
                    EntityType = reader.GetString(0),
                    EntityId = reader.GetString(1),
                    CharacterId = reader.GetString(2),
                    Title = reader.GetString(3),
                    Description = reader.GetString(4),
                    Tags = tags,
                    EnergyCost = reader.GetInt32(6),
                    ResourcesGenerated = resourcesGenerated,
                    ResourcesConsumed = resourcesConsumed,
                    AffinityScore = reader.GetInt32(9),
                    SynergyRating = reader.GetInt32(10),
                    FlexibilityRating = reader.GetInt32(11),
                    AntiSynergyRating = reader.GetInt32(12)
                });
            }
        }
        catch (PostgresException ex) when (ex.SqlState == "42P01")
        {
            return new List<EntityRow>();
        }

        return rows;
    }

    public DeckDrawProfile LoadDeckDrawProfile(string[] deckIds, string[] relicIds)
    {
        DeckDrawProfile profile = new DeckDrawProfile { TotalCards = deckIds.Length };

        if (!HasConnectionString())
        {
            return profile;
        }

        using NpgsqlConnection connection = OpenConnection();

        if (deckIds.Length > 0)
        {
            using NpgsqlCommand command = new NpgsqlCommand(@"
SELECT
    COUNT(*) FILTER (WHERE LOWER(type::text) = 'power'),
    COUNT(*) FILTER (WHERE keywords @> ARRAY['draw'] OR effect_tags @> ARRAY['draw'] OR effect_tags @> ARRAY['card_draw']),
    COUNT(*) FILTER (WHERE keywords @> ARRAY['exhaust'] OR keywords @> ARRAY['ethereal'] OR effect_tags @> ARRAY['exhaust'] OR effect_tags @> ARRAY['self_exhaust'])
FROM cards
WHERE id = ANY(@ids);", connection);

            command.Parameters.AddWithValue("ids", deckIds);

            using NpgsqlDataReader reader = command.ExecuteReader();
            if (reader.Read())
            {
                profile.PowerCards = reader.GetInt32(0);
                profile.DrawCards = reader.GetInt32(1);
                profile.ExhaustCards = reader.GetInt32(2);
            }
        }

        if (relicIds.Length > 0)
        {
            using NpgsqlCommand relicCommand = new NpgsqlCommand(@"
SELECT COUNT(*)
FROM relics
WHERE id = ANY(@ids)
  AND (effect_tags @> ARRAY['draw'] OR effect_tags @> ARRAY['card_draw'] OR effect_tags @> ARRAY['extra_draw']);", connection);

            relicCommand.Parameters.AddWithValue("ids", relicIds);
            object result = relicCommand.ExecuteScalar();
            profile.DrawRelics = result is long l ? (int)l : 0;
        }

        return profile;
    }
}
