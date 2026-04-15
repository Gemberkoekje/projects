using System.Collections.Generic;

namespace Sts2Extractor.Extractors;

internal sealed class RelicExtractionRecord
{
    public string FilePath { get; set; }

    public string RelicId { get; set; }

    public string IdResolutionStatus { get; set; }

    public bool RoslynFallbackUsed { get; set; }

    public string Rarity { get; set; }

    public int AmountBase { get; set; }

    public int CounterBase { get; set; }

    public IReadOnlyList<string> HookNames { get; set; }

    public string HookSource { get; set; }

    public decimal ConfidenceScore { get; set; }

    public RelicExtractionRecord()
    {
        FilePath = string.Empty;
        RelicId = string.Empty;
        IdResolutionStatus = string.Empty;
        Rarity = string.Empty;
        AmountBase = int.MinValue;
        CounterBase = int.MinValue;
        HookNames = new List<string>();
        HookSource = string.Empty;
    }
}
