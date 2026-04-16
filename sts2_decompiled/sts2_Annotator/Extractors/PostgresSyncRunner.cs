using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Sts2Extractor.Annotation;
using Sts2Extractor.Cli;
using Sts2Extractor.Infrastructure;
using Sts2Extractor.Taxonomy;
using Npgsql;
using NpgsqlTypes;

namespace Sts2Extractor.Extractors;

internal sealed class PostgresSyncRunner
{
    private static readonly string[] FullSyncCardCharacterIds =
    {
        "ironclad",
        "silent",
        "defect",
        "regent",
        "necrobinder",
        "colorless"
    };

    public PostgresSyncResult Run(CliOptions options)
    {
        string connectionString = DatabaseSettingsResolver.ResolveConnectionString(options);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Missing PostgreSQL connection string. Use --connection-string, Database:ConnectionString, or STS2_POSTGRES_CONNECTION_STRING.");
        }

        string stagingDirectory = options.OutputPath;
        if (Path.HasExtension(stagingDirectory))
        {
            stagingDirectory = Path.GetDirectoryName(stagingDirectory) ?? options.RootPath;
        }

        if (string.IsNullOrWhiteSpace(stagingDirectory))
        {
            stagingDirectory = Path.Combine(options.RootPath, "output", "postgres_sync");
        }

        Directory.CreateDirectory(stagingDirectory);

        int annotatedEntityCount = 0;
        int insertedRows = 0;

        bool baseDataAlreadyLoaded = options.Resume && HasBaseData(connectionString);
        bool archetypesAlreadyLoaded = options.Resume && TableHasRows(connectionString, "archetypes");
        bool clustersAlreadyLoaded = options.Resume && TableHasRows(connectionString, "synergy_clusters");

        if (!baseDataAlreadyLoaded)
        {
            Console.WriteLine("[sync-postgres] Starting extraction phase...");

            List<CardExtractionResult> cardResults = new List<CardExtractionResult>();
            List<RelicExtractionResult> relicResults = new List<RelicExtractionResult>();
            List<PotionExtractionResult> potionResults = new List<PotionExtractionResult>();
            if (options.AllCharacters)
            {
                for (int i = 0; i < FullSyncCardCharacterIds.Length; i++)
                {
                    string characterId = FullSyncCardCharacterIds[i];
                    string characterDirectory = Path.Combine(stagingDirectory, characterId);
                    Directory.CreateDirectory(characterDirectory);

                    Console.WriteLine($"[sync-postgres] Extracting cards for '{characterId}'...");
                    CliOptions cardOptions = options.ForCommand(CliCommand.ExtractCards, Path.Combine(characterDirectory, "card_extract_preview.csv"));
                    cardOptions = cardOptions.WithCharacter(characterId);
                    cardResults.Add(new CardExtractionRunner().Run(cardOptions));

                    try
                    {
                        Console.WriteLine($"[sync-postgres] Extracting relics for '{characterId}'...");
                        CliOptions relicOptions = options.ForCommand(CliCommand.ExtractRelics, Path.Combine(characterDirectory, "relic_extract_preview.csv"));
                        relicOptions = relicOptions.WithCharacter(characterId);
                        relicResults.Add(new RelicExtractionRunner().Run(relicOptions));
                    }
                    catch (FileNotFoundException ex)
                    {
                        Console.WriteLine($"[sync-postgres] Skipping '{characterId}' relic pool extract: {ex.Message}");
                    }

                    try
                    {
                        Console.WriteLine($"[sync-postgres] Extracting potions for '{characterId}'...");
                        CliOptions potionOptions = options.ForCommand(CliCommand.ExtractPotions, Path.Combine(characterDirectory, "potion_extract_preview.csv"));
                        potionOptions = potionOptions.WithCharacter(characterId);
                        potionResults.Add(new PotionExtractionRunner().Run(potionOptions));
                    }
                    catch (FileNotFoundException ex)
                    {
                        Console.WriteLine($"[sync-postgres] Skipping '{characterId}' potion pool extract: {ex.Message}");
                    }
                }

                string sharedDirectory = Path.Combine(stagingDirectory, "shared");
                Directory.CreateDirectory(sharedDirectory);

                Console.WriteLine("[sync-postgres] Extracting shared/unaffiliated relics...");
                relicResults.Add(new RelicExtractionRunner().Run(
                    options.ForCommand(CliCommand.ExtractRelics, Path.Combine(sharedDirectory, "relic_extract_preview.csv"))));

                Console.WriteLine("[sync-postgres] Extracting shared/unaffiliated potions...");
                potionResults.Add(new PotionExtractionRunner().Run(
                    options.ForCommand(CliCommand.ExtractPotions, Path.Combine(sharedDirectory, "potion_extract_preview.csv"))));
            }
            else
            {
                Console.WriteLine("[sync-postgres] Extracting cards...");
                cardResults.Add(new CardExtractionRunner().Run(options.ForCommand(CliCommand.ExtractCards, Path.Combine(stagingDirectory, "card_extract_preview.csv"))));

                Console.WriteLine("[sync-postgres] Extracting relics...");
                relicResults.Add(new RelicExtractionRunner().Run(options.ForCommand(CliCommand.ExtractRelics, Path.Combine(stagingDirectory, "relic_extract_preview.csv"))));

                Console.WriteLine("[sync-postgres] Extracting potions...");
                potionResults.Add(new PotionExtractionRunner().Run(options.ForCommand(CliCommand.ExtractPotions, Path.Combine(stagingDirectory, "potion_extract_preview.csv"))));
            }

            Console.WriteLine("[sync-postgres] Extracting events/powers/characters + taxonomy seed...");
            EventExtractionResult eventResult = new EventExtractionRunner().Run(options.ForCommand(CliCommand.ExtractEvents, Path.Combine(stagingDirectory, "event_extract_preview.csv")));
            PowerExtractionResult power = new PowerExtractionRunner().Run(options.ForCommand(CliCommand.ExtractPowers, Path.Combine(stagingDirectory, "power_extract_preview.csv")));
            TaxonomySeedResult taxonomy = new TaxonomySeedRunner().Run(options.ForCommand(CliCommand.SeedTaxonomy, Path.Combine(stagingDirectory, "effect_taxonomy_seed.csv")));
            CharacterExtractionResult characterResult = new CharacterExtractionRunner().Run(options.ForCommand(CliCommand.ExtractCharacters, Path.Combine(stagingDirectory, "character_extract_preview.csv")));

            if (options.Annotate)
            {
                Console.WriteLine("[sync-postgres] Running mechanical annotation...");
                List<string> cardIngestionPaths = new List<string>(cardResults.Count);
                for (int i = 0; i < cardResults.Count; i++)
                {
                    cardIngestionPaths.Add(cardResults[i].IngestionOutputPath);
                }

                List<string> relicIngestionPaths = new List<string>(relicResults.Count);
                for (int i = 0; i < relicResults.Count; i++)
                {
                    relicIngestionPaths.Add(relicResults[i].IngestionOutputPath);
                }

                List<string> potionIngestionPaths = new List<string>(potionResults.Count);
                for (int i = 0; i < potionResults.Count; i++)
                {
                    potionIngestionPaths.Add(potionResults[i].IngestionOutputPath);
                }

                MechanicalAnnotationResult annotationResult = new MechanicalAnnotationRunner().Run(
                    options,
                    cardIngestionPaths,
                    relicIngestionPaths,
                    potionIngestionPaths,
                    power.IngestionOutputPath);

                annotatedEntityCount = annotationResult.AnnotatedCount;
                Console.WriteLine($"[sync-postgres] Annotation complete: {annotationResult.AnnotatedCount} annotated, {annotationResult.SkippedCount} skipped, {annotationResult.BatchCount} batches.");
            }

            Console.WriteLine("[sync-postgres] Loading base tables...");
            using (NpgsqlConnection connection = new NpgsqlConnection(connectionString))
            {
                connection.Open();
                using NpgsqlTransaction transaction = connection.BeginTransaction();

                EnsureSupplementalTables(connection, transaction);
                ClearExistingRows(connection, transaction);

                insertedRows += InsertCharacters(connection, transaction, characterResult.IngestionOutputPath);

                for (int i = 0; i < cardResults.Count; i++)
                {
                    CardExtractionResult cardResult = cardResults[i];
                    insertedRows += InsertCards(connection, transaction, cardResult.IngestionOutputPath);
                    insertedRows += InsertCardVariants(connection, transaction, cardResult.VariantOutputPath);
                    insertedRows += InsertCompanionActions(connection, transaction, cardResult.CompanionOutputPath);
                }

                for (int i = 0; i < relicResults.Count; i++)
                {
                    RelicExtractionResult relicResult = relicResults[i];
                    insertedRows += InsertRelics(connection, transaction, relicResult.IngestionOutputPath);
                    insertedRows += InsertHookListeners(connection, transaction, relicResult.HooksOutputPath);
                }

                for (int i = 0; i < potionResults.Count; i++)
                {
                    PotionExtractionResult potionResult = potionResults[i];
                    insertedRows += InsertPotions(connection, transaction, potionResult.IngestionOutputPath);
                }

                insertedRows += InsertEvents(connection, transaction, eventResult.IngestionOutputPath);
                insertedRows += InsertEventOptions(connection, transaction, eventResult.OptionsOutputPath);
                insertedRows += InsertEventOutcomes(connection, transaction, eventResult.OutcomesOutputPath);
                insertedRows += InsertPowers(connection, transaction, power.IngestionOutputPath);
                insertedRows += InsertHookListeners(connection, transaction, power.HooksOutputPath);
                insertedRows += InsertTaxonomy(connection, transaction, taxonomy.OutputPath);

                transaction.Commit();
            }
        }
        else
        {
            Console.WriteLine("[sync-postgres] Resume enabled: base tables already populated, skipping extraction/base load phase.");
        }

