using System.Collections.Generic;

namespace Sts2Extractor.Extractors;

internal sealed class PowerExtractionRecord
{
    public string FilePath { get; set; }

    public string PowerId { get; set; }

    public string IdResolutionStatus { get; set; }

    public bool RoslynFallbackUsed { get; set; }

    public string PowerType { get; set; }

    public string StackType { get; set; }

    public bool IsPlayerPower { get; set; }

    public IReadOnlyList<string> HookNames { get; set; }

    public string HookSource { get; set; }

    public decimal ConfidenceScore { get; set; }

    public PowerExtractionRecord()
    {
        FilePath = string.Empty;
        PowerId = string.Empty;
        IdResolutionStatus = string.Empty;
        PowerType = string.Empty;
        StackType = string.Empty;
        HookNames = new List<string>();
        HookSource = string.Empty;
        IsPlayerPower = true;
    }
}
