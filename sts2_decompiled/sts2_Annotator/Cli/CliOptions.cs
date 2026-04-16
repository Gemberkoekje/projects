using System;
using System.Globalization;
using System.IO;
using Sts2Extractor.Annotation;

namespace Sts2Extractor.Cli;

internal sealed class CliOptions
{
    public static string Usage => "Usage: Sts2Extractor audit|extract-cards|extract-relics|extract-potions|extract-events|extract-powers|extract-characters|generate-ratings|seed-taxonomy|annotate|list-models|init-database|sync-postgres|build-synergy-clusters|discover-archetypes|score-affinities|advise-pick [--root <path>] [--output <path>] [--character <id>] [--all-characters] [--cards <n>] [--relics <n>] [--powers <n>] [--threshold <0-1>] [--batch-size <n>] [--annotate] [--resume] [--resume-character <id>] [--resume-batch <n>] [--only-missing] [--provider anthropic|openai] [--task mechanical|archetype-discovery|affinity-scoring] [--prompt <text>] [--prompt-file <path>] [--system <text>] [--system-file <path>] [--model <id>] [--max-tokens <n>] [--rpm <n>] [--input-tpm <n>] [--output-tpm <n>] [--connection-string <postgres-connection-string>] [advise-pick: --archetype <id|name>] [--deck <card_id,...>] [--relics <relic_id,...>] [--options <id1,id2[,id3]>] [--explain]";

    public CliCommand Command { get; private set; }

    public string RootPath { get; private set; }

    public string OutputPath { get; private set; }

    public int CardSampleSize { get; private set; }

    public int RelicSampleSize { get; private set; }

    public int PowerSampleSize { get; private set; }

    public decimal ConfidenceThreshold { get; private set; }

    public LlmProviderKind Provider { get; private set; }

    public AnnotationTaskKind AnnotationTask { get; private set; }

    public string PromptText { get; private set; }

    public string PromptFilePath { get; private set; }

    public string SystemPromptText { get; private set; }

    public string SystemPromptFilePath { get; private set; }

    public string ModelOverride { get; private set; }

    public int MaxTokens { get; private set; }

    public int RequestsPerMinute { get; private set; }

    public int InputTokensPerMinute { get; private set; }

    public int OutputTokensPerMinute { get; private set; }

    public string ConnectionString { get; private set; }

    public string CharacterId { get; private set; }

    public int BatchSize { get; private set; }

    public string ArchetypeRef { get; private set; }

    public string[] DeckIds { get; private set; }

    public string[] RelicIds { get; private set; }

    public string[] OptionIds { get; private set; }

    public bool ExplainPick { get; private set; }

    public bool Resume { get; private set; }

    public bool OnlyMissingAffinities { get; private set; }

    public bool IsValid { get; private set; }

    public string ValidationMessage { get; private set; }

    public bool AllCharacters { get; private set; }

    public bool Annotate { get; private set; }

    public string ResumeCharacterId { get; private set; }

    public int ResumeBatchNumber { get; private set; }

    private CliOptions()
    {
        Command = CliCommand.None;
        RootPath = Directory.GetCurrentDirectory();
        OutputPath = string.Empty;
        CardSampleSize = 20;
        RelicSampleSize = 10;
        PowerSampleSize = 10;
        ConfidenceThreshold = 0.85m;
        Provider = LlmProviderKind.Anthropic;
        AnnotationTask = AnnotationTaskKind.Mechanical;
        PromptText = string.Empty;
        PromptFilePath = string.Empty;
        SystemPromptText = string.Empty;
        SystemPromptFilePath = string.Empty;
        ModelOverride = string.Empty;
        MaxTokens = 1200;
        RequestsPerMinute = 0;
        InputTokensPerMinute = 0;
        OutputTokensPerMinute = 0;
        ConnectionString = string.Empty;
        CharacterId = string.Empty;
        BatchSize = 50;
        ArchetypeRef = string.Empty;
        DeckIds = Array.Empty<string>();
        RelicIds = Array.Empty<string>();
        OptionIds = Array.Empty<string>();
        ExplainPick = false;
        Resume = false;
        OnlyMissingAffinities = false;
        IsValid = true;
        ValidationMessage = string.Empty;
        AllCharacters = false;
        Annotate = false;
        ResumeCharacterId = string.Empty;
        ResumeBatchNumber = 0;
    }

