using System;
using Sts2Extractor.Annotation;
using Sts2Extractor.Audit;
using Sts2Extractor.Cli;
using Sts2Extractor.Extractors;
using Sts2Extractor.Taxonomy;
using Sts2Extractor.Utilities;

namespace Sts2Extractor;

internal static class Program
{
    private static int Main(string[] args)
    {
        CliOptions options = CliOptions.Parse(args);
        if (!options.IsValid)
        {
            Console.Error.WriteLine(options.ValidationMessage);
            Console.Error.WriteLine(CliOptions.Usage);
            return 1;
        }

        try
        {
            if (options.Command == CliCommand.Audit)
            {
                PatternAuditRunner runner = new PatternAuditRunner();
                PatternAuditResult result = runner.Run(options);
                Console.WriteLine($"Audit complete: {result.RecordCount} records written to {result.OutputPath}");
                Console.WriteLine($"Fallback required for {result.FallbackRequiredCount} records.");
                return 0;
            }

            if (options.Command == CliCommand.ExtractCards)
            {
                CardExtractionRunner runner = new CardExtractionRunner();
                CardExtractionResult result = runner.Run(options);
                Console.WriteLine($"Card extraction complete: {result.RecordCount} records");
                Console.WriteLine($"  Audit preview: {result.OutputPath}");
                Console.WriteLine($"  Ingestion (base/upgraded): {result.IngestionOutputPath}");
                Console.WriteLine($"  Variants (exceptions): {result.VariantOutputPath} ({result.VariantCount} records)");
                Console.WriteLine($"  Companion actions: {result.CompanionOutputPath} ({result.CompanionActionCount} records)");
                Console.WriteLine($"  Low-confidence cards: {result.LowConfidenceCount}");
                return 0;
            }

            if (options.Command == CliCommand.ExtractRelics)
            {
                RelicExtractionRunner runner = new RelicExtractionRunner();
                RelicExtractionResult result = runner.Run(options);
                Console.WriteLine($"Relic extraction complete: {result.RecordCount} records");
                Console.WriteLine($"  Audit preview: {result.OutputPath}");
                Console.WriteLine($"  Ingestion: {result.IngestionOutputPath}");
                Console.WriteLine($"  Hook listeners staging: {result.HooksOutputPath} ({result.HookCount} records)");
                Console.WriteLine($"  Low-confidence relics: {result.LowConfidenceCount}");
                return 0;
            }

            if (options.Command == CliCommand.ExtractPotions)
            {
                PotionExtractionRunner runner = new PotionExtractionRunner();
                PotionExtractionResult result = runner.Run(options);
                Console.WriteLine($"Potion extraction complete: {result.RecordCount} records");
                Console.WriteLine($"  Audit preview: {result.OutputPath}");
                Console.WriteLine($"  Ingestion: {result.IngestionOutputPath}");
                Console.WriteLine($"  Low-confidence potions: {result.LowConfidenceCount}");
                return 0;
            }

            if (options.Command == CliCommand.ExtractEvents)
            {
                EventExtractionRunner runner = new EventExtractionRunner();
                EventExtractionResult result = runner.Run(options);
                Console.WriteLine($"Event extraction complete: {result.RecordCount} records");
                Console.WriteLine($"  Audit preview: {result.OutputPath}");
                Console.WriteLine($"  Ingestion: {result.IngestionOutputPath}");
                Console.WriteLine($"  Options staging: {result.OptionsOutputPath} ({result.OptionCount} records)");
                Console.WriteLine($"  Outcomes staging: {result.OutcomesOutputPath} ({result.OutcomeCount} records)");
                Console.WriteLine($"  Low-confidence events: {result.LowConfidenceCount}");
                return 0;
            }

            if (options.Command == CliCommand.ExtractPowers)
            {
                PowerExtractionRunner runner = new PowerExtractionRunner();
                PowerExtractionResult result = runner.Run(options);
                Console.WriteLine($"Power extraction complete: {result.RecordCount} records");
                Console.WriteLine($"  Audit preview: {result.OutputPath}");
                Console.WriteLine($"  Ingestion: {result.IngestionOutputPath}");
                Console.WriteLine($"  Hook listeners staging: {result.HooksOutputPath} ({result.HookCount} records)");
                Console.WriteLine($"  Low-confidence powers: {result.LowConfidenceCount}");
                return 0;
            }

            if (options.Command == CliCommand.ExtractCharacters)
            {
                CharacterExtractionRunner runner = new CharacterExtractionRunner();
                CharacterExtractionResult result = runner.Run(options);
                Console.WriteLine($"Character extraction complete: {result.RecordCount} records");
                Console.WriteLine($"  Audit preview: {result.OutputPath}");
                Console.WriteLine($"  Ingestion: {result.IngestionOutputPath}");
                return 0;
            }

            if (options.Command == CliCommand.GenerateRatings)
            {
                EntityStrengthRatingsRunner runner = new EntityStrengthRatingsRunner();
                EntityStrengthRatingsResult result = runner.Run(options);
                Console.WriteLine($"Entity strength ratings complete: {result.RecordCount} records");
                Console.WriteLine($"  Output: {result.OutputPath}");
                Console.WriteLine($"  Cards: {result.CardCount}");
                Console.WriteLine($"  Relics: {result.RelicCount}");
                Console.WriteLine($"  Potions: {result.PotionCount}");
                Console.WriteLine($"  Event options: {result.EventOptionCount}");
                return 0;
            }

            if (options.Command == CliCommand.SyncPostgres)
            {
                PostgresSyncRunner runner = new PostgresSyncRunner();
                PostgresSyncResult result = runner.Run(options);
                Console.WriteLine($"PostgreSQL sync complete: {result.InsertedRowCount} rows synced");
                Console.WriteLine($"  Connection: {result.ConnectionDisplay}");
                Console.WriteLine($"  Staging directory: {result.StagingDirectory}");
                if (result.AnnotatedEntityCount > 0)
                {
                    Console.WriteLine($"  Annotated entities: {result.AnnotatedEntityCount}");
                }

                Console.WriteLine("  Regenerated synergy edges: skipped (run build-synergy-edges explicitly)");
                Console.WriteLine($"  Regenerated ratings: {result.RatingsCount}");
                Console.WriteLine($"  Regenerated archetypes/clusters: {result.ArchetypeCount}/{result.SynergyClusterCount}");
                Console.WriteLine($"  Regenerated affinities: {result.AffinityCount}");

                return 0;
            }

            if (options.Command == CliCommand.InitDatabase)
            {
                DatabaseInitializer.InitializeSchema(options);
                return 0;
            }

            if (options.Command == CliCommand.BuildSynergyEdges)
            {
                BuildSynergyEdgesRunner runner = new BuildSynergyEdgesRunner();
                BuildSynergyEdgesResult result = runner.Run(options);
                Console.WriteLine($"Synergy edge build complete: {result.EdgeCount} edges from {result.CandidatePairCount} candidate pairs ({result.BatchCount} batches)");
                Console.WriteLine($"  Output: {result.OutputPath}");
                return 0;
            }

            if (options.Command == CliCommand.BuildSynergyClusters)
            {
                BuildSynergyClustersRunner runner = new BuildSynergyClustersRunner();
                BuildSynergyClustersResult result = runner.Run(options);
                Console.WriteLine($"Synergy cluster build complete: {result.ClusterCount} rows across {result.ArchetypeCount} archetypes");
                Console.WriteLine($"  Archetypes output: {result.ArchetypesOutputPath}");
                Console.WriteLine($"  Clusters output: {result.ClustersOutputPath}");
                return 0;
            }

            if (options.Command == CliCommand.DiscoverArchetypes)
            {
                DiscoverArchetypesRunner runner = new DiscoverArchetypesRunner();
                DiscoverArchetypesResult result = runner.Run(options);
                Console.WriteLine($"Archetype discovery complete: {result.ArchetypeCount} archetypes across {result.CharacterCount} characters");
                Console.WriteLine($"  Output: {result.OutputPath}");
                return 0;
            }

            if (options.Command == CliCommand.ScoreAffinities)
            {
                ScoreAffinitiesRunner runner = new ScoreAffinitiesRunner();
                ScoreAffinitiesResult result = runner.Run(options);
                Console.WriteLine($"Affinity scoring complete: {result.AffinityCount} rows ({result.EntityCount} entities, {result.BatchCount} batches)");
                Console.WriteLine($"  Output: {result.OutputPath}");
                return 0;
            }

            if (options.Command == CliCommand.AdvisePick)
            {
                AdvisePickRunner runner = new AdvisePickRunner();
                runner.Run(options);
                return 0;
            }

            if (options.Command == CliCommand.SeedTaxonomy)
            {
                TaxonomySeedRunner runner = new TaxonomySeedRunner();
                TaxonomySeedResult result = runner.Run(options);
                Console.WriteLine($"Taxonomy seed complete: {result.RecordCount} records written to {result.OutputPath}");
                return 0;
            }

            if (options.Command == CliCommand.ListModels)
            {
                ListModelsRunner runner = new ListModelsRunner();
                int code = runner.Run(options);
                return code;
            }

            if (options.Command == CliCommand.Annotate)
            {
                AnnotationRunner runner = new AnnotationRunner();
                AnnotationResult result = runner.Run(options);
                Console.WriteLine($"Annotation complete with provider={result.Provider}, model={result.Model}, task={options.AnnotationTask}");
                Console.WriteLine(result.Content);
                return 0;
            }

            Console.Error.WriteLine("Unknown command.");
            Console.Error.WriteLine(CliOptions.Usage);
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }
}
