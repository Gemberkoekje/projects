using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Sts2Extractor.Cli;
using Sts2Extractor.Infrastructure;

namespace Sts2Extractor.Audit;

internal sealed class PatternAuditRunner
{
    private readonly PatternInspector _inspector;

    public PatternAuditRunner()
    {
        _inspector = new PatternInspector();
    }

    public PatternAuditResult Run(CliOptions options)
    {
        List<string> files = new List<string>();
        files.AddRange(GetSampleFiles(options.RootPath, "MegaCrit.Sts2.Core.Models.Cards", options.CardSampleSize));
        files.AddRange(GetSampleFiles(options.RootPath, "MegaCrit.Sts2.Core.Models.Relics", options.RelicSampleSize));
        files.AddRange(GetSampleFiles(options.RootPath, "MegaCrit.Sts2.Core.Models.Powers", options.PowerSampleSize));

        List<PatternAuditRecord> records = new List<PatternAuditRecord>();
        foreach (string file in files)
        {
            string relative = Path.GetRelativePath(options.RootPath, file);
            string source = File.ReadAllText(file);
            PatternAuditRecord record = _inspector.Inspect(relative, source);
            record.FallbackRequired = record.ConfidenceScore < options.ConfidenceThreshold;
            records.Add(record);
        }

        CsvWriter.Write(options.OutputPath, new[]
        {
            "file",
            "entity_type",
            "fields_found",
            "fields_missing",
            "confidence_score",
            "fallback_required"
        }, records.Select(static record => (IReadOnlyList<string>)new[]
        {
            record.FilePath,
            record.EntityType.ToString(),
            string.Join(";", record.FieldsFound),
            string.Join(";", record.FieldsMissing),
            record.ConfidenceScore.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
            record.FallbackRequired ? "true" : "false"
        }));

        return new PatternAuditResult
        {
            OutputPath = options.OutputPath,
            RecordCount = records.Count,
            FallbackRequiredCount = records.Count(static x => x.FallbackRequired)
        };
    }

    private static IReadOnlyList<string> GetSampleFiles(string rootPath, string relativeDirectory, int limit)
    {
        string fullDirectory = Path.Combine(rootPath, relativeDirectory);
        if (!Directory.Exists(fullDirectory))
        {
            throw new DirectoryNotFoundException($"Missing expected directory: {fullDirectory}");
        }

        return Directory
            .EnumerateFiles(fullDirectory, "*.cs", SearchOption.TopDirectoryOnly)
            .OrderBy(static file => file, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .ToList();
    }
}
