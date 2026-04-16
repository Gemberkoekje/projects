using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Sts2Extractor.Cli;
using Sts2Extractor.Infrastructure;
using Npgsql;

namespace Sts2Extractor.Extractors;

internal sealed class EntityStrengthRatingsRunner
{
    private readonly CardExtractor _cardExtractor;
    private readonly RelicExtractor _relicExtractor;
    private readonly PotionExtractor _potionExtractor;
    private readonly EventExtractor _eventExtractor;

    public EntityStrengthRatingsRunner()
    {
        _cardExtractor = new CardExtractor();
        _relicExtractor = new RelicExtractor();
        _potionExtractor = new PotionExtractor();
        _eventExtractor = new EventExtractor();
    }

    public EntityStrengthRatingsResult Run(CliOptions options)
    {
        string connectionString = DatabaseSettingsResolver.ResolveConnectionString(options);
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            List<EntityStrengthRatingRecord> databaseRatings = BuildRatingsFromDatabase(connectionString);
            if (databaseRatings.Count > 0)
            {
                WriteRatings(options.OutputPath, databaseRatings);
                return BuildResult(options.OutputPath, databaseRatings);
            }
        }

        List<EntityStrengthRatingRecord> extractedRatings = BuildRatingsFromExtractedSources(options);
        WriteRatings(options.OutputPath, extractedRatings);
        return BuildResult(options.OutputPath, extractedRatings);
    }

    private List<EntityStrengthRatingRecord> BuildRatingsFromDatabase(string connectionString)
    {
        List<EntityStrengthRatingRecord> ratings = new List<EntityStrengthRatingRecord>();

        try
        {
            using NpgsqlConnection connection = new NpgsqlConnection(connectionString);
            connection.Open();

            List<DatabaseEntityRatingSource> entities = LoadDatabaseEntities(connection);
            List<DatabaseEventOptionRatingSource> eventOptions = LoadDatabaseEventOptions(connection);
            Dictionary<string, EntityAffinitySummary> affinitySummaries = LoadAffinitySummaries(connection);

            for (int i = 0; i < entities.Count; i++)
            {
                DatabaseEntityRatingSource entity = entities[i];
                string key = BuildEntityKey(entity.EntityType, entity.EntityId);
                EntityAffinitySummary summary = affinitySummaries.TryGetValue(key, out EntityAffinitySummary value)
                    ? value
                    : new EntityAffinitySummary();
                ratings.Add(RateDatabaseEntity(entity, summary));
            }

            for (int i = 0; i < eventOptions.Count; i++)
            {
                ratings.Add(RateDatabaseEventOption(eventOptions[i]));
            }
        }
        catch (PostgresException ex) when (ex.SqlState == "42P01")
        {
            return new List<EntityStrengthRatingRecord>();
        }

        return ratings;
    }

    private static List<DatabaseEntityRatingSource> LoadDatabaseEntities(NpgsqlConnection connection)
    {
        List<DatabaseEntityRatingSource> rows = new List<DatabaseEntityRatingSource>();
        LoadCardEntities(connection, rows);
        LoadRelicEntities(connection, rows);
        LoadPotionEntities(connection, rows);
        return rows;
    }

    private static void LoadCardEntities(NpgsqlConnection connection, List<DatabaseEntityRatingSource> rows)
    {
        using NpgsqlCommand command = new NpgsqlCommand(@"
SELECT
    id,
    COALESCE(effect_tags, ARRAY[]::text[]),
    COALESCE(resources_generated, ARRAY[]::text[]),
    COALESCE(resources_consumed, ARRAY[]::text[]),
    COALESCE(anti_synergy_tags, ARRAY[]::text[]),
    COALESCE(energy_cost, 0),
    COALESCE(gains_block, FALSE),
    COALESCE(damage_base, 0),
    COALESCE(damage_upgraded, 0),
    COALESCE(block_base, 0),
    COALESCE(block_upgraded, 0),
    COALESCE(target, ''),
    COALESCE(type, ''),
    COALESCE(on_play_source, '') || E'\n' || COALESCE(on_upgrade_source, '')
FROM cards
ORDER BY id;", connection);

        using NpgsqlDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(new DatabaseEntityRatingSource
            {
                EntityType = "card",
                EntityId = reader.GetString(0),
                EffectTags = ReadStringArray(reader, 1),
                ResourcesGenerated = ReadStringArray(reader, 2),
                ResourcesConsumed = ReadStringArray(reader, 3),
                AntiSynergyTags = ReadStringArray(reader, 4),
                EnergyCost = reader.GetInt32(5),
                GainsBlock = reader.GetBoolean(6),
                DamageBase = reader.GetInt32(7),
                DamageUpgraded = reader.GetInt32(8),
                BlockBase = reader.GetInt32(9),
                BlockUpgraded = reader.GetInt32(10),
                TargetType = reader.GetString(11),
                Subtype = reader.GetString(12),
                SourceText = reader.GetString(13)
            });
        }
    }

    private static void LoadRelicEntities(NpgsqlConnection connection, List<DatabaseEntityRatingSource> rows)
    {
        using NpgsqlCommand command = new NpgsqlCommand(@"
SELECT
    id,
    COALESCE(effect_tags, ARRAY[]::text[]),
    COALESCE(resources_generated, ARRAY[]::text[]),
    COALESCE(resources_consumed, ARRAY[]::text[]),
    ARRAY[]::text[],
    COALESCE(rarity, ''),
    COALESCE(counter_base, -2147483648),
    COALESCE(amount_base, -2147483648),
    COALESCE(triggers_on, ARRAY[]::text[]),
    COALESCE(hook_source, '')
FROM relics
ORDER BY id;", connection);

        using NpgsqlDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(new DatabaseEntityRatingSource
            {
                EntityType = "relic",
                EntityId = reader.GetString(0),
                EffectTags = ReadStringArray(reader, 1),
                ResourcesGenerated = ReadStringArray(reader, 2),
                ResourcesConsumed = ReadStringArray(reader, 3),
                AntiSynergyTags = ReadStringArray(reader, 4),
                Rarity = reader.GetString(5),
                CounterBase = reader.GetInt32(6),
                AmountBase = reader.GetInt32(7),
                TriggersOn = ReadStringArray(reader, 8),
                SourceText = reader.GetString(9)
            });
        }
    }

    private static void LoadPotionEntities(NpgsqlConnection connection, List<DatabaseEntityRatingSource> rows)
    {
        using NpgsqlCommand command = new NpgsqlCommand(@"
SELECT
    id,
    COALESCE(effect_tags, ARRAY[]::text[]),
    ARRAY[]::text[],
    ARRAY[]::text[],
    ARRAY[]::text[],
    COALESCE(rarity, ''),
    COALESCE(amount_base, 0),
    COALESCE(on_use_source, '')
FROM potions
ORDER BY id;", connection);

        using NpgsqlDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(new DatabaseEntityRatingSource
            {
                EntityType = "potion",
                EntityId = reader.GetString(0),
                EffectTags = ReadStringArray(reader, 1),
                ResourcesGenerated = ReadStringArray(reader, 2),
                ResourcesConsumed = ReadStringArray(reader, 3),
                AntiSynergyTags = ReadStringArray(reader, 4),
                Rarity = reader.GetString(5),
                AmountBase = reader.GetInt32(6),
                SourceText = reader.GetString(7)
            });
        }
    }

    private static List<DatabaseEventOptionRatingSource> LoadDatabaseEventOptions(NpgsqlConnection connection)
    {
        List<DatabaseEventOptionRatingSource> rows = new List<DatabaseEventOptionRatingSource>();
        using NpgsqlCommand command = new NpgsqlCommand(@"
SELECT event_id, COALESCE(option_key, ''), COALESCE(handler_method, ''), COALESCE(handler_source, '')
FROM event_options
ORDER BY event_id, option_key;", connection);

        using NpgsqlDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(new DatabaseEventOptionRatingSource
            {
                EventId = reader.GetString(0),
                OptionKey = reader.GetString(1),
                HandlerMethod = reader.GetString(2),
                HandlerSource = reader.GetString(3)
            });
        }

        return rows;
    }

    private static Dictionary<string, EntityAffinitySummary> LoadAffinitySummaries(NpgsqlConnection connection)
    {
        Dictionary<string, EntityAffinitySummary> summaries = new Dictionary<string, EntityAffinitySummary>(StringComparer.OrdinalIgnoreCase);
        using NpgsqlCommand command = new NpgsqlCommand(@"
SELECT entity_type, entity_id, affinity_score
FROM entity_archetype_affinity;", connection);

        using NpgsqlDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            string entityType = reader.GetString(0);
            string entityId = reader.GetString(1);
            int affinityScore = reader.IsDBNull(2) ? 0 : reader.GetInt32(2);
            string key = BuildEntityKey(entityType, entityId);
            if (!summaries.TryGetValue(key, out EntityAffinitySummary summary))
            {
                summary = new EntityAffinitySummary();
                summaries[key] = summary;
            }

            summary.Add(affinityScore);
        }

        return summaries;
    }

    private static string BuildEntityKey(string entityType, string entityId)
    {
        return entityType + "::" + entityId;
    }

    private static EntityStrengthRatingRecord RateDatabaseEntity(DatabaseEntityRatingSource entity, EntityAffinitySummary summary)
    {
        if (summary.Count == 0)
        {
            return CreateRating(entity.EntityType, entity.EntityId, 0, 0, 0, new[]
            {
                "no archetype affinity grades available"
            }, entity.EntityType);
        }

        int synergy = summary.MaxAffinity;
        int flexibility = (int)Math.Round(summary.TotalAffinity / (double)summary.Count, MidpointRounding.AwayFromZero);
        int antiSynergy = summary.MinAffinity;

        return CreateRating(entity.EntityType, entity.EntityId, synergy, flexibility, antiSynergy, new[]
        {
            "derived from "
            + summary.Count.ToString(CultureInfo.InvariantCulture)
            + " archetype affinity grades (max="
            + summary.MaxAffinity.ToString(CultureInfo.InvariantCulture)
            + ", mean="
            + flexibility.ToString(CultureInfo.InvariantCulture)
            + ", min="
            + summary.MinAffinity.ToString(CultureInfo.InvariantCulture)
            + ")"
        }, entity.EntityType);
    }

    private static EntityStrengthRatingRecord RateDatabaseEventOption(DatabaseEventOptionRatingSource option)
    {
        int synergy = 4;
        int flexibility = 4;
        int antiSynergy = 2;
        List<string> rationale = new List<string>();

        int positiveSignals = CountContains(option.HandlerSource,
            "gain",
            "reward",
            "gold",
            "relic",
            "card",
            "heal",
            "upgrade",
            "remove");

        if (positiveSignals > 0)
        {
            synergy += Math.Min(4, positiveSignals);
            rationale.Add("contains positive reward flow");
        }

        int penaltySignals = CountContains(option.HandlerSource,
            "losehp",
            "lose hp",
            "take damage",
            "damage player",
            "curse",
            "remove max hp",
            "sacrifice");

        antiSynergy += Math.Min(5, penaltySignals);

        if (penaltySignals > 0)
        {
            rationale.Add("contains explicit penalties");
        }

        if (CountContains(option.HandlerSource, "if", "switch", "choice", "random") > 0)
        {
            flexibility += 1;
        }

        string optionKey = option.OptionKey;
        if (string.IsNullOrWhiteSpace(optionKey))
        {
            optionKey = option.HandlerMethod;
        }

        if (string.IsNullOrWhiteSpace(optionKey))
        {
            optionKey = "option";
        }

        return CreateRating("event", option.EventId + "::" + optionKey, synergy, flexibility, antiSynergy, rationale, "event_option");
    }

    private List<EntityStrengthRatingRecord> BuildRatingsFromExtractedSources(CliOptions options)
    {
        string cardsDirectory = Path.Combine(options.RootPath, "MegaCrit.Sts2.Core.Models.Cards");
        string relicsDirectory = Path.Combine(options.RootPath, "MegaCrit.Sts2.Core.Models.Relics");
        string potionsDirectory = Path.Combine(options.RootPath, "MegaCrit.Sts2.Core.Models.Potions");
        string eventsDirectory = Path.Combine(options.RootPath, "MegaCrit.Sts2.Core.Models.Events");

        ValidateDirectory(cardsDirectory);
        ValidateDirectory(relicsDirectory);
        ValidateDirectory(potionsDirectory);
        ValidateDirectory(eventsDirectory);

        List<CardExtractionRecord> cards = Directory
            .EnumerateFiles(cardsDirectory, "*.cs", SearchOption.TopDirectoryOnly)
            .OrderBy(static file => file, StringComparer.OrdinalIgnoreCase)
            .Select(file => _cardExtractor.Extract(options.RootPath, file))
            .ToList();

        List<RelicExtractionRecord> relics = Directory
            .EnumerateFiles(relicsDirectory, "*.cs", SearchOption.TopDirectoryOnly)
            .OrderBy(static file => file, StringComparer.OrdinalIgnoreCase)
            .Select(file => _relicExtractor.Extract(options.RootPath, file))
            .ToList();

        List<PotionExtractionRecord> potions = Directory
            .EnumerateFiles(potionsDirectory, "*.cs", SearchOption.TopDirectoryOnly)
            .OrderBy(static file => file, StringComparer.OrdinalIgnoreCase)
            .Select(file => _potionExtractor.Extract(options.RootPath, file))
            .ToList();

        List<EventExtractionRecord> events = Directory
            .EnumerateFiles(eventsDirectory, "*.cs", SearchOption.TopDirectoryOnly)
            .OrderBy(static file => file, StringComparer.OrdinalIgnoreCase)
            .Select(file => _eventExtractor.Extract(options.RootPath, file))
            .ToList();

        List<EntityStrengthRatingRecord> ratings = new List<EntityStrengthRatingRecord>();

        foreach (CardExtractionRecord card in cards)
        {
            if (string.IsNullOrWhiteSpace(card.CardId))
            {
                continue;
            }

            ratings.Add(RateCard(card));
        }

        foreach (RelicExtractionRecord relic in relics)
        {
            if (string.IsNullOrWhiteSpace(relic.RelicId))
            {
                continue;
            }

            ratings.Add(RateRelic(relic));
        }

        foreach (PotionExtractionRecord potion in potions)
        {
            if (string.IsNullOrWhiteSpace(potion.PotionId))
            {
                continue;
            }

            ratings.Add(RatePotion(potion));
        }

        foreach (EventExtractionRecord eventRecord in events)
        {
            foreach (EventOptionRecord option in eventRecord.Options)
            {
                if (string.IsNullOrWhiteSpace(option.EventId))
                {
                    continue;
                }

                ratings.Add(RateEventOption(option));
            }
        }

        return ratings;
    }

    private static void WriteRatings(string outputPath, List<EntityStrengthRatingRecord> ratings)
    {
        CsvWriter.Write(outputPath, new[]
        {
            "entity_type",
            "entity_id",
            "card_variant_id",
            "synergy_rating",
            "flexibility_rating",
            "anti_synergy_rating",
            "rationale"
        }, ratings.Select(static rating => (IReadOnlyList<string>)new[]
        {
            rating.EntityType,
            rating.EntityId,
            rating.CardVariantId,
            rating.SynergyRating.ToString(CultureInfo.InvariantCulture),
            rating.FlexibilityRating.ToString(CultureInfo.InvariantCulture),
            rating.AntiSynergyRating.ToString(CultureInfo.InvariantCulture),
            rating.Rationale
        }));
    }

    private static EntityStrengthRatingsResult BuildResult(string outputPath, List<EntityStrengthRatingRecord> ratings)
    {
        return new EntityStrengthRatingsResult
        {
            OutputPath = outputPath,
            RecordCount = ratings.Count,
            CardCount = ratings.Count(static x => string.Equals(x.EntityType, "card", StringComparison.Ordinal)),
            RelicCount = ratings.Count(static x => string.Equals(x.EntityType, "relic", StringComparison.Ordinal)),
            PotionCount = ratings.Count(static x => string.Equals(x.EntityType, "potion", StringComparison.Ordinal)),
            EventOptionCount = ratings.Count(static x => string.Equals(x.EntityType, "event", StringComparison.Ordinal))
        };
    }

    private static EntityStrengthRatingRecord RateCard(CardExtractionRecord card)
    {
        int synergy = 4;
        int flexibility = 5;
        int antiSynergy = 2;
        List<string> rationale = new List<string>();

        if (card.DamageBase > 0 || card.DamageUpgraded > 0)
        {
            synergy += 1;
            rationale.Add("has direct damage");
        }

        if (card.GainsBlock || card.BlockBase > 0 || card.BlockUpgraded > 0)
        {
            synergy += 1;
            flexibility += 1;
            rationale.Add("has defensive value");
        }

        int tagBonus = Math.Min(2, card.EffectTags.Count / 2);
        if (tagBonus > 0)
        {
            synergy += tagBonus;
            rationale.Add("multiple effect tags");
        }

        if (card.CompanionActions.Count > 0)
        {
            synergy += 1;
            rationale.Add("companion interaction");
        }

        if (card.EnergyCost == 0)
        {
            synergy += 1;
            flexibility += 2;
            rationale.Add("zero-cost");
        }
        else if (card.EnergyCost == 1)
        {
            flexibility += 2;
        }
        else if (card.EnergyCost == 2)
        {
            flexibility += 1;
        }
        else if (card.EnergyCost >= 3)
        {
            flexibility -= 1;
            antiSynergy += 1;
            rationale.Add("high energy cost");
        }

        if (ContainsAnyTag(card.EffectTags, "self-damage", "self_damage", "exhaust", "ethereal", "lose_hp", "hp_loss"))
        {
            antiSynergy += 2;
            flexibility -= 1;
            rationale.Add("has common drawback tags");
        }

        int textPenalty = CountContains(card.OnPlaySource + "\n" + card.OnUpgradeSource,
            "losehp",
            "lose hp",
            "selfdamage",
            "self damage",
            "discard",
            "exhaust",
            "damage self");

        antiSynergy += Math.Min(3, textPenalty);

        if (string.Equals(card.CardType, "Curse", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(card.CardType, "Status", StringComparison.OrdinalIgnoreCase))
        {
            flexibility -= 3;
            antiSynergy += 3;
            rationale.Add("curse/status type");
        }

        if (string.Equals(card.TargetType, "AnyEnemy", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(card.TargetType, "AllEnemies", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(card.TargetType, "Self", StringComparison.OrdinalIgnoreCase))
        {
            flexibility += 1;
        }

        return new EntityStrengthRatingRecord
        {
            EntityType = "card",
            EntityId = card.CardId,
            SynergyRating = ClampRating(synergy),
            FlexibilityRating = ClampRating(flexibility),
            AntiSynergyRating = ClampRating(antiSynergy),
            Rationale = JoinRationale(rationale, "card")
        };
    }

    private static EntityStrengthRatingRecord RateRelic(RelicExtractionRecord relic)
    {
        int synergy = 5;
        int flexibility = 5;
        int antiSynergy = 2;
        List<string> rationale = new List<string>();

        if (relic.HookNames.Count > 0)
        {
            synergy += Math.Min(2, relic.HookNames.Count);
            rationale.Add("multiple hooks");
        }

        if (relic.AmountBase != int.MinValue || relic.CounterBase != int.MinValue)
        {
            synergy += 1;
            rationale.Add("has quantified scaling");
        }

        if (string.Equals(relic.Rarity, "Common", StringComparison.OrdinalIgnoreCase))
        {
            flexibility += 2;
        }
        else if (string.Equals(relic.Rarity, "Uncommon", StringComparison.OrdinalIgnoreCase) || string.Equals(relic.Rarity, "Starter", StringComparison.OrdinalIgnoreCase))
        {
            flexibility += 1;
        }
        else if (string.Equals(relic.Rarity, "Shop", StringComparison.OrdinalIgnoreCase) || string.Equals(relic.Rarity, "Event", StringComparison.OrdinalIgnoreCase))
        {
            flexibility -= 1;
        }

        int positiveSignals = CountContains(relic.HookSource, "gain", "draw", "energy", "block", "strength", "vulnerable", "weak");
        if (positiveSignals > 0)
        {
            synergy += Math.Min(2, positiveSignals);
        }

        int penaltySignals = CountContains(relic.HookSource, "losehp", "lose hp", "selfdamage", "self damage", "discard", "exhaust", "curse");
        antiSynergy += Math.Min(4, penaltySignals);

        if (penaltySignals > 0)
        {
            rationale.Add("contains drawback behavior");
        }

        return new EntityStrengthRatingRecord
        {
            EntityType = "relic",
            EntityId = relic.RelicId,
            SynergyRating = ClampRating(synergy),
            FlexibilityRating = ClampRating(flexibility),
            AntiSynergyRating = ClampRating(antiSynergy),
            Rationale = JoinRationale(rationale, "relic")
        };
    }

    private static EntityStrengthRatingRecord RatePotion(PotionExtractionRecord potion)
    {
        int synergy = 5;
        int flexibility = 5;
        int antiSynergy = 2;
        List<string> rationale = new List<string>();

        if (potion.AmountBase > 0)
        {
            synergy += 1;
            rationale.Add("has explicit amount");
        }

        if (string.Equals(potion.Rarity, "Common", StringComparison.OrdinalIgnoreCase))
        {
            flexibility += 2;
        }
        else if (string.Equals(potion.Rarity, "Uncommon", StringComparison.OrdinalIgnoreCase))
        {
            flexibility += 1;
        }

        if (string.Equals(potion.TargetType, "AnyEnemy", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(potion.TargetType, "Self", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(potion.TargetType, "AllEnemies", StringComparison.OrdinalIgnoreCase))
        {
            flexibility += 1;
        }

        int positiveSignals = CountContains(potion.OnUseSource, "draw", "energy", "block", "strength", "dexterity", "vulnerable", "weak", "damage");
        if (positiveSignals > 0)
        {
            synergy += Math.Min(2, positiveSignals);
        }

        int penaltySignals = CountContains(potion.OnUseSource, "losehp", "lose hp", "selfdamage", "self damage", "discard", "exhaust", "curse");
        antiSynergy += Math.Min(3, penaltySignals);

        if (penaltySignals > 0)
        {
            rationale.Add("contains drawback behavior");
        }

        return new EntityStrengthRatingRecord
        {
            EntityType = "potion",
            EntityId = potion.PotionId,
            SynergyRating = ClampRating(synergy),
            FlexibilityRating = ClampRating(flexibility),
            AntiSynergyRating = ClampRating(antiSynergy),
            Rationale = JoinRationale(rationale, "potion")
        };
    }

    private static EntityStrengthRatingRecord RateEventOption(EventOptionRecord option)
    {
        int synergy = 4;
        int flexibility = 4;
        int antiSynergy = 2;
        List<string> rationale = new List<string>();

        int positiveSignals = CountContains(option.HandlerSource,
            "gain",
            "reward",
            "gold",
            "relic",
            "card",
            "heal",
            "upgrade",
            "remove");

        if (positiveSignals > 0)
        {
            synergy += Math.Min(4, positiveSignals);
            rationale.Add("contains positive reward flow");
        }

        int penaltySignals = CountContains(option.HandlerSource,
            "losehp",
            "lose hp",
            "take damage",
            "damage player",
            "curse",
            "remove max hp",
            "sacrifice");

        antiSynergy += Math.Min(5, penaltySignals);

        if (penaltySignals > 0)
        {
            rationale.Add("contains explicit penalties");
        }

        if (CountContains(option.HandlerSource, "if", "switch", "choice", "random") > 0)
        {
            flexibility += 1;
        }

        string optionKey = option.OptionTextKey;
        if (string.IsNullOrWhiteSpace(optionKey))
        {
            optionKey = option.HandlerMethod;
        }

        if (string.IsNullOrWhiteSpace(optionKey))
        {
            optionKey = "option";
        }

        return new EntityStrengthRatingRecord
        {
            EntityType = "event",
            EntityId = option.EventId + "::" + optionKey,
            SynergyRating = ClampRating(synergy),
            FlexibilityRating = ClampRating(flexibility),
            AntiSynergyRating = ClampRating(antiSynergy),
            Rationale = JoinRationale(rationale, "event_option")
        };
    }

    private static EntityStrengthRatingRecord CreateRating(string entityType, string entityId, int synergy, int flexibility, int antiSynergy, IReadOnlyList<string> rationale, string fallback)
    {
        return new EntityStrengthRatingRecord
        {
            EntityType = entityType,
            EntityId = entityId,
            SynergyRating = ClampRating(synergy),
            FlexibilityRating = ClampRating(flexibility),
            AntiSynergyRating = ClampRating(antiSynergy),
            Rationale = JoinRationale(rationale, fallback)
        };
    }

    private static int ClampRating(int value)
    {
        if (value < 0)
        {
            return 0;
        }

        if (value > 10)
        {
            return 10;
        }

        return value;
    }

    private static bool ContainsAnyTag(IReadOnlyList<string> tags, params string[] fragments)
    {
        foreach (string tag in tags)
        {
            string lower = tag.ToLowerInvariant();
            foreach (string fragment in fragments)
            {
                if (lower.Contains(fragment, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static int CountContains(string source, params string[] fragments)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return 0;
        }

        string lower = source.ToLowerInvariant();
        int count = 0;
        foreach (string fragment in fragments)
        {
            if (lower.Contains(fragment, StringComparison.Ordinal))
            {
                count++;
            }
        }

        return count;
    }

    private static string JoinRationale(IReadOnlyList<string> reasons, string fallback)
    {
        if (reasons.Count == 0)
        {
            return "heuristic score from extracted mechanics (" + fallback + ")";
        }

        return string.Join("; ", reasons);
    }

    private static void ValidateDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            throw new DirectoryNotFoundException("Missing expected directory: " + path);
        }
    }

    private static string[] ReadStringArray(NpgsqlDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
        {
            return Array.Empty<string>();
        }

        string[] value = reader.GetFieldValue<string[]>(ordinal);
        return value ?? Array.Empty<string>();
    }

    private sealed class DatabaseEntityRatingSource
    {
        public string EntityType { get; set; }

        public string EntityId { get; set; }

        public string[] EffectTags { get; set; }

        public string[] ResourcesGenerated { get; set; }

        public string[] ResourcesConsumed { get; set; }

        public string[] AntiSynergyTags { get; set; }

        public string[] TriggersOn { get; set; }

        public int EnergyCost { get; set; }

        public bool GainsBlock { get; set; }

        public int DamageBase { get; set; }

        public int DamageUpgraded { get; set; }

        public int BlockBase { get; set; }

        public int BlockUpgraded { get; set; }

        public string TargetType { get; set; }

        public string Subtype { get; set; }

        public string Rarity { get; set; }

        public int CounterBase { get; set; }

        public int AmountBase { get; set; }

        public string SourceText { get; set; }

        public DatabaseEntityRatingSource()
        {
            EntityType = string.Empty;
            EntityId = string.Empty;
            EffectTags = Array.Empty<string>();
            ResourcesGenerated = Array.Empty<string>();
            ResourcesConsumed = Array.Empty<string>();
            AntiSynergyTags = Array.Empty<string>();
            TriggersOn = Array.Empty<string>();
            TargetType = string.Empty;
            Subtype = string.Empty;
            Rarity = string.Empty;
            CounterBase = int.MinValue;
            AmountBase = 0;
            SourceText = string.Empty;
        }
    }

    private sealed class DatabaseEventOptionRatingSource
    {
        public string EventId { get; set; }

        public string OptionKey { get; set; }

        public string HandlerMethod { get; set; }

        public string HandlerSource { get; set; }

        public DatabaseEventOptionRatingSource()
        {
            EventId = string.Empty;
            OptionKey = string.Empty;
            HandlerMethod = string.Empty;
            HandlerSource = string.Empty;
        }
    }

    private sealed class EntityAffinitySummary
    {
        public int Count { get; private set; }

        public int TotalAffinity { get; private set; }

        public int MinAffinity { get; private set; }

        public int MaxAffinity { get; private set; }

        public EntityAffinitySummary()
        {
            MinAffinity = 10;
            MaxAffinity = 0;
        }

        public void Add(int affinityScore)
        {
            int clamped = Math.Max(0, Math.Min(10, affinityScore));
            Count++;
            TotalAffinity += clamped;
            if (clamped < MinAffinity)
            {
                MinAffinity = clamped;
            }

            if (clamped > MaxAffinity)
            {
                MaxAffinity = clamped;
            }
        }
    }

    private sealed class EntityEdgeSummary
    {
        public int PositiveEdgeCount { get; set; }

        public int PositiveStrengthTotal { get; set; }

        public int MaxPositiveStrength { get; set; }

        public int NegativeEdgeCount { get; set; }

        public int NegativeStrengthTotal { get; set; }

        public int MaxNegativeStrength { get; set; }

        public HashSet<string> SharedTags { get; }

        public EntityEdgeSummary()
        {
            SharedTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }
}
