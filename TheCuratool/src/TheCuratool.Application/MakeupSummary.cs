using TheCuratool.Domain;

namespace TheCuratool.Application;

public sealed record MakeupSummary(
    SetupCounts CurrentCounts,
    IReadOnlyList<SetupCounts> TargetCounts,
    IReadOnlyDictionary<CharacterType, IReadOnlyList<string>> ChosenCharactersByType,
    int RemainingSeats,
    IReadOnlyList<string> OutOfScriptCharacterIds);