        BuildSynergyClustersResult clusters = new BuildSynergyClustersResult();
        DiscoverArchetypesResult discoveredArchetypes = new DiscoverArchetypesResult();

        if (!archetypesAlreadyLoaded || !clustersAlreadyLoaded)
        {
            Console.WriteLine("[sync-postgres] [generation] Rebuilding archetypes + synergy_clusters...");
            clusters = new BuildSynergyClustersRunner().Run(
                options.ForCommand(CliCommand.BuildSynergyClusters, Path.Combine(stagingDirectory, "synergy_clusters_staging.csv")));
            Console.WriteLine($"[sync-postgres] [generation] Cluster outputs: {clusters.ArchetypeCount} archetypes, {clusters.ClusterCount} cluster rows.");

            using (NpgsqlConnection connection = new NpgsqlConnection(connectionString))
            {
                connection.Open();
                using NpgsqlTransaction transaction = connection.BeginTransaction();
                ExecuteNonQuery(connection, transaction, "DELETE FROM synergy_clusters;");
                ExecuteNonQuery(connection, transaction, "DELETE FROM entity_archetype_affinity;");
                ExecuteNonQuery(connection, transaction, "DELETE FROM archetypes;");
                insertedRows += InsertArchetypes(connection, transaction, clusters.ArchetypesOutputPath);
                insertedRows += InsertSynergyClusters(connection, transaction, clusters.ClustersOutputPath);
                transaction.Commit();
            }

            string discoveredArchetypesPath = Path.Combine(stagingDirectory, "archetypes_discovery_staging.csv");
            Console.WriteLine("[sync-postgres] [generation] Discovering concept archetypes...");
            discoveredArchetypes = new DiscoverArchetypesRunner().Run(
                options.ForCommand(CliCommand.DiscoverArchetypes, discoveredArchetypesPath));
            Console.WriteLine($"[sync-postgres] [generation] Discovered {discoveredArchetypes.ArchetypeCount} archetypes across {discoveredArchetypes.CharacterCount} characters.");
        }
        else
        {
            Console.WriteLine("[sync-postgres] Resume enabled: archetypes/clusters already present, skipping rebuild/discovery phase.");
            clusters.ArchetypeCount = CountRows(connectionString, "archetypes");
            clusters.ClusterCount = CountRows(connectionString, "synergy_clusters");
            discoveredArchetypes.CharacterCount = options.AllCharacters ? FullSyncCardCharacterIds.Length - 1 : 1;
            discoveredArchetypes.ArchetypeCount = clusters.ArchetypeCount;
        }

