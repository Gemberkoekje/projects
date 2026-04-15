using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using Sts2Extractor.Annotation;
using Sts2Extractor.Annotation.Providers;
using Sts2Extractor.Cli;
using Sts2Extractor.Infrastructure;
using Npgsql;

namespace Sts2Extractor.Extractors;

internal sealed class AdvisePickRunner
{
    private const double WeightAffinity = 0.50;
    private const double WeightDeckFit = 0.25;
    private const double WeightAnti = 0.15;
    private const double WeightFlex = 0.10;

    public AdvisePickResult Run(CliOptions options)
    {
        string connectionString = DatabaseSettingsResolver.ResolveConnectionString(options);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Missing PostgreSQL connection string. Use --connection-string, Database:ConnectionString, or STS2_POSTGRES_CONNECTION_STRING.");
        }

        int archetypeId = ResolveArchetypeId(connectionString, options.ArchetypeRef);

        string[] allHeld = options.DeckIds.Concat(options.RelicIds).ToArray();
        int heldSize = Math.Max(allHeld.Length, 1);

        Dictionary<string, string> optionTypes = ResolveEntityTypes(connectionString, options.OptionIds);

        List<SynergyEdgeRecord> edges = allHeld.Length > 0
            ? LoadEdges(connectionString, options.OptionIds, allHeld)
            : new List<SynergyEdgeRecord>();

        Dictionary<string, double> archetypeAffinity = archetypeId > 0
            ? LoadOptionAffinity(connectionString, new[] { archetypeId }, options.OptionIds, optionTypes)
            : new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        int[] investedArchetypes = options.DeckIds.Length > 0
            ? LoadInvestedArchetypes(connectionString, options.DeckIds)
            : Array.Empty<int>();

        Dictionary<string, double> deckAffinityFit = investedArchetypes.Length > 0
            ? LoadOptionDeckAffinityFit(connectionString, investedArchetypes, options.OptionIds, optionTypes)
            : new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        Dictionary<string, double> flexScores = LoadFlexibilityScores(connectionString, options.OptionIds);

        HashSet<string> heldEffectTags = allHeld.Length > 0
            ? LoadHeldEffectTags(connectionString, options.DeckIds, options.RelicIds)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        Dictionary<string, string[]> optionAntiTags = LoadOptionAntiSynergyTags(connectionString, options.OptionIds, optionTypes);

        List<PickScoreRecord> scores = new List<PickScoreRecord>();
        foreach (string optionId in options.OptionIds)
        {
            List<SynergyEdgeRecord> optionEdges = edges
                .Where(e => string.Equals(e.EntityAId, optionId, StringComparison.OrdinalIgnoreCase)
                         || string.Equals(e.EntityBId, optionId, StringComparison.OrdinalIgnoreCase))
                .ToList();

            double affinity = archetypeAffinity.TryGetValue(optionId, out double af) ? af : 0.0;
            double deckFit = deckAffinityFit.TryGetValue(optionId, out double df) ? df : 0.0;

            double antiEdgeSum = optionEdges
                .Where(e => e.IsAntiSynergy)
                .Sum(e => (double)e.SynergyStrength);
            double antiPenalty = Math.Min(antiEdgeSum / (heldSize * 10.0), 1.0);

            string[] antiTags = optionAntiTags.TryGetValue(optionId, out string[] at) ? at : Array.Empty<string>();
            int tagOverlaps = antiTags.Count(t => heldEffectTags.Contains(t));
            antiPenalty = Math.Min(antiPenalty + (tagOverlaps * 0.1), 1.0);

            double flex = flexScores.TryGetValue(optionId, out double fs) ? fs : 0.5;

            double composite = (WeightAffinity * affinity) + (WeightDeckFit * deckFit) - (WeightAnti * antiPenalty) + (WeightFlex * flex);
            composite = Math.Max(0.0, composite);

            List<string> drivers = new List<string>();
            foreach (SynergyEdgeRecord edge in optionEdges.Where(e => !e.IsAntiSynergy))
            {
                string driverId = string.Equals(edge.EntityAId, optionId, StringComparison.OrdinalIgnoreCase)
                    ? edge.EntityBId
                    : edge.EntityAId;
                string explanation = string.IsNullOrWhiteSpace(edge.Explanation) ? string.Empty : $" ({edge.Explanation})";
                drivers.Add($"{driverId}{explanation}");
            }

            scores.Add(new PickScoreRecord
            {
                EntityId = optionId,
                EntityType = optionTypes.TryGetValue(optionId, out string et) ? et : "unknown",
                EdgeOverlapScore = deckFit,
                ArchetypeFitScore = affinity,
                AntiSynergyPenalty = antiPenalty,
                FlexibilityScore = flex,
                CompositeScore = composite,
                SynergyDrivers = string.Join("; ", drivers),
                Explanation = string.Empty
            });
        }

