using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Sts2Extractor.Parsing;

namespace Sts2Extractor.Extractors;

internal sealed class EventExtractor
{
    private static readonly Regex OptionRegex = new Regex("new\\s+EventOption\\(\\s*this\\s*,\\s*(?<handler>[A-Za-z_][A-Za-z0-9_]*)\\s*,\\s*\"(?<key>[^\"]+)\"", RegexOptions.Compiled | RegexOptions.Singleline);

    public EventExtractionRecord Extract(string rootPath, string filePath)
    {
        string source = System.IO.File.ReadAllText(filePath);
        EventExtractionRecord record = new EventExtractionRecord
        {
            FilePath = System.IO.Path.GetRelativePath(rootPath, filePath)
        };

        int confidenceParts = 0;
        bool roslynFallbackUsed = false;

        bool hasClassInfo = SourcePatterns.TryGetClassInfo(source, out string className, out string baseType, out bool classFallbackUsed);
        roslynFallbackUsed |= classFallbackUsed;
        if (hasClassInfo && SourcePatterns.IsTypeName(baseType, "EventModel"))
        {
            record.EventId = className;
            record.IdResolutionStatus = "class_name";
            confidenceParts++;
        }

        if (record.EventId.Length == 0)
        {
            record.EventId = System.IO.Path.GetFileNameWithoutExtension(filePath);
            record.IdResolutionStatus = "filename_fallback";
        }

        record.IsShared = source.Contains("IsShared => true", StringComparison.Ordinal);

        record.InitialOptionsSource = SourcePatterns.ExtractMethodBlock(source, "GenerateInitialOptions", out bool optionsFallbackUsed);
        roslynFallbackUsed |= optionsFallbackUsed;

        record.ResumeSource = SourcePatterns.ExtractMethodBlock(source, "Resume", out bool resumeFallbackUsed);
        roslynFallbackUsed |= resumeFallbackUsed;

        List<EventOptionRecord> options = ParseOptions(record.EventId, record.InitialOptionsSource, source, ref roslynFallbackUsed);
        record.Options = options;

        List<EventOutcomeRecord> outcomes = BuildOutcomes(record.EventId, options, source, record.ResumeSource);
        record.Outcomes = outcomes;

        if (record.InitialOptionsSource.Length > 0)
        {
            confidenceParts++;
        }

        if (options.Count > 0)
        {
            confidenceParts++;
        }

        if (record.ResumeSource.Length > 0 || outcomes.Count > 0)
        {
            confidenceParts++;
        }

        record.RoslynFallbackUsed = roslynFallbackUsed;
        record.ConfidenceScore = confidenceParts / 4m;
        return record;
    }

    private static List<EventOptionRecord> ParseOptions(string eventId, string initialOptionsSource, string fullSource, ref bool roslynFallbackUsed)
    {
        List<EventOptionRecord> options = new List<EventOptionRecord>();
        if (initialOptionsSource.Length == 0)
        {
            return options;
        }

        MatchCollection matches = OptionRegex.Matches(initialOptionsSource);
        foreach (Match match in matches)
        {
            string handler = match.Groups["handler"].Value.Trim();
            string key = match.Groups["key"].Value.Trim();
            if (handler.Length == 0)
            {
                continue;
            }

            string handlerSource = SourcePatterns.ExtractMethodBlock(fullSource, handler, out bool handlerFallbackUsed);
            roslynFallbackUsed |= handlerFallbackUsed;

            options.Add(new EventOptionRecord
            {
                EventId = eventId,
                OptionTextKey = key,
                HandlerMethod = handler,
                HandlerSource = handlerSource
            });
        }

        return options;
    }

    private static List<EventOutcomeRecord> BuildOutcomes(string eventId, IReadOnlyList<EventOptionRecord> options, string source, string resumeSource)
    {
        List<EventOutcomeRecord> outcomes = new List<EventOutcomeRecord>();

        if (resumeSource.Length > 0)
        {
            outcomes.Add(new EventOutcomeRecord
            {
                EventId = eventId,
                OutcomeId = "resume",
                OutcomeKind = "resume",
                OutcomeSource = resumeSource
            });
        }

        foreach (EventOptionRecord option in options)
        {
            if (!LooksLikeOutcome(option.HandlerSource))
            {
                continue;
            }

            outcomes.Add(new EventOutcomeRecord
            {
                EventId = eventId,
                OutcomeId = option.HandlerMethod,
                OutcomeKind = "option_handler",
                OutcomeSource = option.HandlerSource
            });
        }

        if (outcomes.Count == 0 && source.Contains("SetEventFinished(", StringComparison.Ordinal))
        {
            outcomes.Add(new EventOutcomeRecord
            {
                EventId = eventId,
                OutcomeId = "implicit_finish",
                OutcomeKind = "heuristic",
                OutcomeSource = source
            });
        }

        return outcomes;
    }

    private static bool LooksLikeOutcome(string handlerSource)
    {
        if (string.IsNullOrWhiteSpace(handlerSource))
        {
            return false;
        }

        return handlerSource.Contains("StartCombat(", StringComparison.Ordinal)
            || handlerSource.Contains("SetEventFinished(", StringComparison.Ordinal)
            || handlerSource.Contains("RewardsCmd.", StringComparison.Ordinal)
            || handlerSource.Contains("RelicCmd.", StringComparison.Ordinal)
            || handlerSource.Contains("CardCmd.", StringComparison.Ordinal)
            || handlerSource.Contains("Potion", StringComparison.Ordinal);
    }
}
