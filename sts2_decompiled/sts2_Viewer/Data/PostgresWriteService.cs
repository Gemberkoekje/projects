using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Sts2Viewer.Data;

public sealed class PostgresWriteService
{
    private readonly string _connectionString;

    public PostgresWriteService(IConfiguration configuration)
    {
        string configured = configuration["Database:ConnectionString"] ?? string.Empty;
        string env = Environment.GetEnvironmentVariable("STS2_POSTGRES_CONNECTION_STRING") ?? string.Empty;
        _connectionString = string.IsNullOrWhiteSpace(configured) ? env : configured;
    }

    public bool HasConnectionString()
    {
        return !string.IsNullOrWhiteSpace(_connectionString);
    }

    public int SaveRunState(string name, string characterId, int archetypeId, string[] cardIds, string[] relicIds)
    {
        if (!HasConnectionString())
        {
            return 0;
        }

        using NpgsqlConnection connection = OpenConnection();
        using NpgsqlCommand command = new NpgsqlCommand(@"
WITH updated AS (
    UPDATE run_states
       SET character_id = NULLIF(@character_id, ''),
           archetype_id = CASE WHEN @archetype_id <= 0 THEN NULL ELSE @archetype_id END,
           card_ids = @card_ids,
           relic_ids = @relic_ids,
           created_at = now()
     WHERE LOWER(name) = LOWER(@name)
 RETURNING id
)
INSERT INTO run_states (name, character_id, archetype_id, card_ids, relic_ids)
SELECT @name, NULLIF(@character_id, ''), CASE WHEN @archetype_id <= 0 THEN NULL ELSE @archetype_id END, @card_ids, @relic_ids
WHERE NOT EXISTS (SELECT 1 FROM updated)
RETURNING id;", connection);

        command.Parameters.AddWithValue("name", name);
        command.Parameters.AddWithValue("character_id", characterId);
        command.Parameters.AddWithValue("archetype_id", archetypeId);
        command.Parameters.AddWithValue("card_ids", cardIds);
        command.Parameters.AddWithValue("relic_ids", relicIds);

        object result = command.ExecuteScalar();
        if (result is int id)
        {
            return id;
        }

        using NpgsqlCommand fallback = new NpgsqlCommand("SELECT id FROM run_states WHERE LOWER(name) = LOWER(@name) ORDER BY created_at DESC LIMIT 1", connection);
        fallback.Parameters.AddWithValue("name", name);
        object fallbackResult = fallback.ExecuteScalar();
        return fallbackResult is int fallbackId ? fallbackId : 0;
    }

    public IReadOnlyList<RunStateRow> GetRunStates()
    {
        if (!HasConnectionString())
        {
            return new List<RunStateRow>();
        }

        List<RunStateRow> rows = new List<RunStateRow>();

        using NpgsqlConnection connection = OpenConnection();
        using NpgsqlCommand command = new NpgsqlCommand(@"
SELECT
    rs.id,
    rs.name,
    COALESCE(rs.character_id, ''),
    COALESCE(rs.archetype_id, 0),
    COALESCE(a.name, ''),
    COALESCE(rs.card_ids, ARRAY[]::text[]),
    COALESCE(rs.relic_ids, ARRAY[]::text[]),
    rs.created_at
FROM run_states rs
LEFT JOIN archetypes a ON a.id = rs.archetype_id
ORDER BY rs.created_at DESC, rs.id DESC;", connection);

        using NpgsqlDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(new RunStateRow
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1),
                CharacterId = reader.GetString(2),
                ArchetypeId = reader.GetInt32(3),
                ArchetypeName = reader.GetString(4),
                CardIds = reader.GetFieldValue<string[]>(5),
                RelicIds = reader.GetFieldValue<string[]>(6),
                CreatedAt = reader.GetFieldValue<DateTimeOffset>(7)
            });
        }