    public CliOptions ForCommand(CliCommand command, string outputPath)
    {
        return new CliOptions
        {
            Command = command,
            RootPath = RootPath,
            OutputPath = outputPath,
            CardSampleSize = CardSampleSize,
            RelicSampleSize = RelicSampleSize,
            PowerSampleSize = PowerSampleSize,
            ConfidenceThreshold = ConfidenceThreshold,
            Provider = Provider,
            AnnotationTask = AnnotationTask,
            PromptText = PromptText,
            PromptFilePath = PromptFilePath,
            SystemPromptText = SystemPromptText,
            SystemPromptFilePath = SystemPromptFilePath,
            ModelOverride = ModelOverride,
            MaxTokens = MaxTokens,
            RequestsPerMinute = RequestsPerMinute,
            InputTokensPerMinute = InputTokensPerMinute,
            OutputTokensPerMinute = OutputTokensPerMinute,
            ConnectionString = ConnectionString,
            CharacterId = CharacterId,
            BatchSize = BatchSize,
            ArchetypeRef = ArchetypeRef,
            DeckIds = DeckIds,
            RelicIds = RelicIds,
            OptionIds = OptionIds,
            ExplainPick = ExplainPick,
            Resume = Resume,
            OnlyMissingAffinities = OnlyMissingAffinities,
            IsValid = true,
            ValidationMessage = string.Empty,
            AllCharacters = AllCharacters,
            Annotate = Annotate,
            ResumeCharacterId = ResumeCharacterId,
            ResumeBatchNumber = ResumeBatchNumber
        };
    }

    public CliOptions WithCharacter(string characterId)
    {
        return new CliOptions
        {
            Command = Command,
            RootPath = RootPath,
            OutputPath = OutputPath,
            CardSampleSize = CardSampleSize,
            RelicSampleSize = RelicSampleSize,
            PowerSampleSize = PowerSampleSize,
            ConfidenceThreshold = ConfidenceThreshold,
            Provider = Provider,
            AnnotationTask = AnnotationTask,
            PromptText = PromptText,
            PromptFilePath = PromptFilePath,
            SystemPromptText = SystemPromptText,
            SystemPromptFilePath = SystemPromptFilePath,
            ModelOverride = ModelOverride,
            MaxTokens = MaxTokens,
            RequestsPerMinute = RequestsPerMinute,
            InputTokensPerMinute = InputTokensPerMinute,
            OutputTokensPerMinute = OutputTokensPerMinute,
            ConnectionString = ConnectionString,
            CharacterId = characterId.ToLowerInvariant(),
            BatchSize = BatchSize,
            ArchetypeRef = ArchetypeRef,
            DeckIds = DeckIds,
            RelicIds = RelicIds,
            OptionIds = OptionIds,
            ExplainPick = ExplainPick,
            Resume = Resume,
            OnlyMissingAffinities = OnlyMissingAffinities,
            IsValid = IsValid,
            ValidationMessage = ValidationMessage,
            AllCharacters = AllCharacters,
            Annotate = Annotate,
            ResumeCharacterId = ResumeCharacterId,
            ResumeBatchNumber = ResumeBatchNumber
        };
    }

    public CliOptions WithOnlyMissingAffinities()
    {
        return new CliOptions
        {
            Command = Command,
            RootPath = RootPath,
            OutputPath = OutputPath,
            CardSampleSize = CardSampleSize,
            RelicSampleSize = RelicSampleSize,
            PowerSampleSize = PowerSampleSize,
            ConfidenceThreshold = ConfidenceThreshold,
            Provider = Provider,
            AnnotationTask = AnnotationTask,
            PromptText = PromptText,
            PromptFilePath = PromptFilePath,
            SystemPromptText = SystemPromptText,
            SystemPromptFilePath = SystemPromptFilePath,
            ModelOverride = ModelOverride,
            MaxTokens = MaxTokens,
            RequestsPerMinute = RequestsPerMinute,
            InputTokensPerMinute = InputTokensPerMinute,
            OutputTokensPerMinute = OutputTokensPerMinute,
            ConnectionString = ConnectionString,
            CharacterId = CharacterId,
            BatchSize = BatchSize,
            ArchetypeRef = ArchetypeRef,
            DeckIds = DeckIds,
            RelicIds = RelicIds,
            OptionIds = OptionIds,
            ExplainPick = ExplainPick,
            Resume = Resume,
            OnlyMissingAffinities = true,
            IsValid = IsValid,
            ValidationMessage = ValidationMessage,
            AllCharacters = AllCharacters,
            Annotate = Annotate,
            ResumeCharacterId = ResumeCharacterId,
            ResumeBatchNumber = ResumeBatchNumber
        };
    }

