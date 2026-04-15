using System.Collections.Generic;

namespace Sts2Extractor.Extractors;

internal sealed class AdvisePickResult
{
    public List<PickScoreRecord> Scores { get; set; }

    public string LlmNarrative { get; set; }

    public AdvisePickResult()
    {
        Scores = new List<PickScoreRecord>();
        LlmNarrative = string.Empty;
    }
}