        string affinitiesPath = Path.Combine(stagingDirectory, "entity_archetype_affinity_staging.csv");
        Console.WriteLine("[sync-postgres] [generation] Scoring entity-archetype affinities...");
        CliOptions affinityOptions = options.ForCommand(CliCommand.ScoreAffinities, affinitiesPath);
        if (options.Resume && !affinityOptions.OnlyMissingAffinities)
        {
            affinityOptions = affinityOptions.WithOnlyMissingAffinities();
            Console.WriteLine("[sync-postgres] Resume enabled: forcing --only-missing for affinity scoring.");
        }

        ScoreAffinitiesResult affinities = new ScoreAffinitiesRunner().Run(affinityOptions);
        Console.WriteLine($"[sync-postgres] [generation] Affinities produced: {affinities.AffinityCount} rows.");

        string ratingsPath = Path.Combine(stagingDirectory, "entity_strength_ratings_staging.csv");
        Console.WriteLine("[sync-postgres] [generation] Rebuilding entity_strength_ratings from archetype affinities...");
        EntityStrengthRatingsResult ratings = new EntityStrengthRatingsRunner().Run(options.ForCommand(CliCommand.GenerateRatings, ratingsPath));
        Console.WriteLine($"[sync-postgres] [generation] Ratings produced: {ratings.RecordCount} records.");

        using (NpgsqlConnection connection = new NpgsqlConnection(connectionString))
        {
            connection.Open();
            using NpgsqlTransaction transaction = connection.BeginTransaction();
            ExecuteNonQuery(connection, transaction, "DELETE FROM entity_strength_ratings;");
            insertedRows += InsertRatings(connection, transaction, ratings.OutputPath);
            transaction.Commit();
        }

