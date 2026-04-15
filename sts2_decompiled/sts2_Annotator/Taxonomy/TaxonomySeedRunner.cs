using System;
using System.Collections.Generic;
using System.Linq;
using Sts2Extractor.Cli;
using Sts2Extractor.Infrastructure;

namespace Sts2Extractor.Taxonomy;

internal sealed class TaxonomySeedRunner
{
    public TaxonomySeedResult Run(CliOptions options)
    {
        List<EffectTaxonomySeedRecord> records = BuildSeedRecords();

        CsvWriter.Write(options.OutputPath, new[]
        {
            "tag",
            "category",
            "description"
        }, records.Select(static record => (IReadOnlyList<string>)new[]
        {
            record.Tag,
            record.Category,
            record.Description
        }));

        return new TaxonomySeedResult
        {
            OutputPath = options.OutputPath,
            RecordCount = records.Count
        };
    }

    private static List<EffectTaxonomySeedRecord> BuildSeedRecords()
    {
        return new List<EffectTaxonomySeedRecord>
        {
            new EffectTaxonomySeedRecord
            {
                Tag = "generate_stars",
                Category = "resource_generation",
                Description = "Generates Stars resource for follow-up card effects."
            },
            new EffectTaxonomySeedRecord
            {
                Tag = "consume_stars",
                Category = "resource_consumption",
                Description = "Consumes stored Stars resource to amplify or unlock effects."
            },
            new EffectTaxonomySeedRecord
            {
                Tag = "gain_forge",
                Category = "resource_generation",
                Description = "Gains Forge resource for Regent-specific mechanics."
            },
            new EffectTaxonomySeedRecord
            {
                Tag = "consume_forge",
                Category = "resource_consumption",
                Description = "Consumes Forge resource to pay for enhanced effects."
            },
            new EffectTaxonomySeedRecord
            {
                Tag = "osty_attack",
                Category = "companion_action",
                Description = "Triggers a direct Osty companion attack action."
            },
            new EffectTaxonomySeedRecord
            {
                Tag = "osty_damage_source",
                Category = "companion_action",
                Description = "Marks damage/effects sourced from Osty interactions."
            },
            new EffectTaxonomySeedRecord
            {
                Tag = "companion_synergy",
                Category = "companion_synergy",
                Description = "General tag for companion-linked synergy behavior."
            }
        };
    }
}
