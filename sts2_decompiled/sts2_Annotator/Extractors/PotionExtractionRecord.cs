using System.Collections.Generic;

namespace Sts2Extractor.Extractors;

internal sealed class PotionExtractionRecord
{
    public string FilePath { get; set; }

    public string PotionId { get; set; }

    public string IdResolutionStatus { get; set; }

    public bool RoslynFallbackUsed { get; set; }

    public string Rarity { get; set; }

    public string TargetType { get; set; }

    public int AmountBase { get; set; }

    public string OnUseSource { get; set; }

    public decimal ConfidenceScore { get; set; }

    public PotionExtractionRecord()
    {
        FilePath = string.Empty;
        PotionId = string.Empty;
        IdResolutionStatus = string.Empty;
        Rarity = string.Empty;
        TargetType = string.Empty;
        AmountBase = int.MinValue;
        OnUseSource = string.Empty;
    }
}
