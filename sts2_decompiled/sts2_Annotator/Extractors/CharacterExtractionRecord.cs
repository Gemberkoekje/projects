using System.Collections.Generic;

namespace Sts2Extractor.Extractors;

internal sealed class CharacterExtractionRecord
{
    public string FilePath { get; set; }

    public string CharacterId { get; set; }

    public int StartingHp { get; set; }

    public int StartingGold { get; set; }

    public int MaxEnergy { get; set; }

    public int OrbSlots { get; set; }

    public List<string> StartingDeck { get; set; }

    public List<string> StartingRelics { get; set; }

    public decimal ConfidenceScore { get; set; }

    public CharacterExtractionRecord()
    {
        FilePath = string.Empty;
        CharacterId = string.Empty;
        StartingHp = int.MinValue;
        StartingGold = int.MinValue;
        MaxEnergy = 3;
        OrbSlots = 0;
        StartingDeck = new List<string>();
        StartingRelics = new List<string>();
    }
}