        scores.Sort(static (a, b) => b.CompositeScore.CompareTo(a.CompositeScore));
        for (int i = 0; i < scores.Count; i++)
        {
            scores[i].Rank = i + 1;
        }

        PrintResults(scores);

        string llmNarrative = string.Empty;
        if (options.ExplainPick && scores.Count > 0)
        {
            llmNarrative = GenerateNarrative(scores, options);
            Console.WriteLine();
            Console.WriteLine("── LLM Explanation ──");
            Console.WriteLine(llmNarrative);
        }

        return new AdvisePickResult { Scores = scores, LlmNarrative = llmNarrative };
    }

    private static void PrintResults(List<PickScoreRecord> scores)
    {
        foreach (PickScoreRecord score in scores)
        {
            Console.WriteLine(
                $"Rank {score.Rank}: {score.EntityId,-20} score={score.CompositeScore * 10:F2}  " +
                $"(deck={score.EdgeOverlapScore * 10:F1}, arch={score.ArchetypeFitScore * 10:F0}, " +
                $"anti={score.AntiSynergyPenalty * 10:F0}, flex={score.FlexibilityScore * 10:F0})");
            if (!string.IsNullOrWhiteSpace(score.SynergyDrivers))
            {
                Console.WriteLine($"  → Synergizes with: {score.SynergyDrivers}");
            }
        }
    }

    private static int ResolveArchetypeId(string connectionString, string archetypeRef)
    {
        if (string.IsNullOrWhiteSpace(archetypeRef))
        {
            return 0;
        }

        if (int.TryParse(archetypeRef, NumberStyles.Integer, CultureInfo.InvariantCulture, out int numericId))
        {
            return numericId;
        }

        using NpgsqlConnection conn = new NpgsqlConnection(connectionString);
        conn.Open();
        using NpgsqlCommand cmd = new NpgsqlCommand("SELECT id FROM archetypes WHERE LOWER(name) = LOWER(@name) LIMIT 1", conn);
        cmd.Parameters.AddWithValue("name", archetypeRef);
        object result = cmd.ExecuteScalar();
        return result is int id ? id : 0;
    }

    private static Dictionary<string, string> ResolveEntityTypes(string connectionString, string[] optionIds)
    {
        Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using NpgsqlConnection conn = new NpgsqlConnection(connectionString);
        conn.Open();

        ReadEntityTypeFromTable(conn, result, "SELECT id FROM cards WHERE id = ANY(@ids)", optionIds, "card");
        ReadEntityTypeFromTable(conn, result, "SELECT id FROM relics WHERE id = ANY(@ids)", optionIds, "relic");
        ReadEntityTypeFromTable(conn, result, "SELECT id FROM potions WHERE id = ANY(@ids)", optionIds, "potion");

        foreach (string id in optionIds)
        {
            if (!result.ContainsKey(id))
            {
                result[id] = "unknown";
            }
        }

        return result;
    }

    private static void ReadEntityTypeFromTable(NpgsqlConnection conn, Dictionary<string, string> result, string sql, string[] ids, string entityType)
    {
        using NpgsqlCommand cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("ids", ids);
        using NpgsqlDataReader reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            string id = reader.GetString(0);
            if (!result.ContainsKey(id))
            {
                result[id] = entityType;
            }
        }
    }

    private static List<SynergyEdgeRecord> LoadEdges(string connectionString, string[] optionIds, string[] heldIds)
    {
        List<SynergyEdgeRecord> result = new List<SynergyEdgeRecord>();
        using NpgsqlConnection conn = new NpgsqlConnection(connectionString);
        conn.Open();

        const string sql = @"
            SELECT entity_a_type, entity_a_id, entity_b_type, entity_b_id,
                   synergy_strength, is_anti_synergy, shared_tags, explanation
            FROM entity_synergy_edges
            WHERE (entity_a_id = ANY(@options) AND entity_b_id = ANY(@held))
               OR (entity_b_id = ANY(@options) AND entity_a_id = ANY(@held))";

        using NpgsqlCommand cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("options", optionIds);
        cmd.Parameters.AddWithValue("held", heldIds);

        using NpgsqlDataReader reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new SynergyEdgeRecord
            {
                EntityAType = reader.GetString(0),
                EntityAId = reader.GetString(1),
                EntityBType = reader.GetString(2),
                EntityBId = reader.GetString(3),
                SynergyStrength = reader.GetInt32(4),
                IsAntiSynergy = reader.GetBoolean(5),
                SharedTags = reader.IsDBNull(6) ? string.Empty : string.Join(";", reader.GetFieldValue<string[]>(6)),
                Explanation = reader.IsDBNull(7) ? string.Empty : reader.GetString(7)
            });
        }

        return result;
    }

    private static Dictionary<string, double> LoadOptionAffinity(
        string connectionString,
        int[] archetypeIds,
        string[] optionIds,
        Dictionary<string, string> optionTypes)
    {
        Dictionary<string, double> result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        if (archetypeIds.Length == 0)
        {
            return result;
        }

        Dictionary<string, double> map = LoadAffinityRows(connectionString, archetypeIds, optionIds);

        for (int i = 0; i < optionIds.Length; i++)
        {
            string optionId = optionIds[i];
            string optionType = optionTypes.TryGetValue(optionId, out string value) ? value : "unknown";
            string key = BuildAffinityKey(archetypeIds[0], optionType, optionId);
            if (map.TryGetValue(key, out double score))
            {
                result[optionId] = score;
            }
        }

        return result;
    }

    private static int[] LoadInvestedArchetypes(string connectionString, string[] deckIds)
    {
        List<int> result = new List<int>();
        using NpgsqlConnection conn = new NpgsqlConnection(connectionString);
        conn.Open();

        const string sql = @"
SELECT archetype_id
FROM entity_archetype_affinity
WHERE entity_type = 'card'
  AND entity_id = ANY(@deck)
  AND affinity_score >= 5
GROUP BY archetype_id
HAVING COUNT(*) >= 2
ORDER BY archetype_id;";

        using NpgsqlCommand cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("deck", deckIds);

        using NpgsqlDataReader reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            result.Add(reader.GetInt32(0));
        }

        return result.ToArray();
    }

    private static Dictionary<string, double> LoadOptionDeckAffinityFit(
        string connectionString,
        int[] archetypeIds,
        string[] optionIds,
        Dictionary<string, string> optionTypes)
    {
        Dictionary<string, double> result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        if (archetypeIds.Length == 0)
        {
            return result;
        }

        Dictionary<string, double> map = LoadAffinityRows(connectionString, archetypeIds, optionIds);

        for (int i = 0; i < optionIds.Length; i++)
        {
            string optionId = optionIds[i];
            string optionType = optionTypes.TryGetValue(optionId, out string value) ? value : "unknown";

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

            result[optionId] = count == 0 ? 0.0 : (sum / count);
        }

        return result;
    }

    private static Dictionary<string, double> LoadAffinityRows(string connectionString, int[] archetypeIds, string[] optionIds)
    {
        Dictionary<string, double> result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        using NpgsqlConnection conn = new NpgsqlConnection(connectionString);
        conn.Open();

        const string sql = @"
SELECT archetype_id, entity_type, entity_id, affinity_score
FROM entity_archetype_affinity
WHERE archetype_id = ANY(@arch_ids)
  AND entity_id = ANY(@option_ids);";

        using NpgsqlCommand cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("arch_ids", archetypeIds);
        cmd.Parameters.AddWithValue("option_ids", optionIds);

        using NpgsqlDataReader reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            int archetypeId = reader.GetInt32(0);
            string entityType = reader.GetString(1);
            string entityId = reader.GetString(2);
            double score = (reader.IsDBNull(3) ? 0 : reader.GetInt32(3)) / 10.0;
            result[BuildAffinityKey(archetypeId, entityType, entityId)] = score;
        }

        return result;
    }

    private static string BuildAffinityKey(int archetypeId, string entityType, string entityId)
    {
        return archetypeId.ToString(CultureInfo.InvariantCulture) + "::" + entityType + "::" + entityId;
    }

    private static Dictionary<string, double> LoadFlexibilityScores(string connectionString, string[] optionIds)
    {
        Dictionary<string, double> result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        using NpgsqlConnection conn = new NpgsqlConnection(connectionString);
        conn.Open();

        const string sql = "SELECT entity_id, flexibility_rating FROM entity_strength_ratings WHERE entity_id = ANY(@ids)";
        using NpgsqlCommand cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("ids", optionIds);

        using NpgsqlDataReader reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            string entityId = reader.GetString(0);
            double rating = reader.IsDBNull(1) ? 5.0 : reader.GetInt32(1);
            if (!result.ContainsKey(entityId))
            {
                result[entityId] = rating / 10.0;
            }
        }

        return result;
    }

    private static HashSet<string> LoadHeldEffectTags(string connectionString, string[] deckIds, string[] relicIds)
    {
        HashSet<string> result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using NpgsqlConnection conn = new NpgsqlConnection(connectionString);
        conn.Open();

        if (deckIds.Length > 0)
        {
            CollectEffectTags(conn, result, "SELECT effect_tags FROM cards WHERE id = ANY(@ids)", deckIds);
        }

        if (relicIds.Length > 0)
        {
            CollectEffectTags(conn, result, "SELECT effect_tags FROM relics WHERE id = ANY(@ids)", relicIds);
        }

        return result;
    }

    private static void CollectEffectTags(NpgsqlConnection conn, HashSet<string> result, string sql, string[] ids)
    {
        using NpgsqlCommand cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("ids", ids);
        using NpgsqlDataReader reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            if (!reader.IsDBNull(0))
            {
                string[] tags = reader.GetFieldValue<string[]>(0);
                foreach (string tag in tags)
                {
                    result.Add(tag);
                }
            }
        }
    }

    private static Dictionary<string, string[]> LoadOptionAntiSynergyTags(
        string connectionString,
        string[] optionIds,
        Dictionary<string, string> optionTypes)
    {
        Dictionary<string, string[]> result = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        string[] cardOptions = optionIds
            .Where(id => optionTypes.TryGetValue(id, out string type) && string.Equals(type, "card", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (cardOptions.Length == 0)
        {
            return result;
        }

        using NpgsqlConnection conn = new NpgsqlConnection(connectionString);
        conn.Open();

        using NpgsqlCommand cmd = new NpgsqlCommand("SELECT id, anti_synergy_tags FROM cards WHERE id = ANY(@ids)", conn);
        cmd.Parameters.AddWithValue("ids", cardOptions);
        using NpgsqlDataReader reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            string id = reader.GetString(0);
            string[] tags = reader.IsDBNull(1) ? Array.Empty<string>() : reader.GetFieldValue<string[]>(1);
            result[id] = tags;
        }

        return result;
    }

    private static string GenerateNarrative(List<PickScoreRecord> scores, CliOptions options)
    {
        string model = string.IsNullOrWhiteSpace(options.ModelOverride)
            ? ModelRouter.Resolve(options.Provider, AnnotationTaskKind.Synergy)
            : options.ModelOverride;

        ILlmProvider provider = LlmProviderFactory.Create(options.Provider);

        StringBuilder sb = new StringBuilder();
        sb.AppendLine("You are an expert Slay the Spire 2 pick advisor. Explain in plain English why the top-ranked option is the best pick given the current deck state.");
        sb.AppendLine("Be concise (2-4 sentences). Focus on archetype affinity, deck fit, anti-synergy penalties, and trade-offs.");
        sb.AppendLine();
        sb.AppendLine("Scored options:");
        foreach (PickScoreRecord score in scores)
        {
            sb.AppendLine(
                $"Rank {score.Rank}: {score.EntityId} (score={score.CompositeScore * 10:F2}, " +
                $"deck={score.EdgeOverlapScore * 10:F1}, arch={score.ArchetypeFitScore * 10:F0}, " +
                $"anti={score.AntiSynergyPenalty * 10:F0}, flex={score.FlexibilityScore * 10:F0})");
            if (!string.IsNullOrWhiteSpace(score.SynergyDrivers))
            {
                sb.AppendLine($"  Synergy drivers: {score.SynergyDrivers}");
            }
        }

        AnnotationRequest request = new AnnotationRequest
        {
            Model = model,
            SystemPrompt = "You are an expert Slay the Spire 2 pick advisor. Be concise and specific.",
            Prompt = sb.ToString(),
            MaxTokens = options.MaxTokens
        };

        AnnotationResult annotationResult = provider.AnnotateAsync(request, CancellationToken.None).GetAwaiter().GetResult();
        return annotationResult.Content;
    }
}