        return rows;
    }

    public void DeleteRunState(int id)
    {
        if (!HasConnectionString() || id <= 0)
        {
            return;
        }

        using NpgsqlConnection connection = OpenConnection();
        using NpgsqlCommand command = new NpgsqlCommand("DELETE FROM run_states WHERE id = @id", connection);
        command.Parameters.AddWithValue("id", id);
        command.ExecuteNonQuery();
    }

    public void SavePickAdvice(int runStateId, IReadOnlyList<PickScoreRow> scores)
    {
        if (!HasConnectionString() || scores.Count == 0)
        {
            return;
        }

        using NpgsqlConnection connection = OpenConnection();
        using NpgsqlTransaction transaction = connection.BeginTransaction();

        using NpgsqlCommand command = new NpgsqlCommand(@"
INSERT INTO pick_advice (
    run_state_id,
    option_entity_type,
    option_entity_id,
    rank,
    edge_overlap_score,
    archetype_fit_score,
    anti_synergy_penalty,
    composite_score,
    explanation)
VALUES (
    CASE WHEN @run_state_id <= 0 THEN NULL ELSE @run_state_id END,
    @option_entity_type,
    @option_entity_id,
    @rank,
    @edge_overlap_score,
    @archetype_fit_score,
    @anti_synergy_penalty,
    @composite_score,
    @explanation);", connection, transaction);

        command.Parameters.Add("run_state_id", NpgsqlTypes.NpgsqlDbType.Integer);
        command.Parameters.Add("option_entity_type", NpgsqlTypes.NpgsqlDbType.Text);
        command.Parameters.Add("option_entity_id", NpgsqlTypes.NpgsqlDbType.Text);
        command.Parameters.Add("rank", NpgsqlTypes.NpgsqlDbType.Integer);
        command.Parameters.Add("edge_overlap_score", NpgsqlTypes.NpgsqlDbType.Double);
        command.Parameters.Add("archetype_fit_score", NpgsqlTypes.NpgsqlDbType.Double);
        command.Parameters.Add("anti_synergy_penalty", NpgsqlTypes.NpgsqlDbType.Double);
        command.Parameters.Add("composite_score", NpgsqlTypes.NpgsqlDbType.Double);
        command.Parameters.Add("explanation", NpgsqlTypes.NpgsqlDbType.Text);

        for (int i = 0; i < scores.Count; i++)
        {
            PickScoreRow score = scores[i];
            command.Parameters["run_state_id"].Value = runStateId;
            command.Parameters["option_entity_type"].Value = score.EntityType;
            command.Parameters["option_entity_id"].Value = score.EntityId;
            command.Parameters["rank"].Value = score.Rank;
            command.Parameters["edge_overlap_score"].Value = score.EdgeOverlapScore;
            command.Parameters["archetype_fit_score"].Value = score.ArchetypeFitScore;
            command.Parameters["anti_synergy_penalty"].Value = score.AntiSynergyPenalty;
            command.Parameters["composite_score"].Value = score.CompositeScore;
            command.Parameters["explanation"].Value = score.Explanation;
            command.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    public IReadOnlyList<PickAdviceRow> GetRecentPickAdvice(string[] optionIds, int limit)
    {
        if (!HasConnectionString() || optionIds.Length == 0 || limit <= 0)
        {
            return new List<PickAdviceRow>();
        }

        List<PickAdviceRow> rows = new List<PickAdviceRow>();

        using NpgsqlConnection connection = OpenConnection();
        using NpgsqlCommand command = new NpgsqlCommand(@"
SELECT
    id,
    COALESCE(run_state_id, 0),
    option_entity_type,
    option_entity_id,
    rank,
    COALESCE(edge_overlap_score, 0),
    COALESCE(archetype_fit_score, 0),
    COALESCE(anti_synergy_penalty, 0),
    COALESCE(composite_score, 0),
    COALESCE(explanation, ''),
    created_at
FROM pick_advice
WHERE option_entity_id = ANY(@option_ids)
ORDER BY created_at DESC, id DESC
LIMIT @limit;", connection);

        command.Parameters.AddWithValue("option_ids", optionIds);
        command.Parameters.AddWithValue("limit", limit);

        using NpgsqlDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(new PickAdviceRow
            {
                Id = reader.GetInt32(0),
                RunStateId = reader.GetInt32(1),
                OptionEntityType = reader.GetString(2),
                OptionEntityId = reader.GetString(3),
                Rank = reader.GetInt32(4),
                EdgeOverlapScore = reader.GetDouble(5),
                ArchetypeFitScore = reader.GetDouble(6),
                AntiSynergyPenalty = reader.GetDouble(7),
                CompositeScore = reader.GetDouble(8),
                Explanation = reader.GetString(9),
                CreatedAt = reader.GetFieldValue<DateTimeOffset>(10)
            });
        }

        return rows;
    }

    private NpgsqlConnection OpenConnection()
    {
        NpgsqlConnection connection = new NpgsqlConnection(_connectionString);
        connection.Open();
        return connection;
    }
}
