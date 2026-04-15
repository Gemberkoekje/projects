using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Sts2Extractor.Cli;
using Sts2Extractor.Infrastructure;
using Sts2Extractor.Localization;

namespace Sts2Extractor.Extractors;

internal sealed class EventExtractionRunner
{
    private readonly EventExtractor _extractor;

    public EventExtractionRunner()
    {
        _extractor = new EventExtractor();
    }

    public EventExtractionResult Run(CliOptions options)
    {
        string eventsDirectory = Path.Combine(options.RootPath, "MegaCrit.Sts2.Core.Models.Events");
        if (!Directory.Exists(eventsDirectory))
        {
            throw new DirectoryNotFoundException($"Missing expected directory: {eventsDirectory}");
        }

        List<EventExtractionRecord> records = Directory
            .EnumerateFiles(eventsDirectory, "*.cs", SearchOption.TopDirectoryOnly)
            .OrderBy(static file => file, StringComparer.OrdinalIgnoreCase)
            .Select(file => _extractor.Extract(options.RootPath, file))
            .ToList();

        CsvWriter.Write(options.OutputPath, new[]
        {
            "file",
            "event_id",
            "id_resolution_status",
            "roslyn_fallback_used",
            "is_shared",
            "option_count",
            "outcome_count",
            "confidence_score",
            "initial_options_source",
            "resume_source"
        }, records.Select(static record => (IReadOnlyList<string>)new[]
        {
            record.FilePath,
            record.EventId,
            record.IdResolutionStatus,
            record.RoslynFallbackUsed ? "true" : "false",
            record.IsShared ? "true" : "false",
            record.Options.Count.ToString(CultureInfo.InvariantCulture),
            record.Outcomes.Count.ToString(CultureInfo.InvariantCulture),
            record.ConfidenceScore.ToString("0.00", CultureInfo.InvariantCulture),
            Compact(record.InitialOptionsSource),
            Compact(record.ResumeSource)
        }));

        string outputDirectory = Path.GetDirectoryName(options.OutputPath) ?? options.RootPath;
        LocStringReader locReader = LocStringReader.Create(options);

        for (int i = 0; i < records.Count; i++)
        {
            EventExtractionRecord record = records[i];
            for (int j = 0; j < record.Options.Count; j++)
            {
                EventOptionRecord option = record.Options[j];
                option.Title = locReader.GetEventOptionTitle(record.EventId, "INITIAL", option.OptionTextKey);
                option.Description = locReader.GetEventOptionDescription(record.EventId, "INITIAL", option.OptionTextKey);
            }

            for (int j = 0; j < record.Outcomes.Count; j++)
            {
                EventOutcomeRecord outcome = record.Outcomes[j];
                EventOptionRecord matchingOption = record.Options.FirstOrDefault(o => string.Equals(o.HandlerMethod, outcome.OutcomeId, StringComparison.Ordinal));
                if (matchingOption != null)
                {
                    outcome.Title = matchingOption.Title;
                    outcome.Description = matchingOption.Description;
                }
                else
                {
                    outcome.Title = locReader.GetEventTitle(record.EventId);
                    outcome.Description = locReader.GetEventDescription(record.EventId);
                }
            }
        }

        List<EventIngestionRecord> ingestionRecords = records.Select(record => new EventIngestionRecord
        {
            EventId = record.EventId,
            Title = locReader.GetEventTitle(record.EventId),
            Description = locReader.GetEventDescription(record.EventId),
            IsDeterministic = record.Options.Count > 0 && record.Outcomes.Count > 0,
            OptionsSummary = string.Join(";", record.Options.Select(o => o.OptionTextKey + "|" + o.HandlerMethod)),
            OutcomeSource = string.Join("\n\n", record.Outcomes.Select(o => o.OutcomeSource))
        }).ToList();

        string ingestionOutputPath = Path.Combine(outputDirectory, "events_ingestion.csv");
        CsvWriter.Write(ingestionOutputPath, new[]
        {
            "event_id",
            "title",
            "description",
            "is_deterministic",
            "options_summary",
            "outcome_source",
            "effect_tags",
            "effect_description"
        }, ingestionRecords.Select(static record => (IReadOnlyList<string>)new[]
        {
            record.EventId,
            record.Title,
            record.Description,
            record.IsDeterministic ? "true" : "false",
            record.OptionsSummary,
            record.OutcomeSource,
            string.Join(";", record.EffectTags),
            record.EffectDescription
        }));

        List<EventOptionRecord> optionRows = records.SelectMany(static record => record.Options).ToList();
        string optionsOutputPath = Path.Combine(outputDirectory, "event_options_staging.csv");
        CsvWriter.Write(optionsOutputPath, new[]
        {
            "event_id",
            "option_text_key",
            "title",
            "description",
            "handler_method",
            "handler_source"
        }, optionRows.Select(static option => (IReadOnlyList<string>)new[]
        {
            option.EventId,
            option.OptionTextKey,
            option.Title,
            option.Description,
            option.HandlerMethod,
            option.HandlerSource
        }));

        List<EventOutcomeRecord> outcomeRows = records.SelectMany(static record => record.Outcomes).ToList();
        string outcomesOutputPath = Path.Combine(outputDirectory, "event_outcomes_staging.csv");
        CsvWriter.Write(outcomesOutputPath, new[]
        {
            "event_id",
            "outcome_id",
            "title",
            "description",
            "outcome_kind",
            "outcome_source"
        }, outcomeRows.Select(static outcome => (IReadOnlyList<string>)new[]
        {
            outcome.EventId,
            outcome.OutcomeId,
            outcome.Title,
            outcome.Description,
            outcome.OutcomeKind,
            outcome.OutcomeSource
        }));

        return new EventExtractionResult
        {
            OutputPath = options.OutputPath,
            IngestionOutputPath = ingestionOutputPath,
            OptionsOutputPath = optionsOutputPath,
            OutcomesOutputPath = outcomesOutputPath,
            RecordCount = records.Count,
            OptionCount = optionRows.Count,
            OutcomeCount = outcomeRows.Count,
            LowConfidenceCount = records.Count(r => r.ConfidenceScore < options.ConfidenceThreshold)
        };
    }

    private static string Compact(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return string.Empty;
        }

        return source.Replace('\r', ' ').Replace('\n', ' ').Trim();
    }
}