        return new PostgresSyncResult
        {
            InsertedRowCount = insertedRows,
            AnnotatedEntityCount = annotatedEntityCount,
            RatingsCount = ratings.RecordCount,
            ArchetypeCount = clusters.ArchetypeCount,
            SynergyClusterCount = clusters.ClusterCount,
            AffinityCount = affinities.AffinityCount,
            ConnectionDisplay = DatabaseSettingsResolver.RedactConnectionString(connectionString),
            StagingDirectory = stagingDirectory
        };
    }

    private static void EnsureSupplementalTables(NpgsqlConnection connection, NpgsqlTransaction transaction)
    {
        ExecuteNonQuery(connection, transaction, @"
CREATE TABLE IF NOT EXISTS event_options (
    event_id TEXT NOT NULL,
    option_key TEXT NOT NULL,
    title TEXT,
    description TEXT,
    handler_method TEXT,
    handler_source TEXT,
    PRIMARY KEY (event_id, option_key)
);");

        ExecuteNonQuery(connection, transaction, @"
CREATE TABLE IF NOT EXISTS event_outcomes (
    event_id TEXT NOT NULL,
    outcome_id TEXT NOT NULL,
    title TEXT,
    description TEXT,
    outcome_kind TEXT,
    outcome_source TEXT,
    PRIMARY KEY (event_id, outcome_id)
);");

        ExecuteNonQuery(connection, transaction, @"
CREATE TABLE IF NOT EXISTS entity_archetype_affinity (
    archetype_id INTEGER NOT NULL REFERENCES archetypes(id),
    entity_type TEXT NOT NULL,
    entity_id TEXT NOT NULL,
    affinity_score INTEGER NOT NULL CHECK (affinity_score BETWEEN 0 AND 10),
    PRIMARY KEY (archetype_id, entity_type, entity_id)
);");

        ExecuteNonQuery(connection, transaction, "CREATE INDEX IF NOT EXISTS idx_entity_archetype_affinity_entity ON entity_archetype_affinity(entity_type, entity_id);");
        ExecuteNonQuery(connection, transaction, "CREATE INDEX IF NOT EXISTS idx_entity_archetype_affinity_archetype ON entity_archetype_affinity(archetype_id);");
        ExecuteNonQuery(connection, transaction, "ALTER TABLE event_options ADD COLUMN IF NOT EXISTS title TEXT;");
        ExecuteNonQuery(connection, transaction, "ALTER TABLE event_options ADD COLUMN IF NOT EXISTS description TEXT;");
        ExecuteNonQuery(connection, transaction, "ALTER TABLE event_outcomes ADD COLUMN IF NOT EXISTS title TEXT;");
        ExecuteNonQuery(connection, transaction, "ALTER TABLE event_outcomes ADD COLUMN IF NOT EXISTS description TEXT;");
        ExecuteNonQuery(connection, transaction, "ALTER TABLE characters ADD COLUMN IF NOT EXISTS description TEXT;");
        ExecuteNonQuery(connection, transaction, "ALTER TABLE characters ADD COLUMN IF NOT EXISTS starting_hp INTEGER;");
        ExecuteNonQuery(connection, transaction, "ALTER TABLE characters ADD COLUMN IF NOT EXISTS starting_gold INTEGER;");
        ExecuteNonQuery(connection, transaction, "ALTER TABLE characters ADD COLUMN IF NOT EXISTS max_energy INTEGER;");
        ExecuteNonQuery(connection, transaction, "ALTER TABLE characters ADD COLUMN IF NOT EXISTS orb_slots INTEGER;");
        ExecuteNonQuery(connection, transaction, "ALTER TABLE characters ADD COLUMN IF NOT EXISTS starting_deck TEXT[];");
        ExecuteNonQuery(connection, transaction, "ALTER TABLE characters ADD COLUMN IF NOT EXISTS starting_relics TEXT[];");
    }

    private static int InsertCharacters(NpgsqlConnection connection, NpgsqlTransaction transaction, string csvPath)
    {
        List<Dictionary<string, string>> rows = CsvReader.ReadRows(csvPath);
        int count = 0;
        for (int i = 0; i < rows.Count; i++)
        {
            Dictionary<string, string> row = rows[i];
            using NpgsqlCommand command = new NpgsqlCommand(@"
INSERT INTO characters (
    id, title, description, starting_hp, starting_gold, max_energy, orb_slots,
    starting_deck, starting_relics
) VALUES (
    @id, @title, @description, @starting_hp, @starting_gold, @max_energy, @orb_slots,
    @starting_deck, @starting_relics
) ON CONFLICT (id)
DO UPDATE SET
    title = EXCLUDED.title,
    description = EXCLUDED.description,
    starting_hp = EXCLUDED.starting_hp,
    starting_gold = EXCLUDED.starting_gold,
    max_energy = EXCLUDED.max_energy,
    orb_slots = EXCLUDED.orb_slots,
    starting_deck = EXCLUDED.starting_deck,
    starting_relics = EXCLUDED.starting_relics;", connection, transaction);

            command.Parameters.AddWithValue("id", Read(row, "character_id"));
            command.Parameters.AddWithValue("title", DbValue(Read(row, "title")));
            command.Parameters.AddWithValue("description", DbValue(Read(row, "description")));
            command.Parameters.AddWithValue("starting_hp", DbInt(Read(row, "starting_hp")));
            command.Parameters.AddWithValue("starting_gold", DbInt(Read(row, "starting_gold")));
            command.Parameters.AddWithValue("max_energy", ParseInt(Read(row, "max_energy"), 3));
            command.Parameters.AddWithValue("orb_slots", ParseInt(Read(row, "orb_slots"), 0));
            command.Parameters.Add("starting_deck", NpgsqlDbType.Array | NpgsqlDbType.Text).Value = ParseArray(Read(row, "starting_deck"));
            command.Parameters.Add("starting_relics", NpgsqlDbType.Array | NpgsqlDbType.Text).Value = ParseArray(Read(row, "starting_relics"));
            command.ExecuteNonQuery();
            count++;
        }

        return count;
    }

    private static void ClearExistingRows(NpgsqlConnection connection, NpgsqlTransaction transaction)
    {
        List<string> statements = new List<string>();

        statements.AddRange(new[]
        {
            "DELETE FROM synergy_clusters;",
            "DELETE FROM entity_archetype_affinity;",
            "DELETE FROM archetypes;",
            "DELETE FROM entity_strength_ratings;",
            "DELETE FROM effect_taxonomy;",
            "DELETE FROM event_outcomes;",
            "DELETE FROM event_options;",
            "DELETE FROM events;",
            "DELETE FROM potions;",
            "DELETE FROM hook_listeners WHERE entity_type = 'power';",
            "DELETE FROM powers;",
            "DELETE FROM hook_listeners WHERE entity_type = 'relic';",
            "DELETE FROM relics;",
            "DELETE FROM card_companion_actions;",
            "DELETE FROM card_variants;",
            "DELETE FROM cards;",
            "DELETE FROM characters;"
        });

        for (int i = 0; i < statements.Count; i++)
        {
            ExecuteNonQuery(connection, transaction, statements[i]);
        }
    }

    private static int InsertCards(NpgsqlConnection connection, NpgsqlTransaction transaction, string csvPath)
    {
        List<Dictionary<string, string>> rows = CsvReader.ReadRows(csvPath);
        int count = 0;
        for (int i = 0; i < rows.Count; i++)
        {
            Dictionary<string, string> row = rows[i];
            using NpgsqlCommand command = new NpgsqlCommand(@"
INSERT INTO cards (
    id, character_id, title, description, type, rarity, energy_cost, target, gains_block, keywords,
    damage_base, damage_upgraded, block_base, block_upgraded, magic_base, magic_upgraded,
    max_upgrade, on_play_source, on_upgrade_source, effect_tags, effect_description,
    resources_generated, resources_consumed, scaling_type, anti_synergy_tags
) VALUES (
    @id, @character_id, @title, @description, @type, @rarity, @energy_cost, @target, @gains_block, @keywords,
    @damage_base, @damage_upgraded, @block_base, @block_upgraded, @magic_base, @magic_upgraded,
    @max_upgrade, @on_play_source, @on_upgrade_source, @effect_tags, @effect_description,
    @resources_generated, @resources_consumed, @scaling_type, @anti_synergy_tags
) ON CONFLICT (id)
DO UPDATE SET
    character_id = EXCLUDED.character_id,
    title = EXCLUDED.title,
    description = EXCLUDED.description,
    type = EXCLUDED.type,
    rarity = EXCLUDED.rarity,
    energy_cost = EXCLUDED.energy_cost,
    target = EXCLUDED.target,
    gains_block = EXCLUDED.gains_block,
    keywords = EXCLUDED.keywords,
    damage_base = EXCLUDED.damage_base,
    damage_upgraded = EXCLUDED.damage_upgraded,
    block_base = EXCLUDED.block_base,
    block_upgraded = EXCLUDED.block_upgraded,
    magic_base = EXCLUDED.magic_base,
    magic_upgraded = EXCLUDED.magic_upgraded,
    max_upgrade = EXCLUDED.max_upgrade,
    on_play_source = EXCLUDED.on_play_source,
    on_upgrade_source = EXCLUDED.on_upgrade_source,
    effect_tags = EXCLUDED.effect_tags,
    effect_description = EXCLUDED.effect_description,
    resources_generated = EXCLUDED.resources_generated,
    resources_consumed = EXCLUDED.resources_consumed,
    scaling_type = EXCLUDED.scaling_type,
    anti_synergy_tags = EXCLUDED.anti_synergy_tags;", connection, transaction);

            command.Parameters.AddWithValue("id", Read(row, "card_id"));
            command.Parameters.AddWithValue("character_id", DbCharacterId(Read(row, "character_id")));
            command.Parameters.AddWithValue("title", DbValue(Read(row, "title")));
            command.Parameters.AddWithValue("description", DbValue(Read(row, "description")));
            command.Parameters.AddWithValue("type", Read(row, "type"));
            command.Parameters.AddWithValue("rarity", Read(row, "rarity"));
            command.Parameters.AddWithValue("energy_cost", DbInt(Read(row, "energy_cost")));
            command.Parameters.AddWithValue("target", DbValue(Read(row, "target")));
            command.Parameters.AddWithValue("gains_block", ParseBool(Read(row, "gains_block")));
            command.Parameters.Add("keywords", NpgsqlDbType.Array | NpgsqlDbType.Text).Value = ParseArray(Read(row, "keywords"));
            command.Parameters.AddWithValue("damage_base", DbInt(Read(row, "damage_base")));
            command.Parameters.AddWithValue("damage_upgraded", DbInt(Read(row, "damage_upgraded")));
            command.Parameters.AddWithValue("block_base", DbInt(Read(row, "block_base")));
            command.Parameters.AddWithValue("block_upgraded", DbInt(Read(row, "block_upgraded")));
            command.Parameters.AddWithValue("magic_base", DbInt(Read(row, "magic_base")));
            command.Parameters.AddWithValue("magic_upgraded", DbInt(Read(row, "magic_upgraded")));
            command.Parameters.AddWithValue("max_upgrade", ParseInt(Read(row, "max_upgrade"), 1));
            command.Parameters.AddWithValue("on_play_source", DbValue(Read(row, "on_play_source")));
            command.Parameters.AddWithValue("on_upgrade_source", DbValue(Read(row, "on_upgrade_source")));
            command.Parameters.Add("effect_tags", NpgsqlDbType.Array | NpgsqlDbType.Text).Value = ParseArray(Read(row, "effect_tags"));
            command.Parameters.AddWithValue("effect_description", DbValue(Read(row, "effect_description")));
            command.Parameters.Add("resources_generated", NpgsqlDbType.Array | NpgsqlDbType.Text).Value = ParseArray(Read(row, "resources_generated"));
            command.Parameters.Add("resources_consumed", NpgsqlDbType.Array | NpgsqlDbType.Text).Value = ParseArray(Read(row, "resources_consumed"));
            command.Parameters.AddWithValue("scaling_type", DbValue(Read(row, "scaling_type")));
            command.Parameters.Add("anti_synergy_tags", NpgsqlDbType.Array | NpgsqlDbType.Text).Value = ParseArray(Read(row, "anti_synergy_tags"));
            command.ExecuteNonQuery();
            count++;
        }

        return count;
    }

    private static int InsertCardVariants(NpgsqlConnection connection, NpgsqlTransaction transaction, string csvPath)
    {
        List<Dictionary<string, string>> rows = CsvReader.ReadRows(csvPath);
        int count = 0;
        for (int i = 0; i < rows.Count; i++)
        {
            Dictionary<string, string> row = rows[i];
            using NpgsqlCommand command = new NpgsqlCommand(@"
INSERT INTO card_variants (
    card_id, variant_kind, upgrade_level, source_id, energy_cost, damage_value, block_value,
    magic_value, effect_tags, effect_description, resources_generated, resources_consumed,
    scaling_type, anti_synergy_tags
) VALUES (
    @card_id, @variant_kind, @upgrade_level, @source_id, @energy_cost, @damage_value, @block_value,
    @magic_value, @effect_tags, @effect_description, @resources_generated, @resources_consumed,
    @scaling_type, @anti_synergy_tags
) ON CONFLICT (card_id, upgrade_level)
DO UPDATE SET
    variant_kind = EXCLUDED.variant_kind,
    source_id = EXCLUDED.source_id,
    energy_cost = EXCLUDED.energy_cost,
    damage_value = EXCLUDED.damage_value,
    block_value = EXCLUDED.block_value,
    magic_value = EXCLUDED.magic_value,
    effect_tags = EXCLUDED.effect_tags,
    effect_description = EXCLUDED.effect_description,
    resources_generated = EXCLUDED.resources_generated,
    resources_consumed = EXCLUDED.resources_consumed,
    scaling_type = EXCLUDED.scaling_type,
    anti_synergy_tags = EXCLUDED.anti_synergy_tags;", connection, transaction);

            command.Parameters.AddWithValue("card_id", Read(row, "card_id"));
            command.Parameters.AddWithValue("variant_kind", Read(row, "variant_kind"));
            command.Parameters.AddWithValue("upgrade_level", DbInt(Read(row, "upgrade_level")));
            command.Parameters.AddWithValue("source_id", DbValue(Read(row, "source_id")));
            command.Parameters.AddWithValue("energy_cost", DbInt(Read(row, "energy_cost")));
            command.Parameters.AddWithValue("damage_value", DbInt(Read(row, "damage_value")));
            command.Parameters.AddWithValue("block_value", DbInt(Read(row, "block_value")));
            command.Parameters.AddWithValue("magic_value", DbInt(Read(row, "magic_value")));
            command.Parameters.Add("effect_tags", NpgsqlDbType.Array | NpgsqlDbType.Text).Value = ParseArray(Read(row, "effect_tags"));
            command.Parameters.AddWithValue("effect_description", DbValue(Read(row, "effect_description")));
            command.Parameters.Add("resources_generated", NpgsqlDbType.Array | NpgsqlDbType.Text).Value = ParseArray(Read(row, "resources_generated"));
            command.Parameters.Add("resources_consumed", NpgsqlDbType.Array | NpgsqlDbType.Text).Value = ParseArray(Read(row, "resources_consumed"));
            command.Parameters.AddWithValue("scaling_type", DbValue(Read(row, "scaling_type")));
            command.Parameters.Add("anti_synergy_tags", NpgsqlDbType.Array | NpgsqlDbType.Text).Value = ParseArray(Read(row, "anti_synergy_tags"));
            command.ExecuteNonQuery();
            count++;
        }

        return count;
    }

    private static int InsertCompanionActions(NpgsqlConnection connection, NpgsqlTransaction transaction, string csvPath)
    {
        List<Dictionary<string, string>> rows = CsvReader.ReadRows(csvPath);
        int count = 0;
        for (int i = 0; i < rows.Count; i++)
        {
            Dictionary<string, string> row = rows[i];
            using NpgsqlCommand command = new NpgsqlCommand(@"
INSERT INTO card_companion_actions (card_id, companion_id, action_tag, source_method)
VALUES (@card_id, @companion_id, @action_tag, @source_method)
ON CONFLICT (card_id, companion_id)
DO UPDATE SET
    action_tag = EXCLUDED.action_tag,
    source_method = EXCLUDED.source_method;", connection, transaction);

            command.Parameters.AddWithValue("card_id", Read(row, "card_id"));
            command.Parameters.AddWithValue("companion_id", Read(row, "companion_id"));
            command.Parameters.AddWithValue("action_tag", Read(row, "action_tag"));
            command.Parameters.AddWithValue("source_method", DbValue(Read(row, "source_method")));
            command.ExecuteNonQuery();
            count++;
        }

        return count;
    }

    private static int InsertRelics(NpgsqlConnection connection, NpgsqlTransaction transaction, string csvPath)
    {
        List<Dictionary<string, string>> rows = CsvReader.ReadRows(csvPath);
        int count = 0;
        for (int i = 0; i < rows.Count; i++)
        {
            Dictionary<string, string> row = rows[i];
            using NpgsqlCommand command = new NpgsqlCommand(@"
INSERT INTO relics (
    id, character_id, title, description, flavor_text, rarity, counter_base, amount_base,
    hook_source, effect_tags, effect_description, triggers_on, trigger_condition,
    resources_generated, resources_consumed
) VALUES (
    @id, @character_id, @title, @description, @flavor_text, @rarity, @counter_base, @amount_base,
    @hook_source, @effect_tags, @effect_description, @triggers_on, @trigger_condition,
    @resources_generated, @resources_consumed
) ON CONFLICT (id)
DO UPDATE SET
    character_id = COALESCE(relics.character_id, EXCLUDED.character_id),
    title = EXCLUDED.title,
    description = EXCLUDED.description,
    flavor_text = EXCLUDED.flavor_text,
    rarity = EXCLUDED.rarity,
    counter_base = EXCLUDED.counter_base,
    amount_base = EXCLUDED.amount_base,
    hook_source = EXCLUDED.hook_source,
    effect_tags = EXCLUDED.effect_tags,
    effect_description = EXCLUDED.effect_description,
    triggers_on = EXCLUDED.triggers_on,
    trigger_condition = EXCLUDED.trigger_condition,
    resources_generated = EXCLUDED.resources_generated,
    resources_consumed = EXCLUDED.resources_consumed;", connection, transaction);

            command.Parameters.AddWithValue("id", Read(row, "relic_id"));
            command.Parameters.AddWithValue("character_id", DbCharacterId(Read(row, "character_id")));
            command.Parameters.AddWithValue("title", DbValue(Read(row, "title")));
            command.Parameters.AddWithValue("description", DbValue(Read(row, "description")));
            command.Parameters.AddWithValue("flavor_text", DbValue(Read(row, "flavor_text")));
            command.Parameters.AddWithValue("rarity", Read(row, "rarity"));
            command.Parameters.AddWithValue("counter_base", DbInt(Read(row, "counter_base")));
            command.Parameters.AddWithValue("amount_base", DbInt(Read(row, "amount_base")));
            command.Parameters.AddWithValue("hook_source", DbValue(Read(row, "hook_source")));
            command.Parameters.Add("effect_tags", NpgsqlDbType.Array | NpgsqlDbType.Text).Value = ParseArray(Read(row, "effect_tags"));
            command.Parameters.AddWithValue("effect_description", DbValue(Read(row, "effect_description")));
            command.Parameters.Add("triggers_on", NpgsqlDbType.Array | NpgsqlDbType.Text).Value = ParseArray(Read(row, "triggers_on"));
            command.Parameters.AddWithValue("trigger_condition", DbValue(Read(row, "trigger_condition")));
            command.Parameters.Add("resources_generated", NpgsqlDbType.Array | NpgsqlDbType.Text).Value = ParseArray(Read(row, "resources_generated"));
            command.Parameters.Add("resources_consumed", NpgsqlDbType.Array | NpgsqlDbType.Text).Value = ParseArray(Read(row, "resources_consumed"));
            command.ExecuteNonQuery();
            count++;
        }

        return count;
    }

    private static int InsertHookListeners(NpgsqlConnection connection, NpgsqlTransaction transaction, string csvPath)
    {
        List<Dictionary<string, string>> rows = CsvReader.ReadRows(csvPath);
        int count = 0;
        for (int i = 0; i < rows.Count; i++)
        {
            Dictionary<string, string> row = rows[i];
            using NpgsqlCommand command = new NpgsqlCommand(@"
INSERT INTO hook_listeners (entity_type, entity_id, hook_name)
VALUES (@entity_type, @entity_id, @hook_name)
ON CONFLICT (entity_type, entity_id, hook_name)
DO NOTHING;", connection, transaction);

            command.Parameters.AddWithValue("entity_type", Read(row, "entity_type"));
            command.Parameters.AddWithValue("entity_id", Read(row, "entity_id"));
            command.Parameters.AddWithValue("hook_name", Read(row, "hook_name"));
            command.ExecuteNonQuery();
            count++;
        }

        return count;
    }

    private static int InsertPotions(NpgsqlConnection connection, NpgsqlTransaction transaction, string csvPath)
    {
        List<Dictionary<string, string>> rows = CsvReader.ReadRows(csvPath);
        int count = 0;
        for (int i = 0; i < rows.Count; i++)
        {
            Dictionary<string, string> row = rows[i];
            using NpgsqlCommand command = new NpgsqlCommand(@"
INSERT INTO potions (
    id, character_id, title, description, rarity, amount_base, on_use_source,
    effect_tags, effect_description
) VALUES (
    @id, @character_id, @title, @description, @rarity, @amount_base, @on_use_source,
    @effect_tags, @effect_description
) ON CONFLICT (id)
DO UPDATE SET
    character_id = COALESCE(potions.character_id, EXCLUDED.character_id),
    title = EXCLUDED.title,
    description = EXCLUDED.description,
    rarity = EXCLUDED.rarity,
    amount_base = EXCLUDED.amount_base,
    on_use_source = EXCLUDED.on_use_source,
    effect_tags = EXCLUDED.effect_tags,
    effect_description = EXCLUDED.effect_description;", connection, transaction);

            command.Parameters.AddWithValue("id", Read(row, "potion_id"));
            command.Parameters.AddWithValue("character_id", DbCharacterId(Read(row, "character_id")));
            command.Parameters.AddWithValue("title", DbValue(Read(row, "title")));
            command.Parameters.AddWithValue("description", DbValue(Read(row, "description")));
            command.Parameters.AddWithValue("rarity", DbValue(Read(row, "rarity")));
            command.Parameters.AddWithValue("amount_base", DbInt(Read(row, "amount_base")));
            command.Parameters.AddWithValue("on_use_source", DbValue(Read(row, "on_use_source")));
            command.Parameters.Add("effect_tags", NpgsqlDbType.Array | NpgsqlDbType.Text).Value = ParseArray(Read(row, "effect_tags"));
            command.Parameters.AddWithValue("effect_description", DbValue(Read(row, "effect_description")));
            command.ExecuteNonQuery();
            count++;
        }

        return count;
    }

    private static int InsertPowers(NpgsqlConnection connection, NpgsqlTransaction transaction, string csvPath)
    {
        List<Dictionary<string, string>> rows = CsvReader.ReadRows(csvPath);
        int count = 0;
        for (int i = 0; i < rows.Count; i++)
        {
            Dictionary<string, string> row = rows[i];
            using NpgsqlCommand command = new NpgsqlCommand(@"
INSERT INTO powers (
    id, title, description, type, stack_type, is_player_power, hook_source,
    effect_tags, effect_description, triggers_on, scaling_type
) VALUES (
    @id, @title, @description, @type, @stack_type, @is_player_power, @hook_source,
    @effect_tags, @effect_description, @triggers_on, @scaling_type
);", connection, transaction);

            command.Parameters.AddWithValue("id", Read(row, "power_id"));
            command.Parameters.AddWithValue("title", DbValue(Read(row, "title")));
            command.Parameters.AddWithValue("description", DbValue(Read(row, "description")));
            command.Parameters.AddWithValue("type", DbValue(Read(row, "type")));
            command.Parameters.AddWithValue("stack_type", DbValue(Read(row, "stack_type")));
            command.Parameters.AddWithValue("is_player_power", ParseBool(Read(row, "is_player_power")));
            command.Parameters.AddWithValue("hook_source", DbValue(Read(row, "hook_source")));
            command.Parameters.Add("effect_tags", NpgsqlDbType.Array | NpgsqlDbType.Text).Value = ParseArray(Read(row, "effect_tags"));
            command.Parameters.AddWithValue("effect_description", DbValue(Read(row, "effect_description")));
            command.Parameters.Add("triggers_on", NpgsqlDbType.Array | NpgsqlDbType.Text).Value = ParseArray(Read(row, "triggers_on"));
            command.Parameters.AddWithValue("scaling_type", DbValue(Read(row, "scaling_type")));
            command.ExecuteNonQuery();
            count++;
        }

        return count;
    }

    private static int InsertEvents(NpgsqlConnection connection, NpgsqlTransaction transaction, string csvPath)
    {
        List<Dictionary<string, string>> rows = CsvReader.ReadRows(csvPath);
        int count = 0;
        for (int i = 0; i < rows.Count; i++)
        {
            Dictionary<string, string> row = rows[i];
            using NpgsqlCommand command = new NpgsqlCommand(@"
INSERT INTO events (
    id, title, description, is_deterministic, options_summary
) VALUES (
    @id, @title, @description, @is_deterministic, @options_summary
);", connection, transaction);

            command.Parameters.AddWithValue("id", Read(row, "event_id"));
            command.Parameters.AddWithValue("title", DbValue(Read(row, "title")));
            command.Parameters.AddWithValue("description", DbValue(Read(row, "description")));
            command.Parameters.AddWithValue("is_deterministic", ParseBool(Read(row, "is_deterministic")));
            command.Parameters.AddWithValue("options_summary", DbValue(Read(row, "options_summary")));
            command.ExecuteNonQuery();
            count++;
        }

        return count;
    }

    private static int InsertEventOptions(NpgsqlConnection connection, NpgsqlTransaction transaction, string csvPath)
    {
        List<Dictionary<string, string>> rows = CsvReader.ReadRows(csvPath);
        int count = 0;
        for (int i = 0; i < rows.Count; i++)
        {
            Dictionary<string, string> row = rows[i];
            using NpgsqlCommand command = new NpgsqlCommand(@"
INSERT INTO event_options (event_id, option_key, title, description, handler_method, handler_source)
VALUES (@event_id, @option_key, @title, @description, @handler_method, @handler_source);", connection, transaction);

            command.Parameters.AddWithValue("event_id", Read(row, "event_id"));
            command.Parameters.AddWithValue("option_key", Read(row, "option_text_key"));
            command.Parameters.AddWithValue("title", DbValue(Read(row, "title")));
            command.Parameters.AddWithValue("description", DbValue(Read(row, "description")));
            command.Parameters.AddWithValue("handler_method", DbValue(Read(row, "handler_method")));
            command.Parameters.AddWithValue("handler_source", DbValue(Read(row, "handler_source")));
            command.ExecuteNonQuery();
            count++;
        }

        return count;
    }

    private static int InsertEventOutcomes(NpgsqlConnection connection, NpgsqlTransaction transaction, string csvPath)
    {
        List<Dictionary<string, string>> rows = CsvReader.ReadRows(csvPath);
        int count = 0;
        for (int i = 0; i < rows.Count; i++)
        {
            Dictionary<string, string> row = rows[i];
            using NpgsqlCommand command = new NpgsqlCommand(@"
INSERT INTO event_outcomes (event_id, outcome_id, title, description, outcome_kind, outcome_source)
VALUES (@event_id, @outcome_id, @title, @description, @outcome_kind, @outcome_source);", connection, transaction);

            command.Parameters.AddWithValue("event_id", Read(row, "event_id"));
            command.Parameters.AddWithValue("outcome_id", Read(row, "outcome_id"));
            command.Parameters.AddWithValue("title", DbValue(Read(row, "title")));
            command.Parameters.AddWithValue("description", DbValue(Read(row, "description")));
            command.Parameters.AddWithValue("outcome_kind", DbValue(Read(row, "outcome_kind")));
            command.Parameters.AddWithValue("outcome_source", DbValue(Read(row, "outcome_source")));
            command.ExecuteNonQuery();
            count++;
        }

        return count;
    }

    private static int InsertTaxonomy(NpgsqlConnection connection, NpgsqlTransaction transaction, string csvPath)
    {
        List<Dictionary<string, string>> rows = CsvReader.ReadRows(csvPath);
        int count = 0;
        for (int i = 0; i < rows.Count; i++)
        {
            Dictionary<string, string> row = rows[i];
            using NpgsqlCommand command = new NpgsqlCommand(@"
INSERT INTO effect_taxonomy (tag, category, description)
VALUES (@tag, @category, @description);", connection, transaction);

            command.Parameters.AddWithValue("tag", Read(row, "tag"));
            command.Parameters.AddWithValue("category", DbValue(Read(row, "category")));
            command.Parameters.AddWithValue("description", DbValue(Read(row, "description")));
            command.ExecuteNonQuery();
            count++;
        }

        return count;
    }

    private static int InsertRatings(NpgsqlConnection connection, NpgsqlTransaction transaction, string csvPath)
    {
        List<Dictionary<string, string>> rows = CsvReader.ReadRows(csvPath);
        int count = 0;
        for (int i = 0; i < rows.Count; i++)
        {
            Dictionary<string, string> row = rows[i];
            using NpgsqlCommand command = new NpgsqlCommand(@"
INSERT INTO entity_strength_ratings (
    entity_type, entity_id, card_variant_id, synergy_rating, flexibility_rating,
    anti_synergy_rating, rationale
) VALUES (
    @entity_type, @entity_id, @card_variant_id, @synergy_rating, @flexibility_rating,
    @anti_synergy_rating, @rationale
);", connection, transaction);

            command.Parameters.AddWithValue("entity_type", Read(row, "entity_type"));
            command.Parameters.AddWithValue("entity_id", Read(row, "entity_id"));
            command.Parameters.AddWithValue("card_variant_id", DbInt(Read(row, "card_variant_id")));
            command.Parameters.AddWithValue("synergy_rating", ParseInt(Read(row, "synergy_rating"), 1));
            command.Parameters.AddWithValue("flexibility_rating", ParseInt(Read(row, "flexibility_rating"), 1));
            command.Parameters.AddWithValue("anti_synergy_rating", ParseInt(Read(row, "anti_synergy_rating"), 1));
            command.Parameters.AddWithValue("rationale", DbValue(Read(row, "rationale")));
            command.ExecuteNonQuery();
            count++;
        }

        return count;
    }

    private static int InsertArchetypes(NpgsqlConnection connection, NpgsqlTransaction transaction, string csvPath)
    {
        List<Dictionary<string, string>> rows = CsvReader.ReadRows(csvPath);
        int count = 0;
        for (int i = 0; i < rows.Count; i++)
        {
            Dictionary<string, string> row = rows[i];
            using NpgsqlCommand command = new NpgsqlCommand(@"
INSERT INTO archetypes (character_id, name, description, key_effect_tags)
VALUES (@character_id, @name, @description, @key_effect_tags);", connection, transaction);

            command.Parameters.AddWithValue("character_id", DbValue(Read(row, "character_id")));
            command.Parameters.AddWithValue("name", Read(row, "name"));
            command.Parameters.AddWithValue("description", DbValue(Read(row, "description")));
            command.Parameters.Add("key_effect_tags", NpgsqlDbType.Array | NpgsqlDbType.Text).Value = ParseArray(Read(row, "key_effect_tags"));
            command.ExecuteNonQuery();
            count++;
        }

        return count;
    }

    private static int InsertSynergyClusters(NpgsqlConnection connection, NpgsqlTransaction transaction, string csvPath)
    {
        List<Dictionary<string, string>> rows = CsvReader.ReadRows(csvPath);
        int count = 0;
        for (int i = 0; i < rows.Count; i++)
        {
            Dictionary<string, string> row = rows[i];
            using NpgsqlCommand command = new NpgsqlCommand(@"
INSERT INTO synergy_clusters (
    archetype_id, entity_type, entity_id, card_variant_id, synergy_role, synergy_score, explanation
) VALUES (
    (
        SELECT a.id
        FROM archetypes a
        WHERE COALESCE(a.character_id, '') = COALESCE(@archetype_character_id, '')
          AND a.name = @archetype_name
        ORDER BY a.id
        LIMIT 1
    ),
    @entity_type, @entity_id, @card_variant_id, @synergy_role, @synergy_score, @explanation
);", connection, transaction);

            command.Parameters.AddWithValue("archetype_character_id", DbValue(Read(row, "archetype_character_id")));
            command.Parameters.AddWithValue("archetype_name", Read(row, "archetype_name"));
            command.Parameters.AddWithValue("entity_type", Read(row, "entity_type"));
            command.Parameters.AddWithValue("entity_id", Read(row, "entity_id"));
            command.Parameters.AddWithValue("card_variant_id", DBNull.Value);
            command.Parameters.AddWithValue("synergy_role", DbValue(Read(row, "synergy_role")));
            command.Parameters.AddWithValue("synergy_score", DbDouble(Read(row, "synergy_score")));
            command.Parameters.AddWithValue("explanation", DbValue(Read(row, "explanation")));
            command.ExecuteNonQuery();
            count++;
        }

        return count;
    }

    private static bool HasBaseData(string connectionString)
    {
        return TableHasRows(connectionString, "characters")
            && TableHasRows(connectionString, "cards")
            && TableHasRows(connectionString, "relics")
            && TableHasRows(connectionString, "potions")
            && TableHasRows(connectionString, "events")
            && TableHasRows(connectionString, "powers");
    }

    private static bool TableHasRows(string connectionString, string tableName)
    {
        return CountRows(connectionString, tableName) > 0;
    }

    private static int CountRows(string connectionString, string tableName)
    {
        using NpgsqlConnection connection = new NpgsqlConnection(connectionString);
        connection.Open();
        string sql = "SELECT COUNT(*) FROM " + tableName + ";";
        using NpgsqlCommand command = new NpgsqlCommand(sql, connection);
        object result = command.ExecuteScalar();
        if (result is long longCount)
        {
            return (int)longCount;
        }

        if (result is int intCount)
        {
            return intCount;
        }

        return 0;
    }

    private static void ExecuteNonQuery(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql)
    {
        using NpgsqlCommand command = new NpgsqlCommand(sql, connection, transaction);
        command.ExecuteNonQuery();
    }

    private static string Read(IReadOnlyDictionary<string, string> row, string key)
    {
        if (row.TryGetValue(key, out string value))
        {
            return value;
        }

        return string.Empty;
    }

    private static object DbValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return DBNull.Value;
        }

        return value;
    }

    /// <summary>
    /// Maps a character_id string to the appropriate DB value.
    /// Empty string (full-scan runs) and "colorless" both map to NULL
    /// because colorless cards are universally draftable.
    /// </summary>
    private static object DbCharacterId(string value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || string.Equals(value, "colorless", StringComparison.OrdinalIgnoreCase))
        {
            return DBNull.Value;
        }

        return value;
    }

    private static object DbInt(string value)
    {
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
        {
            return DBNull.Value;
        }

        return parsed;
    }

    private static object DbDouble(string value)
    {
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
        {
            return DBNull.Value;
        }

        return parsed;
    }

    private static int ParseInt(string value, int fallback)
    {
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
        {
            return parsed;
        }

        return fallback;
    }

    private static bool ParseBool(string value)
    {
        return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "1", StringComparison.OrdinalIgnoreCase);
    }

    private static string[] ParseArray(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Array.Empty<string>();
        }

        string[] split = value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return split;
    }
}