    public static CliOptions Parse(string[] args)
    {
        CliOptions options = new CliOptions();
        if (args.Length == 0)
        {
            options.IsValid = false;
            options.ValidationMessage = "A command is required.";
            return options;
        }

        string command = args[0].Trim();
        if (string.Equals(command, "audit", StringComparison.OrdinalIgnoreCase))
        {
            options.Command = CliCommand.Audit;
            options.OutputPath = Path.Combine(options.RootPath, "pattern_audit.csv");
        }
        else if (string.Equals(command, "extract-cards", StringComparison.OrdinalIgnoreCase))
        {
            options.Command = CliCommand.ExtractCards;
            options.OutputPath = Path.Combine(options.RootPath, "card_extract_preview.csv");
        }
        else if (string.Equals(command, "extract-relics", StringComparison.OrdinalIgnoreCase))
        {
            options.Command = CliCommand.ExtractRelics;
            options.OutputPath = Path.Combine(options.RootPath, "relic_extract_preview.csv");
        }
        else if (string.Equals(command, "extract-potions", StringComparison.OrdinalIgnoreCase))
        {
            options.Command = CliCommand.ExtractPotions;
            options.OutputPath = Path.Combine(options.RootPath, "potion_extract_preview.csv");
        }
        else if (string.Equals(command, "extract-events", StringComparison.OrdinalIgnoreCase))
        {
            options.Command = CliCommand.ExtractEvents;
            options.OutputPath = Path.Combine(options.RootPath, "event_extract_preview.csv");
        }
        else if (string.Equals(command, "extract-powers", StringComparison.OrdinalIgnoreCase))
        {
            options.Command = CliCommand.ExtractPowers;
            options.OutputPath = Path.Combine(options.RootPath, "power_extract_preview.csv");
        }
        else if (string.Equals(command, "extract-characters", StringComparison.OrdinalIgnoreCase))
        {
            options.Command = CliCommand.ExtractCharacters;
            options.OutputPath = Path.Combine(options.RootPath, "character_extract_preview.csv");
        }
        else if (string.Equals(command, "generate-ratings", StringComparison.OrdinalIgnoreCase))
        {
            options.Command = CliCommand.GenerateRatings;
            options.OutputPath = Path.Combine(options.RootPath, "entity_strength_ratings_staging.csv");
        }
        else if (string.Equals(command, "seed-taxonomy", StringComparison.OrdinalIgnoreCase))
        {
            options.Command = CliCommand.SeedTaxonomy;
            options.OutputPath = Path.Combine(options.RootPath, "effect_taxonomy_seed.csv");
        }
        else if (string.Equals(command, "annotate", StringComparison.OrdinalIgnoreCase))
        {
            options.Command = CliCommand.Annotate;
        }
        else if (string.Equals(command, "list-models", StringComparison.OrdinalIgnoreCase))
        {
            options.Command = CliCommand.ListModels;
        }
        else if (string.Equals(command, "sync-postgres", StringComparison.OrdinalIgnoreCase))
        {
            options.Command = CliCommand.SyncPostgres;
            options.OutputPath = Path.Combine(options.RootPath, "output", "postgres_sync");
        }
        else if (string.Equals(command, "init-database", StringComparison.OrdinalIgnoreCase))
        {
            options.Command = CliCommand.InitDatabase;
        }
        else if (string.Equals(command, "build-synergy-clusters", StringComparison.OrdinalIgnoreCase))
        {
            options.Command = CliCommand.BuildSynergyClusters;
            options.OutputPath = Path.Combine(options.RootPath, "synergy_clusters_staging.csv");
        }
        else if (string.Equals(command, "discover-archetypes", StringComparison.OrdinalIgnoreCase))
        {
            options.Command = CliCommand.DiscoverArchetypes;
            options.OutputPath = Path.Combine(options.RootPath, "archetypes_discovery_staging.csv");
        }
        else if (string.Equals(command, "score-affinities", StringComparison.OrdinalIgnoreCase))
        {
            options.Command = CliCommand.ScoreAffinities;
            options.OutputPath = Path.Combine(options.RootPath, "entity_archetype_affinity_staging.csv");
        }
        else if (string.Equals(command, "advise-pick", StringComparison.OrdinalIgnoreCase))
        {
            options.Command = CliCommand.AdvisePick;
        }
        else
        {
            options.IsValid = false;
            options.ValidationMessage = $"Unsupported command: {command}";
            return options;
        }

        for (int i = 1; i < args.Length; i++)
        {
            string arg = args[i];
            if (string.Equals(arg, "--root", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryReadValue(args, ref i, out string value, options, "--root")) return options;
                options.RootPath = value;
                continue;
            }

            if (string.Equals(arg, "--output", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryReadValue(args, ref i, out string value, options, "--output")) return options;
                options.OutputPath = value;
                continue;
            }

            if (string.Equals(arg, "--all-characters", StringComparison.OrdinalIgnoreCase))
            {
                options.AllCharacters = true;
                continue;
            }

            if (string.Equals(arg, "--annotate", StringComparison.OrdinalIgnoreCase))
            {
                options.Annotate = true;
                continue;
            }

            if (string.Equals(arg, "--resume", StringComparison.OrdinalIgnoreCase))
            {
                options.Resume = true;
                continue;
            }

            if (string.Equals(arg, "--only-missing", StringComparison.OrdinalIgnoreCase))
            {
                options.OnlyMissingAffinities = true;
                continue;
            }

            if (string.Equals(arg, "--cards", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryReadValue(args, ref i, out string value, options, "--cards")) return options;
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) || parsed <= 0)
                {
                    options.IsValid = false;
                    options.ValidationMessage = "--cards must be a positive integer.";
                    return options;
                }

                options.CardSampleSize = parsed;
                continue;
            }

            if (string.Equals(arg, "--relics", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryReadValue(args, ref i, out string value, options, "--relics")) return options;
                if (options.Command == CliCommand.AdvisePick)
                {
                    options.RelicIds = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                }
                else
                {
                    if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) || parsed <= 0)
                    {
                        options.IsValid = false;
                        options.ValidationMessage = "--relics must be a positive integer.";
                        return options;
                    }

                    options.RelicSampleSize = parsed;
                }

                continue;
            }

            if (string.Equals(arg, "--powers", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryReadValue(args, ref i, out string value, options, "--powers")) return options;
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) || parsed <= 0)
                {
                    options.IsValid = false;
                    options.ValidationMessage = "--powers must be a positive integer.";
                    return options;
                }

                options.PowerSampleSize = parsed;
                continue;
            }

            if (string.Equals(arg, "--threshold", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryReadValue(args, ref i, out string value, options, "--threshold")) return options;
                if (!decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal parsed) || parsed < 0m || parsed > 1m)
                {
                    options.IsValid = false;
                    options.ValidationMessage = "--threshold must be between 0 and 1.";
                    return options;
                }

                options.ConfidenceThreshold = parsed;
                continue;
            }

            if (string.Equals(arg, "--provider", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryReadValue(args, ref i, out string value, options, "--provider")) return options;
                if (string.Equals(value, "anthropic", StringComparison.OrdinalIgnoreCase))
                {
                    options.Provider = LlmProviderKind.Anthropic;
                }
                else if (string.Equals(value, "openai", StringComparison.OrdinalIgnoreCase))
                {
                    options.Provider = LlmProviderKind.OpenAI;
                }
                else
                {
                    options.IsValid = false;
                    options.ValidationMessage = "--provider must be anthropic or openai.";
                    return options;
                }

                continue;
            }

            if (string.Equals(arg, "--task", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryReadValue(args, ref i, out string value, options, "--task")) return options;
                if (string.Equals(value, "mechanical", StringComparison.OrdinalIgnoreCase))
                {
                    options.AnnotationTask = AnnotationTaskKind.Mechanical;
                }
                else if (string.Equals(value, "archetype-discovery", StringComparison.OrdinalIgnoreCase))
                {
                    options.AnnotationTask = AnnotationTaskKind.ArchetypeDiscovery;
                }
                else if (string.Equals(value, "affinity-scoring", StringComparison.OrdinalIgnoreCase))
                {
                    options.AnnotationTask = AnnotationTaskKind.AffinityScoring;
                }
                else
                {
                    options.IsValid = false;
                    options.ValidationMessage = "--task must be mechanical, archetype-discovery, or affinity-scoring.";
                    return options;
                }

                continue;
            }

            if (string.Equals(arg, "--prompt", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryReadValue(args, ref i, out string value, options, "--prompt")) return options;
                options.PromptText = value;
                continue;
            }

            if (string.Equals(arg, "--prompt-file", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryReadValue(args, ref i, out string value, options, "--prompt-file")) return options;
                options.PromptFilePath = value;
                continue;
            }

            if (string.Equals(arg, "--system", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryReadValue(args, ref i, out string value, options, "--system")) return options;
                options.SystemPromptText = value;
                continue;
            }

            if (string.Equals(arg, "--system-file", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryReadValue(args, ref i, out string value, options, "--system-file")) return options;
                options.SystemPromptFilePath = value;
                continue;
            }

            if (string.Equals(arg, "--model", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryReadValue(args, ref i, out string value, options, "--model")) return options;
                options.ModelOverride = value;
                continue;
            }

            if (string.Equals(arg, "--max-tokens", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryReadValue(args, ref i, out string value, options, "--max-tokens")) return options;
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) || parsed <= 0)
                {
                    options.IsValid = false;
                    options.ValidationMessage = "--max-tokens must be a positive integer.";
                    return options;
                }

                options.MaxTokens = parsed;
                continue;
            }

            if (string.Equals(arg, "--rpm", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryReadValue(args, ref i, out string value, options, "--rpm")) return options;
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) || parsed <= 0)
                {
                    options.IsValid = false;
                    options.ValidationMessage = "--rpm must be a positive integer.";
                    return options;
                }

                options.RequestsPerMinute = parsed;
                continue;
            }

            if (string.Equals(arg, "--input-tpm", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryReadValue(args, ref i, out string value, options, "--input-tpm")) return options;
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) || parsed <= 0)
                {
                    options.IsValid = false;
                    options.ValidationMessage = "--input-tpm must be a positive integer.";
                    return options;
                }

                options.InputTokensPerMinute = parsed;
                continue;
            }

            if (string.Equals(arg, "--output-tpm", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryReadValue(args, ref i, out string value, options, "--output-tpm")) return options;
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) || parsed <= 0)
                {
                    options.IsValid = false;
                    options.ValidationMessage = "--output-tpm must be a positive integer.";
                    return options;
                }

                options.OutputTokensPerMinute = parsed;
                continue;
            }

            if (string.Equals(arg, "--connection-string", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryReadValue(args, ref i, out string value, options, "--connection-string")) return options;
                options.ConnectionString = value;
                continue;
            }

            if (string.Equals(arg, "--character", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryReadValue(args, ref i, out string value, options, "--character")) return options;
                options.CharacterId = value.ToLowerInvariant();
                continue;
            }

            if (string.Equals(arg, "--batch-size", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryReadValue(args, ref i, out string value, options, "--batch-size")) return options;
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) || parsed <= 0)
                {
                    options.IsValid = false;
                    options.ValidationMessage = "--batch-size must be a positive integer.";
                    return options;
                }

                options.BatchSize = parsed;
                continue;
            }

            if (string.Equals(arg, "--archetype", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryReadValue(args, ref i, out string value, options, "--archetype")) return options;
                options.ArchetypeRef = value;
                continue;
            }

            if (string.Equals(arg, "--deck", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryReadValue(args, ref i, out string value, options, "--deck")) return options;
                options.DeckIds = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                continue;
            }

            if (string.Equals(arg, "--options", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryReadValue(args, ref i, out string value, options, "--options")) return options;
                options.OptionIds = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                continue;
            }

            if (string.Equals(arg, "--explain", StringComparison.OrdinalIgnoreCase))
            {
                options.ExplainPick = true;
                continue;
            }

            if (string.Equals(arg, "--resume-character", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryReadValue(args, ref i, out string value, options, "--resume-character")) return options;
                options.ResumeCharacterId = value.ToLowerInvariant();
                continue;
            }

            if (string.Equals(arg, "--resume-batch", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryReadValue(args, ref i, out string value, options, "--resume-batch")) return options;
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) || parsed <= 0)
                {
                    options.IsValid = false;
                    options.ValidationMessage = "--resume-batch must be a positive integer.";
                    return options;
                }

                options.ResumeBatchNumber = parsed;
                continue;
            }

            options.IsValid = false;
            options.ValidationMessage = $"Unknown argument: {arg}";
            return options;
        }

        if (string.IsNullOrWhiteSpace(options.OutputPath))
        {
            if (options.Command == CliCommand.Audit)
            {
                options.OutputPath = Path.Combine(options.RootPath, "pattern_audit.csv");
            }
            else if (options.Command == CliCommand.ExtractCards)
            {
                options.OutputPath = Path.Combine(options.RootPath, "card_extract_preview.csv");
            }
            else if (options.Command == CliCommand.ExtractRelics)
            {
                options.OutputPath = Path.Combine(options.RootPath, "relic_extract_preview.csv");
            }
            else if (options.Command == CliCommand.ExtractPotions)
            {
                options.OutputPath = Path.Combine(options.RootPath, "potion_extract_preview.csv");
            }
            else if (options.Command == CliCommand.ExtractEvents)
            {
                options.OutputPath = Path.Combine(options.RootPath, "event_extract_preview.csv");
            }
            else if (options.Command == CliCommand.ExtractPowers)
            {
                options.OutputPath = Path.Combine(options.RootPath, "power_extract_preview.csv");
            }
            else if (options.Command == CliCommand.ExtractCharacters)
            {
                options.OutputPath = Path.Combine(options.RootPath, "character_extract_preview.csv");
            }
            else if (options.Command == CliCommand.GenerateRatings)
            {
                options.OutputPath = Path.Combine(options.RootPath, "entity_strength_ratings_staging.csv");
            }
            else if (options.Command == CliCommand.SeedTaxonomy)
            {
                options.OutputPath = Path.Combine(options.RootPath, "effect_taxonomy_seed.csv");
            }
            else if (options.Command == CliCommand.ListModels)
            {
                options.OutputPath = Path.Combine(options.RootPath, "anthropic_models.json");
            }
            else if (options.Command == CliCommand.SyncPostgres)
            {
                options.OutputPath = Path.Combine(options.RootPath, "output", "postgres_sync");
            }
            else if (options.Command == CliCommand.BuildSynergyClusters)
            {
                options.OutputPath = Path.Combine(options.RootPath, "synergy_clusters_staging.csv");
            }
            else if (options.Command == CliCommand.DiscoverArchetypes)
            {
                options.OutputPath = Path.Combine(options.RootPath, "archetypes_discovery_staging.csv");
            }
            else if (options.Command == CliCommand.ScoreAffinities)
            {
                options.OutputPath = Path.Combine(options.RootPath, "entity_archetype_affinity_staging.csv");
            }
        }

        if (options.Command == CliCommand.Annotate)
        {
            bool hasPrompt = !string.IsNullOrWhiteSpace(options.PromptText) || !string.IsNullOrWhiteSpace(options.PromptFilePath);
            if (!hasPrompt)
            {
                options.IsValid = false;
                options.ValidationMessage = "annotate requires --prompt or --prompt-file.";
                return options;
            }
        }

        if (options.Command == CliCommand.AdvisePick)
        {
            if (options.OptionIds.Length == 0)
            {
                options.IsValid = false;
                options.ValidationMessage = "advise-pick requires --options <id1,id2,...>.";
                return options;
            }
        }

        return options;
    }

    private static bool TryReadValue(string[] args, ref int index, out string value, CliOptions options, string key)
    {
        if (index + 1 >= args.Length)
        {
            options.IsValid = false;
            options.ValidationMessage = $"Missing value for {key}.";
            value = string.Empty;
            return false;
        }

        index++;
        value = args[index];
        return true;
    }
}
